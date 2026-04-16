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
| `Agent:ModelName` | `appsettings.json` | 使用的 AI 模型，進版控 |
| `Agent:SystemPrompt` | `appsettings.json` | 系統提示詞，進版控 |
| `GitHubCopilot:CliPath` | `appsettings.json` / 環境變數 | Copilot CLI 路徑，預設 `copilot` |
| `GitHubCopilot:WorkingDirectory` | user-secrets / 環境變數 | 啟動 Copilot CLI 的工作目錄，未設定時使用目前目錄 |
| `GitHubCopilot:GitHubToken` | user-secrets | 提供給 SDK 的 GitHub Token，**不進版控** |
| `Pipeline:MaxRetries` | `appsettings.json` | 編譯失敗時最多重試次數 |
| `Pipeline:RunTestsAfterBuild` | `appsettings.json` | build 成功後是否再跑 `dotnet test` |
| `Pipeline:BuildConfiguration` | `appsettings.json` | `dotnet build/test` 的組態（Debug/Release） |

### 5. 執行

```bash
dotnet run --project SktVegapunk.Console -- \
  --source "/path/to/window.srw" \
  --output "/path/to/GeneratedController.cs" \
  --target-project "SktVegapunk.slnx"
```

### 5.1 產出規格報告與中介資料

可直接掃描來源資料夾，將規格報告與中介 JSON 輸出到指定目錄：

```bash
dotnet run --project SktVegapunk.Console -- \
  --spec-source "source/sign" \
  --spec-output "output/sign"
```

輸出內容會寫到 `output/<name>/spec/`，包含：

- `report.md`
- `unresolved-causes.md`
- `generation-phase-plan.md`
- `control-inventory.md`
- `control-inventory.json`
- `request-bindings.md`
- `request-bindings.json`
- `payload-mappings.md`
- `payload-mappings.json`
- `response-classifications.md`
- `response-classifications.json`
- `page-flow.md`
- `page-flow.json`
- `interaction-graph.md`
- `interaction-graph.json`
- `datawindows/**/*.json`
- `components/**/*.json`
- `jsp/**/*.html`
- `jsp/**/*.js`
- `jsp/**/*.css`
  - `jsp/**/*.json`
  - 其中 `jsp/**/*.json` 會包含 `forms`、`controls` 與 `events`，目前已抽出 `Click`、`FormActionChange`、`Submit`、`Ajax`、`OpenWindow`、`Navigate`
  - `unresolved-causes.md` 會把無法解析的 endpoint 保留為 deferred placeholder，方便先進 generation phase
- `schema/tables/*.json`（DB 資料表結構：欄位型別、PK、索引、CHECK 約束）
- `schema/triggers/*.json`（Trigger 清單，含事件類型與 body）
- `schema/relationships.json`（所有 FK 關聯彙整）
- `schema/indexes.json`（所有 standalone index 彙整）
- `schema-reconciliation.md` / `schema-reconciliation.json`（SrdSpec 欄位 vs DB Schema 差異，跨多個 DataWindow 累加比對）
- `endpoint-datawindow-map.md` / `endpoint-datawindow-map.json`（resolved endpoint → DataWindow 對應表）
  - 以上 schema artifacts 需在來源目錄中有 `schema/` 子目錄（放置 `.sql` DDL 檔案）才會產生
  - `generation-phase-plan.md` 會整理後端與前端進入 generation phase 的現況、placeholder 與生成順序
  - `control-inventory.*` 會把 `input/select/textarea/button/a` 抽成結構化控制項清單
  - `request-bindings.*` 會把 JSP component call 的 PB 參數來源、form submit 與 ajax payload 摘要整理成可供後端生成使用的橋接資料，並追蹤 `getBytes(...)` 這類 blob 來源
  - `payload-mappings.*` 會把 form submit / ajax 的 payload keys、expression 與 control 來源攤平成獨立清單
  - `response-classifications.*` 會把 endpoint 依線索分類為 `json`、`html`、`file`、`script-redirect`、`text`
  - `page-flow.*` 會把 `events` 進一步推導成 `JSP -> JSP/API/HTML` 的流程邊
  - `interaction-graph.*` 會把 `Click -> handler -> submit/ajax/openWindow/navigate` 串成互動事件鏈
- `warnings.md`（僅有警告時才會產生）



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
