# PLANS_OPUS

## 1. 目標與範圍

將 `source/AI_Migration_Methodology.md` 第 2 章（AI 產出程式規格）落地為可執行的 Pipeline 擴充，使系統能從 PB 匯出檔中 **機械式提取結構化規格**，再以規格驅動 AI 程式碼生成。

### 本輪範圍
- 後端規格提取與後端程式碼生成。
- 延續並擴充現有 Console + Core 架構，不破壞已通過的 8 個測試。
- 將主流程從 `Preprocessing → Generating → Validating → Repairing` 擴充為：

```
Normalizing → Analyzing → Generating → Validating → Repairing
```

### 不在本輪
- 前端 Vue 轉換與 Wireframe 生成。
- 多代理調度與 RAG。
- 資料庫 Schema 遷移。
- 批次模式（Phase 4 規劃但不在本輪交付）。

---

## 2. 現況確認

### 2.1 現有 Pipeline 的能力邊界

| 項目 | 現狀 |
|------|------|
| 提取範圍 | 僅 `event/on ... end event/end on` 區塊 |
| 提取對象 | 單一 `.srw`/`.sru` 檔案 |
| 編碼處理 | `File.ReadAllTextAsync`（假設 UTF-8，無法處理 UTF-16LE） |
| Prompt 策略 | 將 event blocks 直接塞入 prompt，無規格中介層 |
| 驗證機制 | `dotnet build` + 可選 `dotnet test`，含重試迴圈 |

### 2.2 來源檔案盤點（`source/sign`）

| 類型 | 數量 | 位置 | 說明 |
|------|------|------|------|
| `.srd` | 35 | `dw_sign/` (16), `tpec_s61/` (17), `sky_webbase/` (2) | DataWindow 定義，100% 含 `table(...)` 與 `retrieve=` |
| `.sru` | 17 | `sign/` (4), `tpec_s61/` (1), `sky_webbase/` (5), `webap/` (7) | 邏輯程式碼，關鍵為 `n_sign.sru`（2809 行）與 `uo_sign_record.sru`（2336 行） |
| `.jsp` | 18 | 根目錄 | 前端頁面，CORBA 呼叫入口 |
| `.srx` | 4 | `sign/` | EAServer Proxy（介面定義，無實作），可跳過 |
| `.srf` | 7 | `sign/`, `sky_webbase/` | 全域函式定義 |
| `.srs` | 2 | `sign/` | 結構體定義 |

### 2.3 編碼問題

所有 `.srd`/`.sru`/`.srx`/`.srf`/`.srs` 共用同一個問題：

- 預期：`FF FE`（UTF-16LE BOM）+ UTF-16LE 內容
- 實際：前 4 bytes 為 `C3 BF C3 BE`（UTF-16LE BOM `FF FE` 被某工具誤以 ISO-8859-1 → UTF-8 轉碼）
- **影響**：現有 `FileTextStore.ReadAllTextAsync` 使用系統預設編碼，讀出來是亂碼
- **處理方式**：跳過前 4 bytes，以 UTF-16LE 解碼

`.jsp` 檔案為 UTF-8（部分含 BOM），無需特殊處理。

### 2.4 方法論第 2 章 vs. 現況落差

| 方法論描述 | 落差 | 本計畫對策 |
|-----------|------|-----------|
| 2.1 餵 `.srd` → 產 DTO/OpenAPI/TS | Pipeline 無 `.srd` parser | 新增 `SrdExtractor`，deterministic 提取欄位定義與 SQL |
| 2.2 餵 PB method + JSP → 產 endpoint 規格 | Pipeline 只抽 event；未抽 `forward prototypes`、function/subroutine body | 新增 `SruExtractor`，提取方法簽章與函式本文 |
| 2.3 餵商業邏輯 → 產規則文件 | 無函式本文提取，無 SQL/DataWindow 關聯分析 | `SruExtractor` 同時提取函式本文；`SpecReport` 輸出關聯資訊 |

