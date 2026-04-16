# Source 到 GitHub Copilot SDK 的生成流程

這份文件只描述 `--source` 的 migration 生成路徑，也就是：

```bash
dotnet run --project SktVegapunk.Console -- \
  --source "/path/to/source.sru" \
  --output "/path/to/Generated.cs" \
  --target-project "SktVegapunk.slnx"
```

不包含 `--spec-source` / `--spec-output` 的規格提取流程。

## 流程總覽

```text
CLI 參數
  ↓
Program.cs 載入設定與建立 orchestrator
  ↓
MigrationOrchestrator 讀取 source 檔
  ↓
PbScriptExtractor 從原始文字抽出 event/on 區塊
  ↓
PromptBuilder 組成 user prompt
  ↓
CopilotCodeGenerator
  ↓
GitHubCopilotClient
  ↓
GitHub Copilot SDK session.SendAndWaitAsync(...)
  ↓
回傳 generated C# code
  ↓
寫入 output 檔案
  ↓
dotnet build / dotnet test 驗證
  ↓
成功結束，或帶著錯誤訊息重組 repair prompt 再送一次
```

## 1. CLI 入口與設定載入

入口在 [SktVegapunk.Console/Program.cs](/home/carl/git/skt-vegapunk/SktVegapunk.Console/Program.cs:32)。

程式先解析三個 migration 必要參數：

- `--source`
- `--output`
- `--target-project`

接著從設定系統讀取：

- `Agent:SystemPrompt`
- `Agent:ModelName`
- `GitHubCopilot:GitHubToken`
- `GitHubCopilot:CliPath`
- `GitHubCopilot:WorkingDirectory`
- `Pipeline:MaxRetries`
- `Pipeline:RunTestsAfterBuild`
- `Pipeline:BuildConfiguration`

目前預設值定義在 [SktVegapunk.Console/appsettings.json](/home/carl/git/skt-vegapunk/SktVegapunk.Console/appsettings.json:1)。

## 2. 建立生成流程物件

`Program.cs` 會建立以下核心物件：

- `GitHubCopilotClient`
- `CopilotCodeGenerator`
- `MigrationOrchestrator`
- `DotnetBuildValidator`

然後把 CLI 與設定組成 `MigrationRequest`，交給 `MigrationOrchestrator.RunAsync(...)`。

相關程式位置：

- [SktVegapunk.Console/Program.cs](/home/carl/git/skt-vegapunk/SktVegapunk.Console/Program.cs:49)
- [SktVegapunk.Core/Pipeline/MigrationRequest.cs](/home/carl/git/skt-vegapunk/SktVegapunk.Core/Pipeline/MigrationRequest.cs:1)

## 3. 讀取並正規化 source 原始碼

實際讀檔與正規化發生在 [SktVegapunk.Core/Pipeline/MigrationOrchestrator.cs](/home/carl/git/skt-vegapunk/SktVegapunk.Core/Pipeline/MigrationOrchestrator.cs:48)：

```csharp
var rawBytes = await _textFileStore.ReadAllBytesAsync(request.SourceFilePath, cancellationToken);
var sourceArtifact = _sourceNormalizer.Normalize(rawBytes, request.SourceFilePath);
```

`PbSourceNormalizer` 會自動偵測 BOM（含錯誤 BOM `C3 BF C3 BE`）並以 UTF-16LE 解碼，失敗時回傳 warning 而非丟例外。

`ISourceNormalizer` 為可選建構子參數，預設使用 `PbSourceNormalizer`，測試可注入 stub。

相關程式位置：

- [SktVegapunk.Core/Pipeline/MigrationOrchestrator.cs](/home/carl/git/skt-vegapunk/SktVegapunk.Core/Pipeline/MigrationOrchestrator.cs:1)
- [SktVegapunk.Core/Pipeline/FileTextStore.cs](/home/carl/git/skt-vegapunk/SktVegapunk.Core/Pipeline/FileTextStore.cs:5)
- [SktVegapunk.Core/Pipeline/PbSourceNormalizer.cs](/home/carl/git/skt-vegapunk/SktVegapunk.Core/Pipeline/PbSourceNormalizer.cs:1)

### 這一步的特性

- 先以 `ReadAllBytesAsync` 讀取 raw bytes，再交給 `PbSourceNormalizer` 正規化編碼
- 與 spec 路徑（`SpecArtifactsGenerator`）使用相同的 normalizer，編碼處理一致
- 若 source 有無法解碼的區段，回傳 `SourceArtifact.Warnings`，不中斷流程

## 4. 從原始文字抽出可轉換區塊

讀入完整文字後，不是把整份檔案原封不動送模型，而是先交給 [SktVegapunk.Core/Pipeline/PbScriptExtractor.cs](/home/carl/git/skt-vegapunk/SktVegapunk.Core/Pipeline/PbScriptExtractor.cs:7)。

`PbScriptExtractor` 的規則很單純：

- 遇到 `event xxx` 或 `on xxx` 視為區塊開始
- 遇到 `end event` 或 `end on` 視為區塊結束
- 區塊內原始行文會保留縮排與內容
- 每個區塊會變成一筆 `PbEventBlock`

如果最後沒有抽出任何事件區塊，流程會直接失敗，不會呼叫模型。

## 5. 組出送給模型的 prompt

`MigrationOrchestrator` 把抽出的 `eventBlocks` 交給 [SktVegapunk.Core/Pipeline/PromptBuilder.cs](/home/carl/git/skt-vegapunk/SktVegapunk.Core/Pipeline/PromptBuilder.cs:7)。

