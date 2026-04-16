## 預處理（Preprocessing）不用 AI

將 PowerBuilder 原始碼（.srd, .sru, .jsp）交給 AI 預處理有三個致命缺點：

**太貴：** PB 檔案裡充滿 UI 座標、字型設定等對業務邏輯毫無意義的雜訊，大量消耗 Token。

**會漏詞（幻覺 / Hallucination）：** AI 在讀取幾千行結構化文本時，非常容易自作主張省略中間的程式碼。

**沒效率：** PB 的檔案結構是有固定規律的純文字檔，不需要 AI 就能精準切割。

本專案採取的做法：用 C# Regex 與逐行狀態機做確定性提取，這部分 100% 準確，執行成本是零。

## 實際實作：`MigrationOrchestrator`

這是 Migration Pipeline（`--source` 模式）的協調者，位於 `SktVegapunk.Core/Pipeline/MigrationOrchestrator.cs`。

### 工作流程（4 步驟）

**Step 1：讀取與正規化（純 C#）**

`FileTextStore.ReadAllBytesAsync` 讀取 PB 原始檔，`PbSourceNormalizer.Normalize()` 自動偵測 BOM 並以 UTF-16LE 解碼，確保中文字元不亂碼。

**Step 2：提取事件區塊（純 C#）**

`PbScriptExtractor.Extract(normalizedText)` 以逐行狀態機提取 `event/on ... end event/on` 區塊，剔除 UI 屬性（如 `x=100 y=200`），只保留業務邏輯。如果找不到任何事件區塊，直接回傳失敗，不呼叫 LLM。

**Step 3：組出 Prompt → 呼叫 Copilot SDK（LLM）**

`PromptBuilder.BuildInitialPrompt(eventBlocks)` 組裝初始 user prompt；`CopilotCodeGenerator.GenerateAsync()` 透過 `GitHubCopilotClient` → `GitHub.Copilot.SDK` 送出請求並等待回應。

**Step 4：驗證與 Repair Loop（Tool Use）**

`DotnetBuildValidator` 執行 `dotnet build`（可選 `dotnet test`）。失敗時 `PromptBuilder.BuildRepairPrompt()` 帶著原始需求、前一次生成的程式碼與編譯錯誤重組 prompt，重新送給 Copilot。最多執行 `MaxRetries` 次。

### Repair Loop 的意義

這個迴圈是「代理」與「單純對話」的最大差異。AI 不再只是單向的「發送與接收」，而是具備「反思與修正（Reflection and Correction）」能力：

- 若 `dotnet build` 成功 → `MigrationState.Completed`
- 若失敗且還有次數 → `MigrationState.Repairing`，帶錯誤訊息重試
- 若次數耗盡 → `MigrationState.Failed`，回傳最後一次的驗證輸出

### 防呆機制

設定 `MaxRetries`（預設 3）。若 AI 遇到太複雜的底層依賴連續寫錯，程式會優雅地停下來，把難搞的檔案留給人工處理，避免浪費 Token。

## Spec Pipeline 的設計

分析路徑（`--spec-source` 模式）由 `SpecArtifactsGenerator` 協調，完全不呼叫 LLM：

```
ReadAllBytesAsync
  → PbSourceNormalizer.Normalize()
  → SrdExtractor / SruExtractor / JspExtractor
  → SpecReportBuilder.Build() → report.md
  → SchemaExtractor.Extract(DDL) → schema/tables/*.json
  → SchemaReconciliationAnalyzer.Analyze() → schema-reconciliation.md
  → EndpointDataWindowAnalyzer.Analyze() → endpoint-datawindow-map.md
  → GenerationPhasePlanner.GenerateMarkdown() → generation-phase-plan.md
```

所有步驟確定性執行，相同輸入永遠得到相同輸出。