### 2.5 JSP → PB 方法映射（方法論 2.2 表格已驗證）

方法論中列出 14 個 endpoint 映射。經比對 `source/sign`：

| JSP | 呼叫元件 | 呼叫方法 | 在 `.sru` 中找到？ |
|-----|---------|---------|-------------------|
| `sign_api.jsp` | `n_sign` | `of_sign_api` | ✅ `n_sign.sru` |
| `sign_00.jsp` | `n_sign` | `of_sign_00` | ✅ `n_sign.sru` |
| `sign_content.jsp` | `n_sign` | `of_sign_content` | ✅ `n_sign.sru` |
| `sign_dtl.jsp` | `n_sign` | `of_sign_dtl` | ✅ `n_sign.sru` |
| `sign_ins.jsp` | `n_sign` | `of_sign_ins` | ✅ `n_sign.sru` |
| `sign_doc.jsp` | `n_sign` | `of_sign_doc` | ✅ `n_sign.sru` |
| `sign_pick_api_1.jsp` | `n_sign` | `of_sign_pick_api_1` | ✅ `n_sign.sru` |
| `sign_pick_api_2.jsp` | `n_sign` | `of_sign_pick_api_2` | ✅ `n_sign.sru` |
| `sign_pick_api_3.jsp` | `n_sign` | `of_sign_pick_api_3` | ✅ `n_sign.sru` |
| `sign_pick_api_ins.jsp` | `n_sign` | `of_sign_pick_api_ins` | ✅ `n_sign.sru` |
| `sign_pick_api_ins_hd.jsp` | `n_sign` | `of_sign_pick_api_ins_hd` | ✅ `n_sign.sru` |
| `sign_countersign_dtl.jsp` | `n_sign` | `of_sign_countersign_dtl` | ✅ `n_sign.sru` |
| `sign_history_*.jsp` (×3) | `n_sign` | `of_sign_history_*` | ⚠️ 不在 `n_sign.sru` 的 forward prototypes 中，可能定義在父類 `n_sky_webbase.sru` |
| `sign_select.jsp` | `n_sign` | `of_sign_select` | ⚠️ 同上 |
| `sign_select_ins.jsp` | `n_sign` | `of_sign_select_ins` | ⚠️ 同上 |
| `createSign.jsp` | `uo_sign_record` | `uf_create_sign` | ✅ `uo_sign_record.sru` |

**結論**：18 個 JSP 中，12 個可直接對應，6 個需追蹤至父類或標記為 `Unresolved`。

### 2.6 類別繼承鏈

```
nonvisualobject
└── nvo_component (webap/)          ← 基底：DB 連線、壓縮、共用工具
    └── uo_sign_record (tpec_s61/)  ← 簽核流程核心（19 個方法）

n_tpec_webbase (sky_webbase/)       ← Web 基底（23 個方法）
└── n_sign (sign/)                  ← 主 NVO，JSP 入口（18 個方法）
```

⚠️ `n_sign` 繼承自 `n_tpec_webbase`（即 `n_sky_webbase.sru`），部分 JSP 呼叫的方法定義在父類。提取分析時需跨檔案追蹤。

---

## 3. 架構設計

### 3.1 新流程

```
Normalizing → Analyzing → Generating → Validating → Repairing
     │             │            │             │            │
Source Files   MigrationSpec  C# Code     dotnet build   Error feedback
(UTF-16LE)      (JSON/MD)    (.cs)        + test          → re-Generate
```

### 3.2 核心介面（新增）

