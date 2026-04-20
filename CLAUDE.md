# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 專案概述

SktVegapunk 是一個 PowerBuilder → C# 遷移工具，分兩條流程：
- **`--spec-source` 模式**：靜態解析 PB/JSP/Schema 產出 35+ 種 artifact（JSON + Markdown）
- **`--source` 模式**：抽取 PB 事件區塊 → GitHub Copilot SDK 生成 C# → build/repair loop 驗證

## 專案結構

| 專案 | 職責 |
|------|------|
| `SktVegapunk.Core` | 所有業務邏輯：Spec pipeline、Migration pipeline、GitHub Copilot SDK 封裝、資料模型 |
| `SktVegapunk.Console` | CLI 入口，解析參數、載入設定、組裝物件、呼叫 Core |
| `SktVegapunk.Web` | 本機 Web UI，包裝 Console 流程，支援 SSE log 串流、artifact 預覽、prompt template 生成 |
| `SktVegapunk.Tests` | xUnit 測試，覆蓋率用 coverlet |

全域設定（`Directory.Build.props`）：`Nullable=enable`、`TreatWarningsAsErrors=true`、`TargetFramework=net10.0`。

## 常用指令

```bash
# 建置
dotnet build SktVegapunk.slnx

# 測試
dotnet test SktVegapunk.slnx
dotnet test --logger "console;verbosity=detailed"   # 詳細輸出
dotnet test --collect:"XPlat Code Coverage"         # 含覆蓋率

# 格式化
dotnet format SktVegapunk.slnx --verify-no-changes  # 只檢查
dotnet format SktVegapunk.slnx                      # 自動修復

# 執行（spec 模式）
dotnet run --project SktVegapunk.Console -- \
  --spec-source "<source-dir>" \
  --spec-output "<output-dir>"

# 執行（migration 模式）
dotnet run --project SktVegapunk.Console -- \
  --source "<pb-file>" \
  --output "<generated-cs-file>" \
  --target-project "<project-or-sln>"

# Web UI
dotnet run --project SktVegapunk.Web
```

## 驗證設定

Migration 模式需要 GitHub Copilot 身分（spec 模式不需要）：
```bash
copilot login                  # 方法一：本機 CLI 登入
dotnet user-secrets set "GitHubCopilot:GitHubToken" "<token>" --project SktVegapunk.Console  # 方法二
```

設定優先順序：`appsettings.json` → `user-secrets` → 環境變數（`__` 作階層分隔，如 `Agent__ModelName`）。

## 架構重點

### Spec Pipeline（`--spec-source`）

`SktVegapunk.Core/Pipeline/Spec/SpecArtifactsGenerator.cs` 協調整個流程：
1. `PbSourceNormalizer`：自動偵測 BOM（含錯誤 BOM `C3 BF C3 BE`），以 UTF-16LE 解碼 PB 檔
2. `SrdExtractor` / `SruExtractor` / `JspExtractor`：確定性解析各來源檔案
3. `SpecReportBuilder`：組裝 `MigrationSpec`，對齊 JSP→PB→DataWindow 繼承鏈
4. `SchemaExtractor`：解析 Sybase ASE DDL（ISO-8859-1 編碼，不走 PbSourceNormalizer）
5. `SchemaReconciliationAnalyzer`：跨多 DataWindow 累加欄位後比對型別（按類別而非逐字）
6. `EndpointDataWindowAnalyzer`：建立 resolved endpoint → DataWindow 交叉索引
7. `UnresolvedEndpointInferrer`（選配）：設定 `Agent:ModelName` 後啟用 LLM 推導，產出 `inferred-endpoints.*`

### Migration Pipeline（`--source`）

`MigrationOrchestrator` 協調：
1. `PbSourceNormalizer` 正規化原始碼
2. `PbScriptExtractor` 抽取 `event`/`on` 區塊 → `PbEventBlock`（無區塊則直接失敗）
3. `PromptBuilder` 組裝 user prompt
4. `CopilotCodeGenerator` → `GitHubCopilotClient` → GitHub Copilot SDK `SendAndWaitAsync`
5. 寫入輸出檔 → `DotnetBuildValidator`（build + 可選 test）
6. 失敗則進入 repair loop，最多 `MaxRetries` 次

### Web UI（`SktVegapunk.Web`）

Minimal API + 靜態檔案架構，無 MVC/Razor。核心機制：
- `ConcurrentDictionary<string, Channel<string>>` 對應每次執行的 SSE log 串流
- `CopilotClient` 單例懶啟動（用 `SemaphoreSlim` 保護）
- 前端位於 `wwwroot/`，包含 `prompt-template.md`（中文）與 `prompt-template-en.md`（英文）

## 編碼規範

### 業務邏輯位置
- 邏輯一律放 `Core`，I/O 與入口流程放 `Console`
- HTTP 呼叫使用 `IHttpClientFactory` 模式

### 註解風格
- **Public API**：XML doc，`<summary>` 一句話說明目的；有使用前提或副作用才加 `<remarks>`
- **方法內部**：`//` 中文行內，說明「為什麼」而非逐字翻譯程式碼

```csharp
/// <summary>
/// 執行整個遷移流程，從提取到驗證，並在失敗時依設定重試。
/// </summary>
public Task<MigrationResult> RunAsync(MigrationRequest request) { ... }

// 建立 OpenRouterClient，負責與 OpenRouter API 的 HTTP 溝通
var openRouterClient = new OpenRouterClient(httpClient, apiKey);
```

## 文件維護規則

- **`docs/PUNK_RECORDS.md`**：架構取捨與已知限制，只記錄不易從程式碼或 git history 看出的決策
- **`README.md`**：使用者可見的執行指令、設定鍵、操作方式，有變動必須同步更新

## 已知限制

- Migration 模式僅抽取事件區塊，沒有 AST/語意分析，也不自動注入 spec artifacts
- Schema DDL 硬編碼 ISO-8859-1；若 schema 檔改用 UTF-8 須調整 `SpecArtifactsGenerator`
- Frontend Generation Agent 尚未落地，目前提供 prompt template 與 JSP prototype artifacts 作為輸入
