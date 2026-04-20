# SktVegapunk

## 快速開始

### 1. 事前準備

- [.NET SDK 10.0.103](https://dotnet.microsoft.com/download)
- [GitHub Copilot CLI](https://docs.github.com/en/copilot/how-tos/copilot-cli/set-up-copilot-cli)
- 可使用 GitHub Copilot 的 GitHub 帳號

### 2. Clone 專案

```bash
git clone <repo-url>
cd skt-vegapunk
```

### 3. 設定 GitHub Copilot 驗證

本專案改用 **GitHub Copilot SDK for .NET**，SDK 會透過本機 `copilot` CLI 建立 session。
你有兩種驗證方式：

```bash
copilot login
```

或使用 `dotnet user-secrets` 提供 token 給 SDK：

```bash
dotnet user-secrets set "GitHubCopilot:GitHubToken" "你的 GitHub Token" --project SktVegapunk.Console
```

確認 user-secrets 已設定：

```bash
dotnet user-secrets list --project SktVegapunk.Console
```

### 4. 調整設定（選用）

模型與系統提示詞定義在 `SktVegapunk.Console/appsettings.json`，直接編輯即可：

- gpt-5
- claude-sonnet-4.5
- gemini-2.5-pro


```json
{
  "Agent": {
    "ModelName": "gpt-5",
    "SystemPrompt": "你是一個資深的 .NET 開發者。..."
  },
  "GitHubCopilot": {
    "CliPath": "copilot"
  },
  "Pipeline": {
    "MaxRetries": 3,
    "RunTestsAfterBuild": false,
    "BuildConfiguration": "Debug"
  }
}
```

也可以在執行時用環境變數臨時覆蓋，無需修改檔案（`__` 代表階層分隔）：

```bash
# macOS / Linux
Agent__ModelName="claude-sonnet-4.5" dotnet run --project SktVegapunk.Console

# Windows PowerShell
$env:Agent__ModelName="claude-sonnet-4.5"; dotnet run --project SktVegapunk.Console
```

**設定優先順序（後者蓋前者）：**

```
appsettings.json → user-secrets → 環境變數
```

| 設定 | 位置 | 說明 |
|---|---|---|
| `Agent:ModelName` | `appsettings.json` | 使用的 AI 模型，進版控；`--spec-source` 模式若有設定則啟用 LLM 推導 |
| `Agent:SystemPrompt` | `appsettings.json` | 系統提示詞（`--source` 模式專用），進版控 |
| `GitHubCopilot:CliPath` | `appsettings.json` / 環境變數 | Copilot CLI 路徑，預設 `copilot` |
| `GitHubCopilot:WorkingDirectory` | user-secrets / 環境變數 | 啟動 Copilot CLI 的工作目錄，未設定時使用目前目錄 |
| `GitHubCopilot:GitHubToken` | user-secrets | 提供給 SDK 的 GitHub Token，**不進版控** |
| `Pipeline:MaxRetries` | `appsettings.json` | 編譯失敗時最多重試次數 |
| `Pipeline:RunTestsAfterBuild` | `appsettings.json` | build 成功後是否再跑 `dotnet test` |
| `Pipeline:BuildConfiguration` | `appsettings.json` | `dotnet build/test` 的組態（Debug/Release） |

### 5. 執行

#### 5.1 「程式碼遷移」模式，必填 3 個參數：

```bash
  dotnet run --project SktVegapunk.Console -- \
    --source "<pb-file>" \
    --output "<generated-cs-file>" \
    --target-project "<project-or-sln>"
```
  - --source：PowerBuilder 原始檔路徑
  - --output：要輸出的 C# 檔案路徑
  - --target-project：驗證用的 .csproj 或 .sln/.slnx 路徑

例如：

```bash
  dotnet run --project SktVegapunk.Console -- \
    --source "source/sign/w_signin.srw" \
    --output "output/SigninService.cs" \
    --target-project "SktVegapunk.slnx"
```

#### 5.2 「規格拆解 / artifact 產出」模式，必填 2 個參數：

```bash
  dotnet run --project SktVegapunk.Console -- \
    --spec-source "<source-dir>" \
    --spec-output "<output-dir>"
```
  - --spec-source：來源資料夾
  - --spec-output：輸出資料夾

例如：

```bash
  dotnet run --project SktVegapunk.Console -- \
    --spec-source "source/sign" \
    --spec-output "output/specs"
```
- 輸出目錄的 `spec/INDEX.md` 會說明各 artifact 的用途與建議閱讀順序。
- 若 `appsettings.json` 已設定 `Agent:ModelName`，流程會在靜態分析結束後對 unresolved endpoint 自動呼叫 LLM 推導，輸出 `spec/inferred-endpoints.md` 與 `spec/inferred-endpoints.json`。未設定時略過，不影響其他 artifact。
- --source 模式和 --spec-source 模式是二選一，不是混用。
- -- 不能省，因為它是把後面的參數傳給你的 Console 程式，而不是傳給 dotnet run 本身。

### 6. Format

Ctrl + Shift + P → Tasks: Run Task → Format

```bash
# 檢查（不改檔案）
dotnet format --verify-no-changes

# 自動修復所有（空格、using、命名等）
dotnet format
```

### 7. Testing

測試框架：**xUnit**，覆蓋率收集：**coverlet**。

```bash
# 執行所有測試
dotnet test

# 執行並顯示詳細輸出
dotnet test --logger "console;verbosity=detailed"

# 執行並收集程式碼覆蓋率
dotnet test --collect:"XPlat Code Coverage"
```

測試結果與覆蓋率報告會輸出至各專案的 `TestResults/` 目錄（已加入 `.gitignore`，不進版控）。

### 8. Build

```bash
# Debug 建置（開發用）
dotnet build

# Release 建置（最佳化）
dotnet build -c Release

# 發佈（產生可獨立執行的二進位檔）
dotnet publish SktVegapunk.Console -c Release
```

> `Directory.Build.props` 已全域開啟 `TreatWarningsAsErrors`，任何警告都會中止建置。

## 執行前注意事項

- 若未設定 `GitHubCopilot:GitHubToken`，SDK 會改用本機已登入的 `copilot` CLI 身分。
- 第一次 restore `GitHub.Copilot.SDK` 時，套件可能會下載其相依的 Copilot CLI 資產。

## 專案結構

```
skt-vegapunk/
├── global.json
├── Directory.Build.props
├── SktVegapunk.slnx
├── SktVegapunk.Console/
│   └── SktVegapunk.Console.csproj
├── SktVegapunk.Core/
│   └── SktVegapunk.Core.csproj
└── SktVegapunk.Tests/
    └── SktVegapunk.Tests.csproj
```

## Docs

| 檔名 | 說明 |
|---|---|
| `1 ─ 4` | 概念 |
| `1 - Multi-Agent System.md` | 多代理分工與目前落地進度 |
| `AI_TO_COPILOT_FLOW.md` | `--source` 生成路徑到 GitHub Copilot SDK 的實際流程 |
| `copilot-sdk-csharp.instructions.md` | 本專案使用 GitHub Copilot SDK 的 C# 用法備忘 |
| `Methodology/README.md` | `AI_Migration_Methodology.md` 的拆分索引 |
| `PROGRAM_FLOW.md` | 目前流程圖 |
| `PUNK_RECORDS.md` | 目前進度 |

## 目前進度

**Analysis Agent（已完整落地）**
- `PbSourceNormalizer`：自動偵測 BOM、UTF-16LE 解碼
- `SrdExtractor` / `SruExtractor` / `JspExtractor`：確定性解析 `.srd`、`.sru`、`.jsp`
- `JspPrototypeExtractor`：產出 HTML/JS/CSS prototype 與控制項清單（control inventory）
- `SpecReportBuilder`：組裝 `MigrationSpec`，對齊 JSP→PB→DataWindow 繼承鏈
- `SchemaExtractor`：解析 Sybase ASE DDL（174 張表、20 個 Trigger）
- `SchemaReconciliationAnalyzer`：比對 SrdSpec 欄位型別與 DB Schema，跨多 DataWindow 累加比對
- `EndpointDataWindowAnalyzer`：建立 resolved endpoint → DataWindow 交叉索引
- 一次 `--spec-source` 可產出 35+ 種 artifact（JSON + Markdown），不呼叫任何 LLM

**Backend Generation Agent（基礎可用）**
- `MigrationOrchestrator` + `CopilotCodeGenerator`（GitHub Copilot SDK）+ `DotnetBuildValidator`
- 以 `PbSourceNormalizer` 正規化 PB 原始碼，`PbScriptExtractor` 提取事件區塊後送交 Copilot
- 支援 build/repair loop，最多重試 `MaxRetries` 次

**尚未實作**
- `Decoupling Agent`：UI / 業務邏輯 / 資料存取的真正三層拆分
- `Frontend Generation Agent`：Vue 3 元件生成（JSP prototype 已備妥作為輸入）
- `Testing Agent`：自動生成單元測試