```csharp
/// 將 PB 匯出檔從原始編碼正規化為 UTF-8 純文字
interface ISourceNormalizer
{
    SourceArtifact Normalize(byte[] rawBytes, string originalPath);
}

/// 從正規化後的 .srd 文字提取 DataWindow 規格
interface ISrdExtractor
{
    SrdSpec Extract(string normalizedText);
}

/// 從正規化後的 .sru 文字提取方法簽章與函式本文
interface ISruExtractor
{
    SruSpec Extract(string normalizedText);
}

/// 從 .jsp 文字提取 CORBA 呼叫資訊
interface IJspExtractor
{
    JspInvocation Extract(string jspText);
}
```

### 3.3 資料模型（新增）

```
SourceArtifact
├── OriginalPath: string
├── NormalizedText: string
├── SourceEncoding: string        // 偵測到的原始編碼
└── Warnings: IReadOnlyList<string>

SrdSpec
├── FileName: string
├── Columns: IReadOnlyList<SrdColumn>
│   └── SrdColumn { Name, DbName, Type, MaxLength }
├── RetrieveSql: string           // 原始 SQL（保留原貌，不轉 LINQ）
├── Arguments: IReadOnlyList<SrdArgument>
│   └── SrdArgument { Name, Type }
└── Tables: IReadOnlyList<string> // 涉及的資料表名

SruSpec
├── FileName: string
├── ClassName: string
├── ParentClass: string
├── InstanceVariables: IReadOnlyList<string>
├── Prototypes: IReadOnlyList<SruPrototype>
│   └── SruPrototype { AccessLevel, ReturnType, Name, Parameters, IsFunction }
├── Routines: IReadOnlyList<SruRoutine>
│   └── SruRoutine { Prototype, Body, ReferencedDataWindows, ReferencedSql }
└── EventBlocks: IReadOnlyList<PbEventBlock>  // 兼容現有模型

JspInvocation
├── JspFileName: string
├── ComponentName: string         // e.g. "n_sign"
├── MethodName: string            // e.g. "of_sign_00"
├── Parameters: IReadOnlyList<string>
└── HttpParameters: IReadOnlyList<string>  // request.getParameter 呼叫

MigrationSpec（總組裝）
├── DataWindows: IReadOnlyList<SrdSpec>
├── Components: IReadOnlyList<SruSpec>
├── JspInvocations: IReadOnlyList<JspInvocation>
├── EndpointCandidates: IReadOnlyList<EndpointCandidate>
│   └── EndpointCandidate { JspSource, PbMethod, SuggestedHttpMethod,
│                            SuggestedRoute, Status(Resolved|Unresolved) }
└── UnresolvedMethods: IReadOnlyList<string>
```

### 3.4 既有元件的變更範圍

| 元件 | 變更方式 |
|------|---------|
| `MigrationState` | 新增 `Normalizing`、`Analyzing` 兩個枚舉值 |
| `MigrationRequest` | 新增 `SourceDirectory`（資料夾模式，可選）；保留 `SourceFilePath`（單檔模式） |
| `MigrationOrchestrator` | 在 `Preprocessing` 之前插入 `Normalizing` + `Analyzing`；Analyzing 產出 `MigrationSpec` 後交給 `PromptBuilder` |
| `PromptBuilder` | 新增 `BuildSpecDrivenPrompt(MigrationSpec, PromptTarget)` 方法；保留 `BuildInitialPrompt` 相容舊流程 |
| `FileTextStore` | 新增 `ReadAllBytesAsync` 方法（供 Normalizer 使用原始 bytes） |
| `PbScriptExtractor` | **不修改**——退化為 `SruExtractor` 的內部委派（event block 提取邏輯不變） |

### 3.5 設計原則

| 原則 | 實踐 |
|------|------|
| **Deterministic First** | Normalizer 與 Extractor 不使用 AI，輸出可 diff、可單測、可重現 |
| **不信任 AI 做事實提取** | AI 只負責「從 deterministic spec 產出程式碼」與「從函式本文摘要商業規則」 |
| **向後相容** | 單檔 event-only 流程仍可運行（`--source` 指向單一 `.sru` 時走舊路徑） |
| **介面隔離** | 每個 Extractor 獨立介面，Orchestrator 透過依賴注入組裝 |
| **Fail-safe** | 單一檔案正規化或提取失敗時記錄 warning 並跳過，不中止整批 |