這一步會建立 `userPrompt`，格式如下：

```text
以下是從 PowerBuilder 提取的事件邏輯，請轉換為可編譯的 C# 後端程式碼。
請保留業務邏輯語意，並輸出完整程式碼。

[Event:event_name]
...script body...
[/Event]
```

兩個 prompt 來源分工如下：

- `systemPrompt`: 來自設定檔
- `userPrompt`: 來自 `PromptBuilder` 包好的事件區塊

這代表目前送給模型的是「抽出的事件腳本」，不是整份 `source` 原文。

## 6. 透過 GitHub Copilot SDK 送出請求

`MigrationOrchestrator` 進入 `Generating` 狀態後，會呼叫 [SktVegapunk.Core/Pipeline/CopilotCodeGenerator.cs](/home/carl/git/skt-vegapunk/SktVegapunk.Core/Pipeline/CopilotCodeGenerator.cs:20)。

`CopilotCodeGenerator` 只是薄包裝，真正送出請求的是 [SktVegapunk.Core/GitHubCopilotClient.cs](/home/carl/git/skt-vegapunk/SktVegapunk.Core/GitHubCopilotClient.cs:72)。

SDK 呼叫流程如下：

1. `EnsureStartedAsync()` 啟動 `CopilotClient`
2. `CreateSessionAsync(...)` 建立 session
3. 用 `SystemMessageMode.Replace` 放入 `systemPrompt`
4. 用 `MessageOptions.Prompt` 放入 `userPrompt`
5. 呼叫 `session.SendAndWaitAsync(...)`
6. 讀取 `response?.Data.Content`

目前 session 設定還包含：

- `Model = modelName`
- `OnPermissionRequest = PermissionHandler.ApproveAll`

## 7. 寫入 generated code

模型回傳內容後，`MigrationOrchestrator` 會先檢查是否為空字串；非空才會寫到 `request.OutputFilePath`。

寫入位置：

- [SktVegapunk.Core/Pipeline/MigrationOrchestrator.cs](/home/carl/git/skt-vegapunk/SktVegapunk.Core/Pipeline/MigrationOrchestrator.cs:91)
- [SktVegapunk.Core/Pipeline/FileTextStore.cs](/home/carl/git/skt-vegapunk/SktVegapunk.Core/Pipeline/FileTextStore.cs:10)

## 8. build 驗證與 test 驗證

寫完輸出檔後，流程進入 `Validating` 狀態，交給 [SktVegapunk.Core/Pipeline/DotnetBuildValidator.cs](/home/carl/git/skt-vegapunk/SktVegapunk.Core/Pipeline/DotnetBuildValidator.cs:13)。

驗證規則：

- 一定先跑 `dotnet build "<target>" -c <configuration>`
- 若 `RunTestsAfterBuild = true`，再跑 `dotnet test "<target>" -c <configuration>`
- 任何一步失敗，都會把 stdout / stderr / exit code 組成 `validationOutput`

## 9. repair loop

如果 build 或 test 失敗，流程不會立刻結束，而是進入 `Repairing` 狀態。

這時候 [PromptBuilder.cs](/home/carl/git/skt-vegapunk/SktVegapunk.Core/Pipeline/PromptBuilder.cs:31) 會建立第二輪 prompt，內容包含三塊：

- 原始轉換需求
- 前一次輸出的 C# 程式碼
- 驗證輸出

然後再送回 GitHub Copilot SDK 重新生成。

這個迴圈最多執行 `MaxRetries` 次；若超過次數仍失敗，就回傳 `MigrationState.Failed`。

## 10. 目前流程的邊界

這條 migration 路徑目前有幾個明確邊界：

- 沒有做 AST 或語意層分析，只做事件區塊抽取
- 沒有自動整合 `.srd`、`.jsp`、schema 或其他上下文一起送模型
- prompt 的主要輸入仍是 PowerBuilder event script 本身（不含 spec artifacts）
- spec 路徑（`--spec-source`）產出的中介資料目前需要人工引用，尚未自動注入 migration prompt

## 11. 關鍵檔案索引

- [SktVegapunk.Console/Program.cs](/home/carl/git/skt-vegapunk/SktVegapunk.Console/Program.cs:1)
- [SktVegapunk.Console/appsettings.json](/home/carl/git/skt-vegapunk/SktVegapunk.Console/appsettings.json:1)
- [SktVegapunk.Core/Pipeline/MigrationOrchestrator.cs](/home/carl/git/skt-vegapunk/SktVegapunk.Core/Pipeline/MigrationOrchestrator.cs:1)
- [SktVegapunk.Core/Pipeline/PbScriptExtractor.cs](/home/carl/git/skt-vegapunk/SktVegapunk.Core/Pipeline/PbScriptExtractor.cs:1)
- [SktVegapunk.Core/Pipeline/PromptBuilder.cs](/home/carl/git/skt-vegapunk/SktVegapunk.Core/Pipeline/PromptBuilder.cs:1)
- [SktVegapunk.Core/Pipeline/CopilotCodeGenerator.cs](/home/carl/git/skt-vegapunk/SktVegapunk.Core/Pipeline/CopilotCodeGenerator.cs:1)
- [SktVegapunk.Core/GitHubCopilotClient.cs](/home/carl/git/skt-vegapunk/SktVegapunk.Core/GitHubCopilotClient.cs:1)
- [SktVegapunk.Core/Pipeline/DotnetBuildValidator.cs](/home/carl/git/skt-vegapunk/SktVegapunk.Core/Pipeline/DotnetBuildValidator.cs:1)
