# **多代理協作架構（Multi-Agent System, MAS）**，讓不同的 AI 代理負責各自擅長的領域，形成一條自動化的搬遷流水線。

以下是具體的概念與做法說明：

### 1. 核心概念：分工明確的 AI 代理團隊

與其依賴單一的 AI 模型處理所有事情，不如建立一組擁有不同「角色」的 AI 代理。每個代理只需專注於特定的轉換任務，這能大幅提高程式碼生成的準確度與品質。

* **✅ 分析代理（Analysis Agent）—— 已落地：** 以確定性（deterministic）pipeline 實作，不依賴 AI 做事實提取。
  * `SrdExtractor`：解析 DataWindow 定義（欄位、SQL、參數、資料表）
  * `SruExtractor`：解析 PowerScript 類別（prototype、routine body、事件區塊）
  * `JspExtractor` / `JspPrototypeExtractor`：解析 JSP 的 CORBA 呼叫、互動事件、HTML/JS/CSS prototype
  * `SpecReportBuilder`：組裝 `MigrationSpec`，對齊 JSP→PB→DataWindow 繼承鏈
  * `SchemaExtractor`：解析 Sybase ASE DDL（資料表、Trigger、Index）
  * `SchemaReconciliationAnalyzer`：比對 SrdSpec 欄位型別與 DB Schema，輸出差異報告
  * `EndpointDataWindowAnalyzer`：建立 resolved endpoint → DataWindow 交叉索引
  * 以上均以 `--spec-source` / `--spec-output` CLI 模式執行，不需要走生成流程
* **⚠️ 解耦代理（Decoupling Agent）—— 部分完成：** `PbScriptExtractor` 已可從 PB 原始碼提取事件區塊（`event/on … end event/on`），但尚未真正將 UI、業務邏輯、資料存取拆分成獨立層。
* **⚠️ 後端生成代理（Backend Generation Agent）—— 基礎可用：** `MigrationOrchestrator` + `CopilotCodeGenerator` 已可將抽出的 PB event script 送交 GitHub Copilot SDK 生成 C# 程式碼，並以 build/repair loop 自動修復編譯錯誤。尚未整合 spec artifacts 作為上下文。
* **🔲 前端生成代理（Frontend Generation Agent）—— 尚未實作：** `JspPrototypeExtractor` 已輸出 HTML/JS/CSS prototype 與結構化控制項清單（control inventory），可作為前端生成的輸入材料，但 Vue 3 元件生成尚未建立。
* **🔲 測試代理（Testing Agent）—— 尚未實作：** `DotnetBuildValidator` 可跑 `dotnet build/test`，但這是驗證層而非生成測試的代理。

### 2. 關鍵做法與工作流程

要讓這些 AI 代理順利運作，可以規劃以下幾個轉換階段：

#### 階段一：資料視窗轉換（DataWindow Transformation）

PowerBuilder 最核心的技術是資料視窗（DataWindow）。這是系統的心臟，必須優先處理。

* **AI 任務：** AI 代理會讀取 DataWindow 的定義檔，將其拆解為兩部分：
1. **資料層：** 提取 SQL 查詢語句，交給後端代理轉換為 C# 的 Repository 或 DTO（資料傳輸物件 / Data Transfer Object）。
2. **展示層：** 提取欄位屬性（如長度、型別、驗證規則），交給前端代理生成 Vue 3 的表單或表格元件。



#### 階段二：檢索增強生成（Retrieval-Augmented Generation, RAG）

為了確保 AI 生成的 C# 和 Vue 3 程式碼符合你們團隊的架構規範，不能只靠 AI 的預設知識。

* **做法：** 建立一個專案專屬的知識庫（Knowledge Base）。將團隊寫好的 .NET 架構範本、Vue 3 共用元件庫、以及命名慣例（Naming Conventions）輸入給 AI。當 AI 代理在轉換程式碼時，必須先「檢索」這些規範，確保產出的程式碼風格一致且符合現代化標準。

#### 階段三：增量與迭代轉換（Incremental & Iterative Migration）

不要試圖讓 AI 一次轉換整個專案，這會導致極高的錯誤率（幻覺 / Hallucinations）。

* **做法：** 將系統切分為多個小型模組（Modules）或單一功能（Features）。每次只餵給 AI 代理一個功能的 PowerBuilder 程式碼，讓它完成該功能的 .NET API 與 Vue 3 頁面，確認無誤後再進行下一個。

#### 階段四：人機協同（Human-in-the-Loop, HITL）

AI 代理目前無法達到 100% 完美的轉換，特別是在處理極端複雜、缺乏文件的歷史包袱（Technical Debt）時。

* **做法：** 在工作流程中設計「人工審查節點」。AI 完成初步的解耦與草稿程式碼後，必須由資深開發者進行審查與微調。AI 負責完成 80% 的苦力活，人類工程師專注於剩下 20% 的核心邏輯優化與架構決策。

### 總結

透過 AI 代理，你們可以將現代化專案從「純手工重寫」轉變為「AI 輔助的半自動化編譯」。關鍵在於**建立明確的轉換規則**、**導入 RAG 來統一架構標準**，以及**分工明確的多代理系統**。

**目前實作狀態（2026-04-16）**：Analysis Agent 已完整落地為 deterministic spec pipeline，可在不呼叫任何 LLM 的情況下產出 35 種中介文件與 JSON artifacts。Backend Generation Agent 有基礎可用實作但尚未整合 spec 上下文。Decoupling、Frontend、Testing Agent 尚在規劃中。