---

## 4. 分階段實作計畫

### Phase 0：地基——編碼正規化

**目標**：讓 Pipeline 能穩定讀取 `source/sign` 所有 PB 匯出檔。

**交付項目**：
- [ ] `ISourceNormalizer` 介面 + `PbSourceNormalizer` 實作
  - 偵測前 4 bytes `C3 BF C3 BE` → 跳過後以 UTF-16LE 解碼
  - 偵測 `FF FE` → 標準 UTF-16LE 解碼
  - 其餘 → 嘗試 UTF-8
  - 解碼失敗時回傳 warning，不拋例外
- [ ] `SourceArtifact` record
- [ ] `ITextFileStore` 新增 `ReadAllBytesAsync`
- [ ] 測試：以 `source/sign/dw_sign/d_signkind.srd`、`source/sign/sign/n_sign.sru` 為 golden sample
  - 能正確讀出中文欄位名稱
  - 能正確讀出 `retrieve=` 的 SQL

**驗收標準**：
- [ ] `source/sign` 全部 70 個 PB 檔案（.srd + .sru + .srx + .srf + .srs）正規化成功率 ≥ 95%
- [ ] 異常檔案有 warning 訊息且不中止程序
- [ ] `dotnet build` + `dotnet test` 全部通過（≥ 8 + 新增測試）

---

### Phase 1：規格提取——Deterministic Extractors

**目標**：從正規化文字中機械式提取結構化規格。

#### 1a. SrdExtractor（.srd → SrdSpec）

- [ ] `ISrdExtractor` 介面 + `SrdExtractor` 實作
  - 解析 `column=(type=... name=... dbname="...")` → `SrdColumn`
  - 解析 `retrieve="..."` → 原始 SQL（含 PBSELECT 語法需轉換）
  - 解析 `arguments=(("name", type), ...)` → `SrdArgument`
  - 從 `dbname` 提取涉及的資料表名
- [ ] 測試：用 3-5 個代表性 `.srd` 做 golden test
  - 簡單：`d_signkind.srd`（無參數、單表）
  - 中等：`d_agent.srd`（有參數、有 BETWEEN）
  - 複雜：`d_list.srd`（UNION、多 JOIN、子查詢）
  - PBSELECT：`ds_unt.srd`（PBSELECT 語法）

**驗收標準**：
- [ ] 35 個 `.srd` 全部提取成功（容許 SQL 原文保留不轉換）
- [ ] 欄位提取數量與手動盤點一致

#### 1b. SruExtractor（.sru → SruSpec）

- [ ] `ISruExtractor` 介面 + `SruExtractor` 實作
  - 解析 `global type ... from ...` → ClassName、ParentClass
  - 解析 `type variables ... end variables` → InstanceVariables
  - 解析 `forward prototypes ... end prototypes` → Prototypes
  - 解析 `function/subroutine ... end function/end subroutine` → Routines（連同函式本文）
  - 解析 `event/on ... end event/end on` → EventBlocks（復用現有 `PbScriptExtractor` 邏輯）
  - 掃描函式本文中的 DataWindow 引用（`datawindow=` 或 `.retrieve(` 等模式）
- [ ] 測試：
  - `n_sign.sru`：期望 18 個 prototypes、≥ 12 個 routines
  - `uo_sign_record.sru`：期望 19 個 prototypes、19 個 routines
  - 簡單 `.sru`（如 `uo_ds.sru`）：1-2 個方法

**驗收標準**：
- [ ] 關鍵 `.sru`（`n_sign`、`uo_sign_record`、`n_sky_webbase`）的 prototype 提取數量與手動盤點一致
- [ ] 函式本文提取覆蓋率 ≥ 90%（允許巢狀結構邊界情況少量遺漏）

