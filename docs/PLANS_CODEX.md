# PLANS_CODEX

更新日期：2026-02-23

## 1. 目標與範圍

本計畫目標是把 `source/AI_Migration_Methodology.md` 第 2 章（AI 產出程式規格）改成「符合目前專案現況且可逐步落地」的方法，並維持現有 PoC 主流程：

`Preprocessing -> Generating -> Validating -> Repairing`

本輪範圍：
- 只規劃後端規格產出與後端程式生成前置作業。
- 延續目前 Console + Core 架構，不引入前端 Vue 轉換流程。
- 先落地 deterministic extraction（可測、可重現），再導入 AI 規格補強。

不在本輪：
- 不做 Wireframe/Vue 元件生成。
- 不做多代理調度與 RAG 基礎建設。
- 不直接追求「一次產出完整 OpenAPI + TS + C#」全自動。

## 2. 現況確認（依目前 repository 與 `source/sign` 實測）

- 現有 Pipeline 只會從來源檔提取 `event/on ... end event/end on` 區塊。
- `source/sign` 實際主邏輯主要位在 `public/private function` 與 `subroutine`，不是 event。
- `source/sign` 檔案盤點：
  - `.sru`: 17
  - `.srd`: 35
  - `.jsp`: 18
- `.srd` 可機械提取性高：
  - 35/35 含 `table(...)`
  - 35/35 含 `retrieve="..."`
  - 32/35 含 `arguments=(...)`
- `.sru` prototype 可機械提取：
  - `forward prototypes` 內共 87 個 function/subroutine 宣告
- 匯出檔有編碼前處理需求：
  - `source/sign` 的 `.sru/.srd` 普遍是「`ÿþ` 前綴 + UTF-16LE 內容」格式
  - 若不先標準化，現有字串比對邏輯會失準
- JSP 與 PB 定義有落差：
  - JSP 有呼叫 `of_sign_select/of_sign_select_ins`、`n_sign_history.*`
  - 目前匯出的 `.sru` 找不到對應定義，表示來源可能不完整或定義在未匯出 PBL

## 3. 第 2 章落差與可實作替代

| 方法論第 2 章原描述 | 與現況落差 | 可實作替代 |
|---|---|---|
| 2.1 餵 `.srd` 直接產出 DTO/OpenAPI/TS | 目前 Pipeline 沒有 `.srd` parser，也沒有規格中介層 | 先做 `.srd` deterministic parser 產生 `DataWindowSpec`（JSON/Markdown），再讓 AI 從 Spec 生成 DTO/OpenAPI 草稿 |
| 2.2 餵 PB method + JSP 直接產 endpoint 規格 | 目前只抽 event；未抽 `forward prototypes`、未做 JSP 呼叫映射；且有缺檔 | 先做 `SruPrototypeExtractor + JspInvocationExtractor` 產生 endpoint candidates，對缺失方法標記 `Unresolved` 後再交 AI 補描述 |
| 2.3 餵商業邏輯直接產規則文件 | 目前未抽 function/subroutine body，規則來源不足 | 新增 `SruRoutineExtractor`，先切出 routine 區塊與關聯 SQL/DataWindow，再用 AI 摘要成規則文件 |

## 4. 真正可落地的方法（Spec-First + Deterministic Extraction）

### 4.1 新流程（建議）

`Source Normalize -> Analyze -> Build Spec -> Generate -> Validate -> Repair`

### 4.2 核心原則

- 先由 deterministic parser 產生可檢查的中介規格（避免黑箱）。
- AI 只做「規格補述與程式生成」，不負責第一手事實提取。
- 每一步都可單元測試，且產物可追溯到來源檔與行號。

### 4.3 中介規格（建議輸出）

- `output/spec/datawindows/*.json`
  - 每個 `.srd` 的欄位、型別、`retrieve SQL`、`arguments`
- `output/spec/endpoints/endpoints.json`
  - 來自 `JSP -> iJagComponent.method -> PB prototype` 的 endpoint 候選
- `output/spec/routines/*.json`
  - 每個 `.sru` routine 的簽章、本文、依賴（DataWindow/SQL/外部 component）
