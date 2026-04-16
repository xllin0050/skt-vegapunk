在軟體工程領域，一個有生產力的 AI 代理是 **「大語言模型（LLM）+ 程式控制流程 + 外部工具（Tools/Function Calling）」** 的結合體。它們是跑在終端機（Terminal）或伺服器背景的程式，而不是一個聊天視窗。

### 1. 具體運行方式：自動化流水線 (Pipeline)

你手動匯出 PowerBuilder (PB) 的 `.srd`、`.sru`、`.jsp` 等檔案後，整個流程是透過程式碼（C#）自動串聯的，這叫做**代理編排（Agent Orchestration）**。

**目前實作的兩條路徑：**

**路徑 A — Spec Pipeline（`--spec-source`）**

1. **非 AI 預處理（降噪）：** `PbSourceNormalizer` 先正規化 BOM 與 UTF-16LE 編碼；`SrdExtractor`、`SruExtractor`、`JspExtractor` 以 Regex 拆解 PB 結構化文字，產出 DataWindow、Component、JSP invocation 等 JSON artifacts，完全不呼叫 LLM。
2. **Schema 整合：** `SchemaExtractor` 解析 Sybase ASE DDL，`SchemaReconciliationAnalyzer` 比對 DataWindow 欄位與 DB Schema 差異，`EndpointDataWindowAnalyzer` 建立 endpoint → DataWindow 交叉索引。
3. **文件輸出：** `SpecReportBuilder` 組裝 `MigrationSpec`；`GenerationPhasePlanner` 產出生成計畫；所有 artifacts 統一輸出到 `output/spec/`。

**路徑 B — Migration Pipeline（`--source`）**

1. **非 AI 預處理（降噪）：** 同樣先經 `PbSourceNormalizer`；`PbScriptExtractor` 用逐行狀態機提取 `event/on … end event/on` 區塊，剔除 UI 屬性雜訊。
2. **工具調用（Tool Use）：** `MigrationOrchestrator` 呼叫 `CopilotCodeGenerator`，透過 GitHub Copilot SDK 傳送 event script 給 LLM 生成 C# 程式碼。
3. **Feedback Loop：** `DotnetBuildValidator` 執行 `dotnet build/test`；失敗時 `PromptBuilder` 帶著錯誤訊息重組 repair prompt 再送，最多 `MaxRetries` 次。

### 2. 技術選型

| 層級 | 選型 | 說明 |
|------|------|------|
| LLM | GitHub Copilot SDK | `CopilotClient` + `SessionConfig`，通過本機 `copilot` CLI 驗證 |
| 預處理 | C# Regex + 逐行狀態機 | 不使用 ANTLR，以最小複雜度達到足夠精準度 |
| 編排 | 自製 `MigrationOrchestrator` | 不依賴 Semantic Kernel 等框架，保持輕量可測試 |
| 建置驗證 | `System.Diagnostics.Process` | 直接呼叫 `dotnet build/test` |

### 總結來說

這套機制的本質是：**寫一段 C# 程式，去驅動 LLM 幫你寫另一段 C# 程式，並在過程中自動執行編譯與測試**。Spec Pipeline 負責把 PB 知識確定性地萃取成文件，Migration Pipeline 則把這份知識轉換為可運行的後端程式碼。人類工程師的職責從「寫 Code」變成了「設計這條生產線並審核最終產品」。