#### 1c. JspExtractor（.jsp → JspInvocation）

- [ ] `IJspExtractor` 介面 + `JspExtractor` 實作
  - 模式匹配：`component.of_xxx(...)` → ComponentName + MethodName
  - 模式匹配：`request.getParameter("xxx")` → HttpParameters
  - JSP 為 UTF-8，直接用文字比對
- [ ] 測試：
  - `sign_00.jsp`：應提取 `n_sign.of_sign_00` + 7 個 HTTP 參數
  - `createSign.jsp`：應提取 `uo_sign_record.uf_create_sign`

**驗收標準**：
- [ ] 18 個 JSP 全部成功提取方法呼叫名稱
- [ ] HTTP 參數提取率 ≥ 90%

#### 1d. SpecReportBuilder（組裝 + 報告）

- [ ] `MigrationSpec` 組裝：對齊 JSP 呼叫 → PB prototype → DataWindow 引用
- [ ] 輸出 `output/spec/report.md`：
  - Endpoint 候選清單（含 HTTP method/route 建議，參考方法論 2.2 表格）
  - DataWindow 規格摘要
  - Unresolved 清單（JSP 呼叫但無對應 PB 定義的方法）
- [ ] 輸出 `output/spec/datawindows/*.json`（每個 SrdSpec 一份）
- [ ] 輸出 `output/spec/components/*.json`（每個 SruSpec 一份）

**驗收標準**：
- [ ] Spec 報告可人眼審查，包含所有 14 個方法論 2.2 endpoint 候選
- [ ] Unresolved 清單正確列出在繼承鏈中無法追蹤的方法
- [ ] 所有 JSON 可被 `System.Text.Json` 反序列化回原型別

---

### Phase 2：接入 Pipeline——Spec-Driven Generation

**目標**：將規格提取結果接入現有 Orchestrator，使 AI 生成時有結構化 context。

- [ ] `MigrationState` 新增 `Normalizing`、`Analyzing`
- [ ] `MigrationRequest` 新增 `SourceDirectory`（可選，用於分析模式）
- [ ] `MigrationOrchestrator` 擴充：
  - 新增 `Normalizing` 階段：掃描目錄 → 正規化全部檔案 → 產出 `SourceArtifact[]`
  - 新增 `Analyzing` 階段：對每個 artifact 呼叫對應 Extractor → 組裝 `MigrationSpec`
  - `Generating` 階段改用 `BuildSpecDrivenPrompt`（當有 MigrationSpec 時）
  - 向後相容：單檔 event-only 模式不受影響
- [ ] `PromptBuilder` 新增：
  - `BuildSpecDrivenPrompt(MigrationSpec spec, PromptTarget target)`
    - `PromptTarget` 可以是單一 endpoint、單一 DataWindow、或整個 component
    - Prompt 包含：方法簽章、函式本文、相關 DataWindow 定義、方法論 2.2 的 API 映射建議
  - 保留 `BuildInitialPrompt` + `BuildRepairPrompt`
- [ ] CLI 新增 `--analyze-only` flag（僅跑 Normalizing + Analyzing，不生成程式碼）
  - 用途：先產出 spec 供人工審查，確認無誤後再啟動生成

**驗收標準**：
- [ ] `--analyze-only` 可對 `source/sign` 產出完整 spec 報告
- [ ] 從 spec 驅動生成的 prompt 內容可追溯到具體來源檔案與行號
- [ ] 既有單檔模式（`--source`）不受影響，8 個現有測試全部通過

---

### Phase 3：品質閘門

**目標**：在生成前後加入 spec 完整度檢查。

- [ ] `ISpecValidator` 介面 + `SpecValidator` 實作
  - 檢查：每個 endpoint candidate 是否有對應的 PB 函式本文
  - 檢查：每個 SrdSpec 的 column type 是否都能映射到 C# type
  - 檢查：跨元件引用是否完整（A 元件呼叫 B 元件的方法，B 是否在 spec 中）
  - 輸出 `SpecValidationResult`（pass/fail + 缺失項清單）
