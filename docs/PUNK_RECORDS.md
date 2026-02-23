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

### 2026-02-23 Phase 1：規格提取（Deterministic Extractors）
- 實作 `.srd` / `.sru` / `.jsp` 的機械式規格提取，避免依賴 AI 做事實提取。
- **1a. SrdExtractor**：解析 DataWindow 定義，提取欄位、SQL、參數與資料表。
  - 資料模型：`SrdColumn`、`SrdArgument`、`SrdSpec`。
  - 支援 `char(40)` 等類型長度解析、`dbname` 提取資料表名。
  - 介面：`ISrdExtractor`、實作：`SrdExtractor`。
- **1b. SruExtractor**：解析 PowerScript 類別，提取原型、函式本文、事件區塊。
  - 資料模型：`SruPrototype`、`SruRoutine`、`SruSpec`。
  - 支援 `global type ... from ...` 繼承解析、`forward prototypes` 提取、`function/subroutine` 本文解析。
  - 掃描 DataWindow 引用（`datawindow=`、`.retrieve(`）與 SQL 關鍵字。
  - 介面：`ISruExtractor`、實作：`SruExtractor`（內部復用 `IPbScriptExtractor`）。
- **1c. JspExtractor**：解析 JSP 檔案，提取 CORBA 呼叫與 HTTP 參數。
  - 資料模型：`JspInvocation`。
  - 支援 `component.of_xxx(...)` 方法呼叫解析、`request.getParameter("xxx")` HTTP 參數提取。
  - 介面：`IJspExtractor`、實作：`JspExtractor`。
- **1d. SpecReportBuilder**：組裝 `MigrationSpec` 並輸出可審查報告。
  - 資料模型：`EndpointCandidate`（含狀態 `Resolved`/`Unresolved`）、`MigrationSpec`。
  - 實作 JSP → PB → DataWindow 對齊邏輯，標記繼承鏈缺口。
  - 輸出：`output/spec/report.md`、`output/spec/datawindows/*.json`、`output/spec/components/*.json`。
  - 介面：`ISpecReportBuilder`、實作：`SpecReportBuilder`。
- 命名規則：所有 `static readonly Regex` 欄位使用 `_` 前綴。
- 測試：`dotnet build`（成功）、`dotnet test`（13 passed）、`dotnet format --verify-no-changes`（成功）。

### 2026-02-23 Phase 1 Review 修正（Extractor / ReportBuilder）
- 目標與範圍：修正 `docs/REVIEW.md` 與 reviewer 指出的規格提取誤判與中斷風險，聚焦 `JspExtractor`、`SrdExtractor`、`SruExtractor`、`SpecReportBuilder`。
- 主要程式異動與決策：
  - `SktVegapunk.Core/Pipeline/Spec/JspExtractor.cs`
    - 僅匹配 `of_*/uf_*` component 呼叫，避免誤抓 `request.getParameter`、`session.getAttribute` 等 Servlet API。
    - 先解析 receiver 變數宣告，再以「型別名」回填 `ComponentName`（例如 `n_sign iJagComponent`）。
    - 參數定位改用 `Match.Index`，避免 `IndexOf` 多次匹配時錯位。
  - `SktVegapunk.Core/Pipeline/Spec/SrdExtractor.cs`
    - `column` 解析放寬為可選 `update=` / `updatewhereclause=` / `key=`，覆蓋實際 `.srd` 欄位定義。
    - `retrieve` 改為逐字元解析，支援 PBSELECT 的 `~"` 跳脫引號。
    - `arguments` 改為括號平衡掃描，修正只抓到第一個參數的問題。
  - `SktVegapunk.Core/Pipeline/Spec/SruExtractor.cs`
    - `prototype/function start` 正則修正為可正確匹配無回傳型別 `subroutine`。
    - routine 掃描前先移除 `forward prototypes` 區塊，避免把 prototype 誤當函式實作。
  - `SktVegapunk.Core/Pipeline/Spec/SpecReportBuilder.cs`
    - 重複 `ClassName` 改用分組 map，不再因 `ToDictionary` 重鍵直接拋例外。
    - 優先用「含目標 method 的 component」做對齊，降低同名 component 誤判。
    - JSON 輸出改走 `ITextFileStore`（不再直接 `File.WriteAllTextAsync`），符合 DIP 且可測試。
    - 移除偽非同步目錄建立，時間改注入 `TimeProvider`，報告輸出可重現。
- 新增測試（Phase 1 首批）：
  - `SktVegapunk.Tests/Pipeline/Spec/JspExtractorTests.cs`
  - `SktVegapunk.Tests/Pipeline/Spec/SrdExtractorTests.cs`
  - `SktVegapunk.Tests/Pipeline/Spec/SruExtractorTests.cs`
  - `SktVegapunk.Tests/Pipeline/Spec/SpecReportBuilderTests.cs`
- 驗證結果：
  - `dotnet test SktVegapunk.slnx /nr:false /m:1 /p:BuildInParallel=false /p:UseSharedCompilation=false`：成功（21 passed）。
  - `dotnet format SktVegapunk.slnx --verify-no-changes`：失敗（Restore operation failed，需在可完整 restore 的環境重跑）。
- 已知取捨與後續建議：
  - `JspExtractor` 目前以 `of_*/uf_*` 為 PB 方法命名慣例；若未來有其他前綴，需擴充匹配規則。
  - `SpecReportBuilder` 對同名 component 仍採「方法優先，其次首個」策略；若需更強一致性，建議後續加入命名空間/來源路徑權重。

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