- `output/spec/reports/unresolved.md`
  - JSP 呼叫但無對應 PB 定義、或 PB 定義無 JSP 呼叫的差異清單

## 5. 分階段實作計畫

### Phase 0：地基與風險封裝
- [ ] 新增 `PbSourceNormalizer`（負責匯出檔編碼正規化）
- [ ] 明確定義 `SourceArtifact`（含 `OriginalPath`、`NormalizedText`、`Warnings`）
- [ ] 新增測試資料夾（固定使用 `source/sign` 代表檔作 golden sample）

交付標準：
- [ ] 可穩定讀取 `source/sign` 全部 `.sru/.srd` 並輸出 UTF-8 正規化文字
- [ ] 對異常檔案輸出 warning，不因單檔失敗中止整批分析

### Phase 1：Spec Extractors（不含 AI）
- [ ] `SrdContractExtractor`：抽 `column/type/maxlength/dbname/retrieve/arguments`
- [ ] `SruPrototypeExtractor`：抽 `forward prototypes`
- [ ] `SruRoutineExtractor`：抽 function/subroutine 區塊
- [ ] `JspInvocationExtractor`：抽 `request.getParameter` 與 `iJagComponent.*` 呼叫
- [ ] `SpecAssembler`：整合為單一 `MigrationSpec`

交付標準：
- [ ] `.srd` 抽取成功率達 35/35（含 `retrieve`）
- [ ] `.sru` prototype 抽取至少對齊目前盤點基準（87）
- [ ] `.jsp` 呼叫映射可覆蓋 18/18
- [ ] 產出 `unresolved` 清單（例如 `of_sign_select*`, `n_sign_history*`）

### Phase 2：導入現有 Pipeline（Analyzing 狀態）
- [ ] 在 Orchestrator 增加 `Analyzing` 階段（不破壞既有 retry 行為）
- [ ] PromptBuilder 改為讀取 `MigrationSpec`，而非只靠 event blocks
- [ ] 先支援 `Spec -> C# Backend` 單檔/單功能生成

交付標準：
- [ ] 可在不改 CLI 主參數下跑完整流程
- [ ] 生成前 prompt 內容可追溯到 spec 檔案

### Phase 3：驗證與品質閘門
- [ ] 新增 spec 完整度檢查（欄位遺漏、參數遺漏、未對應方法）
- [ ] 保留既有 `dotnet build/test` 驗證迴圈
- [ ] 新增 spec 相關測試（parser 單測 + orchestrator 整合測）

交付標準：
- [ ] `dotnet build`、`dotnet test` 維持通過
- [ ] Spec 驗證失敗時可回報具體來源檔案與鍵值

### Phase 4：批次化與審核追蹤（次階段）
- [ ] 支援資料夾批次分析與批次生成
- [ ] 產出每次遷移的 audit trail（spec + prompt + validation output）
- [ ] 建立「人工審核清單」與優先級規則

## 6. 驗收標準（本計畫對齊點）

- [ ] 不再以 event-only 當主要規格來源，改以 prototype + routine + DataWindow 為主。
- [ ] 所有 AI 生成前都先有 deterministic spec 產物可審查。
- [ ] 能辨識與回報匯出缺漏（而非默默忽略）。
- [ ] 維持現有 Build/Repair 迴圈，避免改完 parser 反而失去驗證機制。

## 7. 主要風險與對策

- 風險：PB 匯出不完整（JSP 有呼叫但缺對應 `.sru`）
  - 對策：`unresolved` 報告列為阻擋條件，先補來源再進入生成。

- 風險：`.srd` SQL 格式複雜（UNION、多行字串、函式）
  - 對策：先保留原 SQL 文字，不急著轉 LINQ；規格層只保證「可追溯」。

- 風險：現有 Pipeline 與新 Analyzer 耦合過深
  - 對策：以介面切分（Normalizer/Extractors/Assembler），先旁路整合再替換。

## 8. 先後順序建議

1. 先完成 Phase 0 + Phase 1（把規格提取做穩）。
2. 再做 Phase 2（接入現有 Orchestrator）。
3. 最後做 Phase 3（品質閘門）與 Phase 4（批次化）。