- [ ] Orchestrator 在 `Analyzing` → `Generating` 之間插入 spec 驗證
  - 若有嚴重缺失（Unresolved 的方法佔比 > 30%），回報 warning 但不中止
  - 缺失資訊附加到 prompt 中，讓 AI 知道哪些部分需要人工補充
- [ ] 保留既有 `dotnet build/test` 驗證迴圈（Validating + Repairing 不變）
- [ ] 新增 spec 提取相關的測試（parser 單測 golden file）

**驗收標準**：
- [ ] `dotnet build` + `dotnet test` 全部通過
- [ ] Spec 驗證能偵測出至少一個已知的 unresolved（例如 `of_sign_history_00`）
- [ ] 驗證失敗時不中止程序，而是在 spec 報告中標記

---

### Phase 4：批次化與審核追蹤（次階段規劃）

> 本 Phase 在 Phase 0-3 穩固後方啟動。

- [ ] 支援資料夾批次分析 + 批次單一 endpoint 生成
- [ ] 每次遷移產出 audit trail：
  - `output/audit/{timestamp}/spec.json`（輸入規格）
  - `output/audit/{timestamp}/prompt.md`（實際送給 AI 的完整 prompt）
  - `output/audit/{timestamp}/generated.cs`（AI 產出）
  - `output/audit/{timestamp}/validation.log`（驗證結果）
- [ ] 建立優先級規則：先遷移低複雜度 endpoint（方法論 5.3 ★★ 等級），再處理高複雜度

---

## 5. 中介規格輸出範例

以 `d_signkind.srd` 為例，Phase 1a 產出的 `output/spec/datawindows/d_signkind.json`：

```json
{
  "fileName": "d_signkind.srd",
  "columns": [
    { "name": "sign_kind", "dbName": "s99_sign_kind.sign_kind", "type": "long", "maxLength": null },
    { "name": "sign_kind_name", "dbName": "s99_sign_kind.sign_kind_name", "type": "char", "maxLength": 40 }
  ],
  "retrieveSql": "SELECT sign_kind, sign_kind_name FROM s99_sign_kind",
  "arguments": [],
  "tables": ["s99_sign_kind"]
}
```

以 `sign_00.jsp` 為例，Phase 1c 產出：

```json
{
  "jspFileName": "sign_00.jsp",
  "componentName": "n_sign",
  "methodName": "of_sign_00",
  "parameters": ["pblpath", "year", "sms", "loginid", "agent", "sign_kind", "card_type"],
  "httpParameters": ["pblpath", "year", "sms", "loginid", "agent", "sign_kind", "card_type"]
}
```

Phase 1d 產出的 `output/spec/report.md`（片段）：

```markdown
## Endpoint Candidates

| # | JSP | PB Method | HTTP | Route | Status |
|---|-----|-----------|------|-------|--------|
| 1 | sign_00.jsp | n_sign.of_sign_00 | GET | /api/sign/dashboard | Resolved |
| 2 | sign_content.jsp | n_sign.of_sign_content | GET | /api/sign/list | Resolved |
| ... |
| 15 | sign_history_00.jsp | n_sign.of_sign_history_00 | GET | /api/sign/history | Unresolved (父類) |

## Unresolved Methods
- `of_sign_history_00`：JSP 呼叫但不在 n_sign.sru forward prototypes，可能定義在 n_sky_webbase.sru
- `of_sign_select`：同上
- `of_sign_select_ins`：同上
```

---

## 6. 先後順序與依賴關係

```
Phase 0 ─────→ Phase 1a ──→ Phase 1d ──→ Phase 2 ──→ Phase 3 ──→ Phase 4
(Normalizer)   Phase 1b ──↗        (Pipeline    (Spec      (Batch)
               Phase 1c ──↗         整合)       驗證)
```

