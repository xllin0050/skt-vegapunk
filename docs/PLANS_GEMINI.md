# Migration Plans

本文件定義從 PowerBuilder (PB) 遷移至 .NET Core 的實作計畫。根據對 `source/AI_Migration_Methodology.md` 與現有程式碼的分析，我們將採取「規格驅動 (Spec-Driven)」的遷移策略。

## 1. 核心願景：從「直接翻譯」轉向「規格驅動」

目前的 PoC 採用的「直接翻譯」模式在處理複雜邏輯與資料契約時容易產生漂移。我們將實作一套真正可落地的「規格產出方法」，確保生成的程式碼具備高度一致性。

### 真正的規格產出流程
1.  **契約提取 (Contract Extraction)**：從 `.sru` 提取 `forward prototypes`，從 `.srd` 提取 `table` 欄位與 SQL。
2.  **中介規格產出 (Spec Generation)**：AI 基於契約產出 Markdown 規格書，定義 API Endpoint、DTO 與資料邏輯。
3.  **精準生成 (Precision Generation)**：AI 參考規格書與 PB 實作程式碼，產出符合設計的 C#。

---

## 2. 短期計畫 (Short-term)

### 2.1 基礎設施強化
- [ ] **編碼支援**：修改 `FileTextStore` 或新增中介層，自動識別並將 `UTF-16LE` 轉換為 `UTF-8`（PB 原始碼檔案特性）。
- [ ] **多檔案類型支援**：擴充 Pipeline 以同時處理 `.sru` (邏輯) 與 `.srd` (資料契約)。

### 2.2 規格分析器 (Object Analyzer)
- [ ] **實作 `PbObjectAnalyzer`**：
    - 針對 `.sru`：提取 `forward prototypes` 區塊，識別所有公開介面。
    - 針對 `.srd`：提取 `table` 定義（欄位、型別）與 `retrieve` SQL。
- [ ] **定義 `MigrationSpec` 模型**：用於存儲提取出的結構化資訊。

### 2.3 流程編排更新
- [ ] **新增 `Analyzing` 狀態**：在 `Preprocessing` 之後，`Generating` 之前。
- [ ] **調整 `PromptBuilder`**：
    - 實作 `BuildSpecPrompt`：要求 AI 產出 Markdown 規格。
    - 修改 `BuildInitialPrompt`：將規格書作為 Context 餵給 AI 生成程式碼。

---

## 3. 中期計畫 (Mid-term)

### 3.1 跨檔案關聯 (Cross-file RAG)
- [ ] **建立 RAG 索引**：將所有 `.srd` 的資料結構建立索引。
- [ ] **關聯分析**：當翻譯 `.sru` 時，若用到某個 DataWindow，自動抓取其 DTO 規格作為 Prompt 補充。

### 3.2 批次轉換與審核追蹤 (Audit Trail)
- [ ] **實作批次模式**：支援資料夾掃描。
- [ ] **產出遷移報告**：包含生成的規格書、程式碼、驗證結果與 AI 的「核心邏輯審查建議」。

---

## 4. 實作規格產出的具體指令 (實作建議版)

針對 Section 2 的「真正實作方法」，我們建議採用的 AI 指令模式：

#### 步驟 1：產出資料規格 (.srd)
> 「讀取此 .srd 檔案的 table 定義與 SQL。請產出一份 Markdown 規格，包含：1. 欄位清單與型別 2. 參數定義 3. 業務邏輯說明。這份規格將作為 DTO 生成的依據。」

#### 步驟 2：產出介面規格 (.sru)
> 「讀取此 .sru 檔案的 forward prototypes。請產出一份 API Endpoint 清單，定義其 HTTP 方法、URL 路徑、Input/Output DTO（參考步驟 1 的規格）。」

---

## 5. 驗證標準
- [ ] 規格書必須包含 100% 的原始欄位定義。
- [ ] API Endpoint 定義必須與 PB 公開方法簽章一致。
- [ ] 生成的 C# 程式碼必須符合規格書定義的 DTO 結構。
