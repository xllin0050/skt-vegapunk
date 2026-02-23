# PUNK RECORDS

## 實作摘要
- 完成第一版「單檔 PB 後端轉換 PoC」。
- 流程已落地為：`Preprocessing -> Generating -> Validating -> Repairing`。
- 支援回饋迴圈（build 失敗時自動帶錯誤訊息重試，直到上限）。

### 2026-02-23 Phase 0：編碼正規化
- 新增 `ISourceNormalizer` / `PbSourceNormalizer`，支援錯誤 BOM (`C3 BF C3 BE`) 自動跳過並以 UTF-16LE 解碼，失敗時回傳 warning 不丟例外。
- 新增 `SourceArtifact` record；`ITextFileStore` 擴充 `ReadAllBytesAsync`，並更新 `FileTextStore` 與測試 stub。
- 新增 `PbSourceNormalizerTests`（含 `d_signkind.srd`、`n_sign.sru` golden 取樣解碼）。
- `MigrationState` 預先納入 `Normalizing`/`Analyzing` 枚舉值供後續擴充。
- 測試：本機 build 成功；`dotnet test` 因 sandbox Socket 限制無法啟動 vstest，需在 CI 或可開啟 socket 的環境重跑。

## 本次範圍
- 僅後端 PoC。
- 僅單檔輸入（`.srw` / `.sru`）。
- 不含 RAG、不含前端 Vue 轉換、不含多代理並行調度。

## 核心架構變更

### 1) OpenRouter Client 重構（DIP）
- `SktVegapunk.Core/OpenRouterClient.cs`
- 變更重點：
  - 改為注入 `HttpClient`，移除內部 `new HttpClient()`。
  - `SendMessageAsync` 增加 `CancellationToken`。
  - 保留並統一設定 `Authorization`、`HTTP-Referer`、`X-Title`。

### 2) Pipeline 模組新增
- 新增資料模型與介面：
  - `PbEventBlock`
  - `MigrationRequest`, `MigrationResult`, `MigrationState`
  - `ICodeGenerator`, `IPbScriptExtractor`, `IPromptBuilder`
  - `IBuildValidator`, `IProcessRunner`, `ITextFileStore`
- 新增實作：
  - `PbScriptExtractor`：逐行狀態機提取 `event/on ... end event/end on` 區塊。
  - `PromptBuilder`：初始 prompt 與修復 prompt 組裝。
  - `OpenRouterCodeGenerator`：封裝模型呼叫。
  - `ProcessRunner`：執行子程序命令。
  - `DotnetBuildValidator`：執行 `dotnet build`，可選擇串接 `dotnet test`。
  - `FileTextStore`：檔案讀寫抽象。
  - `MigrationOrchestrator`：流程編排與重試控制。

### 3) Console 入口改造
- `SktVegapunk.Console/Program.cs`
- 新增 CLI 參數：
  - `--source <pb-file>`
  - `--output <generated-cs-file>`
  - `--target-project <project-or-sln>`
- 入口責任：
  - 讀取設定與 secrets。
  - 初始化依賴。
  - 呼叫 orchestrator，根據結果回傳 exit code。

### 4) 設定新增
- `SktVegapunk.Console/appsettings.json`
- 新增：
  - `Pipeline:MaxRetries`（預設 `3`）
  - `Pipeline:RunTestsAfterBuild`（預設 `false`）
  - `Pipeline:BuildConfiguration`（預設 `Debug`）

## 測試與品質

### 新增測試
- `SktVegapunk.Tests/Pipeline/PbScriptExtractorTests.cs`
  - 混合內容提取、多事件順序、無事件回空集合。
- `SktVegapunk.Tests/Pipeline/MigrationOrchestratorTests.cs`
  - 首次成功、先失敗後成功、達最大重試失敗。
- `SktVegapunk.Tests/OpenRouterClientTests.cs`
  - 成功回應解析、非成功狀態拋例外、Header 驗證。

### 移除測試
- 刪除空白測試：`SktVegapunk.Tests/UnitTest1.cs`

### 驗證結果
- `dotnet build SktVegapunk.slnx`：成功（0 warning / 0 error）
- `dotnet test SktVegapunk.slnx`：成功（8 passed）
- `dotnet format SktVegapunk.slnx --verify-no-changes`：成功

## 文件更新
- `README.md`
  - 新增 Pipeline 設定說明。
  - 更新 CLI 執行方式為帶參數模式。

## 已知取捨
- PB 解析目前採「結構化事件區塊提取」，非完整語法 AST。
- 驗證目標依賴外部 `.sln/.csproj`，本階段不自動建立新專案骨架。
- 狀態雖有 enum，但未導入事件追蹤或持久化狀態儲存。

## 建議下一步
1. 增加批次模式（資料夾掃描、多檔排程）。
2. 加入轉換結果與錯誤輸出的檔案化紀錄（audit trail）。
3. 導入 RAG（團隊範本、命名規範、API 樣板）降低輸出漂移。