- Phase 1a/1b/1c 可平行開發（只要 Phase 0 的 Normalizer 完成）。
- Phase 1d 需要 1a/1b/1c 全部完成。
- Phase 2 需要 Phase 1d 完成。
- Phase 3 可與 Phase 2 後段平行。
- Phase 4 在 Phase 3 之後。

**建議開發順序**：

1. Phase 0（1-2 天）
2. Phase 1a（1-2 天）→ Phase 1b（2-3 天）→ Phase 1c（1 天）→ Phase 1d（1-2 天）
3. Phase 2（2-3 天）
4. Phase 3（1-2 天）

總估 10-15 天。Phase 4 另計。

---

## 7. 驗收標準（全計畫）

| 指標 | 目標 |
|------|------|
| `.srd` 提取成功率 | 35/35 |
| `.sru` prototype 提取覆蓋 | ≥ 87 個（CODEX 盤點基準） |
| `.jsp` 方法映射覆蓋 | 18/18 |
| Unresolved 清單產出 | 正確標記父類繼承缺口 |
| 既有測試不中斷 | 8/8 passed |
| Spec JSON 可反序列化 | 100% |
| `--analyze-only` 可獨立運行 | ✅ |
| `dotnet build` 0 warning | ✅ |
| `dotnet format --verify-no-changes` | ✅ |

---

## 8. 主要風險與對策

| 風險 | 影響 | 對策 |
|------|------|------|
| **BOM 損壞模式不一致** | Phase 0 正規化失敗 | 以 magic bytes 偵測而非假設固定格式；失敗時 fallback UTF-8 |
| **PB 匯出不完整**（父類方法未匯出） | 部分 endpoint 無法提取函式本文 | Unresolved 報告明確標記，生成時在 prompt 加注「此段邏輯需人工補充」 |
| **`.srd` SQL 格式多變**（PBSELECT vs 原生 SQL、UNION、多行字串） | Parser 覆蓋率不足 | 原始 SQL 文字保留不做語意解析；只提取結構化 metadata（column/argument/table） |
| **巢狀 function 邊界難判斷** | `SruExtractor` 誤切函式本文 | 以 `end function`/`end subroutine` + regex 搭配行為測試；golden file 覆蓋邊界情境 |
| **生成的 prompt 過長**（完整 spec 超過 context window） | AI 輸出品質下降 | `PromptTarget` 機制控制 prompt 粒度：單一 endpoint 或單一 DataWindow，而非全量 |
| **新舊流程耦合** | 改壞現有 event-only 模式 | Orchestrator 以 `SourceDirectory` 是否存在判斷走新/舊路徑；舊路徑零修改 |

---

## 9. 與方法論第 2 章的對齊

| 方法論 2.x | 本計畫對應 | Phase |
|-----------|-----------|-------|
| 2.1 餵 .srd → DTO/Schema | `SrdExtractor` deterministic 提取 → AI 從 `SrdSpec` 產出 DTO | Phase 1a + 2 |
| 2.2 餵 PB method + JSP → API endpoint | `SruExtractor` + `JspExtractor` → `EndpointCandidate` → AI 產出 Controller | Phase 1b + 1c + 2 |
| 2.3 餵商業邏輯 → 規則文件 | `SruExtractor.Routines` 提取函式本文 → AI 摘要為規則文件 | Phase 1b + 2 |

**核心差異**：方法論描述的是「直接把原始碼餵給 AI 一步到位」，本計畫拆成兩步：
1. **Deterministic 提取**：可測、可 diff、不依賴 AI
2. **AI 補強與生成**：基於結構化 spec，而非原始文字

這確保了：
- 規格提取結果可人工審查（不是 AI 黑箱）
- 同一份 spec 可重複生成（不因 AI 隨機性造成規格漂移）
- 失敗時可精確定位是提取錯誤還是生成錯誤
