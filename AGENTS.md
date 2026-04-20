# SktVegapunk Agent Guide

## Repo Overview

- `SktVegapunk.Console`: 主控台入口，支援兩種模式：`--spec-source/--spec-output` 規格提取，與 `--source/--output/--target-project` PB 後端生成。
- `SktVegapunk.Web`: 本機 Web UI，負責啟動 Console、串流 log、預覽 Markdown artifacts 與生成 prompt template。
- `SktVegapunk.Core`: 規格提取流水線（Spec Pipeline）、GitHub Copilot SDK 封裝、遷移編排與資料模型。
- `SktVegapunk.Tests`: xUnit 測試專案，目前 55 tests。
- 全域設定在 `Directory.Build.props`：`Nullable=enable`、`TreatWarningsAsErrors=true`、`TargetFramework=net10.0`。

## Local Setup

1. 使用 `global.json` 指定的 SDK（目前 `10.0.103`）。
2. 設定 GitHub Copilot token（僅 migration 模式需要，不要寫入程式碼或 `appsettings.json`）：
   - `dotnet user-secrets set "GitHubCopilot:GitHubToken" "<your-token>" --project SktVegapunk.Console`
   - 或直接執行 `copilot login` 使用本機 CLI 身分
3. 驗證 secrets：
   - `dotnet user-secrets list --project SktVegapunk.Console`

## Configuration Rules

- 目前 `Program.cs` 使用的載入順序是：
  1. `appsettings.json`
  2. `AddUserSecrets<Program>()`
  3. `AddEnvironmentVariables()`
- 依 Microsoft Learn，後載入者覆蓋先載入者。
- 環境變數階層鍵使用 `__`，例如 `Agent__ModelName` 對應 `Agent:ModelName`。

## Build/Test/Format Commands

- `dotnet run --project SktVegapunk.Console`
- `dotnet run --project SktVegapunk.Web`
- `dotnet build SktVegapunk.slnx`
- `dotnet test SktVegapunk.slnx`
- `dotnet format SktVegapunk.slnx --verify-no-changes`

## Coding Expectations

- 不提交任何 secrets。
- 保持可測試性：業務邏輯放在 `Core`，I/O 與入口流程放在 `Console`。
- HTTP 呼叫若要擴充，優先採用 Microsoft Learn 建議的 `IHttpClientFactory` 模式，不要每次請求建立/銷毀 client。
- 變更後至少要能通過 `build` 與 `test`。

## Documentation & Reporting Rules

- 每次完成「有改動程式碼或流程」的實作後，若有架構層面的非顯然決策，補記到 `docs/PUNK_RECORDS.md`。
- `docs/PUNK_RECORDS.md` 只記錄不易從程式碼或 git history 直接看出的資訊：
  - 架構取捨與當下原因
  - 已知限制與建議後續
  - 不需要記錄「逐步驗證流程」或「每次 build/test 通過」之類的操作細節
- `README.md` 必須同步更新所有「使用者會受影響」的內容，例如：
  - 執行指令與參數
  - 新增/變更設定鍵
  - 新流程的操作方式與必要說明
  - Web UI 的可用功能、限制與新增文件入口

## Comment Style

### 局部實作（方法內部）
使用 `//` 中文行內註解，說明「為什麼這樣做」或「這個物件負責什麼」，而非逐字翻譯程式碼本身。
每個邏輯區塊以一行空白隔開，並在第一行放置簡短說明。必要時可用多行，但應保持精簡：

```csharp
// 建立 OpenRouterClient，負責與 OpenRouter API 的 HTTP 溝通
var openRouterClient = new OpenRouterClient(httpClient, apiKey);
```

### Public API（類別、方法、屬性）
使用 XML doc，`<summary>` 以一句話說明「這個成員的目的是什麼」。直接從目的開始，省略「這個方法會…」等贅詞。
若有使用前提、副作用或注意事項，才加 `<remarks>`；非必要請省略：

```csharp
/// <summary>
/// 執行整個遷移流程，從提取到驗證，並在失敗時依設定重試。
/// </summary>
/// <remarks>
/// 超過 <see cref="MigrationRequest.MaxRetries"/> 次後仍失敗，
/// 回傳 <see cref="MigrationState.Failed"/> 而非拋出例外。
/// </remarks>
public Task<MigrationResult> RunAsync(MigrationRequest request) { ... }
```
