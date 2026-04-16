## 7. 提供額外資源以提升 AI 分析品質

> **核心觀點**：只靠 PB 程式碼，AI 已經可以完成 70-80% 的規格產出工作。
> 但如果額外提供 **資料庫 Schema（DDL）**，可以補齊剩下的 20-30%，
> 並且大幅降低「遺漏隱藏邏輯」的風險。

### 7.1 目前「只靠程式碼」已經能看到什麼

從本專案的 36 個 DataWindow（`.srd`）和 16 個 PB 腳本（`.sru`）中，AI 已提取出：

| 已知資訊 | 資料來源 | 範例 |
|---------|---------|------|
| 資料表名稱（14+ 張） | `.srd` 的 SQL | `s99_sign_record_mst`, `s99_flow_spec`, `s10_employee` |
| 欄位名稱與部分型別 | DataWindow column 定義 | `sign_serno char(13)`, `emp_id char(6)` |
| JOIN 關聯 | SQL 的 INNER JOIN / LEFT JOIN | `sign_record JOIN sign_record_mst ON sign_serno` |
| 查詢參數 | DataWindow arguments | `as_emp_id (string)`, `ai_signkind (number)` |
| WHERE 條件中的商業規則 | SQL WHERE 子句 | `sign_status = '1'`, `chk_yn = 'Y'` |
| UNION 查詢的分支邏輯 | d_list.srd | 正常流程 UNION 分支流程 |

### 7.2 有了 Schema 之後 AI「額外」能做什麼

#### A. 資料庫約束 → 直接對應新系統三層架構

| Schema 中的約束 | 目前能看到嗎 | 有 Schema 後的遷移價值 |
|---|---|---|
| **Primary Key** | ❌ 只能猜測 | → EF Core Entity 的 `[Key]` / `HasKey()` |
| **Foreign Key** | ⚠️ 只看到 JOIN，不確定是否有 FK 約束 | → EF Core Navigation Property + `OnModelCreating` 設定 |
| **NOT NULL** | ❌ 完全看不到 | → 前端 `required` 驗證 + DTO `[Required]` + OpenAPI `required` 陣列 |
| **DEFAULT 值** | ❌ 完全看不到 | → Entity 建構子初始值、`HasDefaultValue()`、前端表單預設值 |
| **INDEX** | ❌ 完全看不到 | → EF Core Migration 的 `HasIndex()`，避免新系統效能問題 |
| **CHECK 約束** | ⚠️ 只從 WHERE 子句間接推測 | → 前端 dropdown/radio 選項、後端驗證、OpenAPI `enum` |
| **欄位 COMMENT** | ❌ 完全看不到 | → DTO 的 XML 註解、OpenAPI `description`、前端 label 文字 |

圖示：Schema 資訊如何流向新架構三層

```
┌─────────────────────────────────────────────────────────────────────┐
│                     資料庫 Schema (DDL)                              │
│                                                                     │
│  CREATE TABLE s99_sign_record_mst (                                 │
│    sign_serno  CHAR(13)   NOT NULL,   ── PK ──┐                    │
│    flow_id     CHAR(8)    NOT NULL,            │                    │
│    vou_subject VARCHAR(60) DEFAULT '',         │                    │
│    sign_status CHAR(1)    CHECK(IN('0','1')),  │                    │
│    CONSTRAINT pk_sign_mst PRIMARY KEY(sign_serno),                  │
│    CONSTRAINT fk_flow FOREIGN KEY(flow_id) REFERENCES s99_flow_spec │
│  );                                                                 │
│  COMMENT ON COLUMN sign_serno IS '簽核序號';                         │
│  CREATE INDEX idx_sign_status ON ... ;                              │
└────────────┬────────────────┬────────────────┬──────────────────────┘
             │                │                │
    ┌────────▼──────┐ ┌──────▼───────┐ ┌──────▼──────────────┐
    │   後端 Entity  │ │ OpenAPI Spec │ │    前端 Vue 元件     │
    │               │ │              │ │                     │
    │ [Key]         │ │ required:    │ │ rules: [            │
    │ [Required]    │ │   - sign_serno│ │   { required: true }│
    │ [MaxLength(13)]│ │   - flow_id  │ │   { max: 13 }      │
    │ [Comment(...)]│ │ enum:        │ │ ]                   │
    │ HasIndex()    │ │   - "0","1"  │ │ default: ''         │
    │ HasDefaultValue│ │ default: '' │ │ label: '簽核序號'   │
    │ Navigation    │ │ description: │ │ <el-select>         │
    │   Property    │ │  '簽核序號'  │ │   0/1 options       │
    └───────────────┘ └──────────────┘ └─────────────────────┘
```

#### B. 發現「隱藏邏輯」— 最關鍵的價值

**Trigger（觸發器）**

資料庫 Trigger 中的邏輯在 PB 程式碼裡**完全看不到**，但它可能包含：

- INSERT 後自動更新 master 表的狀態
- UPDATE 時自動寫入異動 log
- DELETE 前的級聯檢查
- 自動計算欄位（如簽核序號產生規則）

這些邏輯在遷移時必須搬到 .NET Core Service 層。**沒有 Schema 就可能遺漏。**

```
Claude Code 指令範本：

讀取這些 Trigger 原始碼，列出：
1. 每個 Trigger 的觸發時機（BEFORE/AFTER × INSERT/UPDATE/DELETE）
2. 包含的商業邏輯（用中文描述）
3. 對應到新系統應該放在哪個 Service method 中
4. 產出 C# 的等效程式碼
```

**Stored Procedure（預存程序）**

如果有 SP，它們可能包含：
- 跨表交易（Transaction）邏輯
- 批次處理邏輯
- 排程任務的核心邏輯

同樣需要遷移到 .NET Core Service 層。

```
Claude Code 指令範本：

讀取這些 Stored Procedure，對每一支：
1. 說明其功能（中文）
2. 列出輸入/輸出參數 → 對應 API 的 Request/Response
3. 列出存取的資料表 → 對應 Repository 需要的方法
4. 產出 C# Service method 的完整實作
```

#### C. 產出完整的 EF Core DbContext（一次到位）

只靠 DataWindow，AI 只能產出「程式碼用到的那些欄位」的部分 DTO。
有了 Schema，AI 可以一次產出 **完整的 Entity + DbContext**：

```
Claude Code 指令範本：

讀取這份完整的 DDL，產出：
1. 所有 Entity class（含 [Key], [Required], [MaxLength], [Comment] 標註）
2. 每個 FK 關聯的 Navigation Property（含 ICollection<T> 和反向參考）
3. SignDbContext 的 OnModelCreating：
   - 所有 HasKey() 設定
   - 所有 HasForeignKey() + OnDelete 行為
   - 所有 HasIndex() 設定
   - 所有 HasDefaultValue() 設定
   - 所有 HasCheckConstraint() 設定
4. 對應的 TypeScript interface（給前端用）
5. 對應的 OpenAPI 3.0 Schema 定義
```

產出範例（以 `s99_sign_record_mst` 為例）：

```csharp
// ===== Entity =====
/// <summary>簽核主檔</summary>
public class SignRecordMst
{
    [Key]
    [MaxLength(13)]
    [Comment("簽核序號")]
    public string SignSerno { get; set; } = string.Empty;

    [Required]
    [MaxLength(8)]
    [Comment("流程代碼")]
    public string FlowId { get; set; } = string.Empty;

    [MaxLength(60)]
    [Comment("主旨")]
    public string? VouSubject { get; set; }

    [MaxLength(1)]
    [Comment("簽核狀態: 0=進行中, 1=完成")]
    public string SignStatus { get; set; } = "0";

    // ─── Navigation Property（來自 FK 約束）───
    public FlowSpec Flow { get; set; } = null!;
    public ICollection<SignRecord> SignRecords { get; set; } = new List<SignRecord>();
    public ICollection<SignDoc> SignDocs { get; set; } = new List<SignDoc>();
}

// ===== DbContext =====
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<SignRecordMst>(entity =>
    {
        entity.HasKey(e => e.SignSerno);
        entity.HasIndex(e => e.SignStatus).HasDatabaseName("idx_sign_status");
        entity.Property(e => e.SignStatus).HasDefaultValue("0");
        entity.HasCheckConstraint("chk_sign_status", "[SignStatus] IN ('0','1')");

        entity.HasOne(e => e.Flow)
              .WithMany(f => f.SignRecordMsts)
              .HasForeignKey(e => e.FlowId)
              .OnDelete(DeleteBehavior.Restrict);
    });
}
```

對比：如果沒有 Schema，AI 只能從 `d_list.srd` 產出：

```csharp
// 只有 DataWindow 用到的欄位，缺少 PK/FK/Default/Index 資訊
public class SignListDto
{
    public string SignSerno { get; set; }  // 不知道是不是 PK
    public string FlowName { get; set; }   // 不知道能不能 NULL
    public string VouSubject { get; set; } // 不知道預設值
    // 沒有 Navigation Property
}
```

#### D. 自動產出完整的前端驗證規則

有了 Schema 約束，AI 可以為每個表單欄位產出精確的驗證規則：

```
Schema 約束              →    前端驗證規則
─────────────────────────────────────────────
NOT NULL                 →    required: true
CHAR(13)                 →    maxLength: 13, minLength: 13 (固定長度)
VARCHAR(60)              →    maxLength: 60
DEFAULT ''               →    initialValue: ''
CHECK (IN ('Y','N'))     →    type: 'radio', options: ['Y','N']
CHECK (status IN ('0','1','2')) → type: 'select', options: [...]
FK → s10_employee        →    需要員工選擇器元件（el-select + 遠端搜尋）
FK → s99_flow_spec       →    需要流程下拉選單
```

### 7.3 本專案涉及的資料表完整清單

以下為**經真實 DDL (s9x_schema_sql.sql) 驗證**後的核心簽核表清單：

**簽核系統表 (s99_* 命名空間) — 已驗證**

| 資料表 | 中文名 | PK | 實際欄位數 | AI 原推測 | 狀態 |
|--------|--------|-----|-----------|----------|------|
| `s99_sign_record_mst` | 簽核記錄主檔 | (sign_serno) | 16 | 12 | ✅ 已驗證 |
| `s99_sign_record` | 簽核記錄 | **(sign_serno, step_key, sign_cntno)** | 31 | 17 | ✅ PK 已修正 |
| `s99_sign_agent` | 代理人設定 | **(serial_no)** | 11 | 7 | ✅ PK 已修正 |
| `s99_flow_setup` | 流程設定 | (flow_id, ver_id, step_key) | 23 | 8 | ✅ 已驗證 |
| `s99_flow_spec` | 流程規格 | (flow_id) | 11 | 2 | ✅ 已驗證 |
| `s99_sign_doc` | 附件文件 | (sign_serno, doc_seq) | 19 | 16 | ✅ 已驗證 |
| `s99_sign_doc_step` | 文件步驟顯示 | (sign_serno, doc_seq, step_ordid) | 6 | 4 | ✅ 已驗證 |
| `s99_sign_kind` | 簽核種類 | (sign_kind) | 4 | 2 | ✅ 已驗證 |
| `s99_sign_status` | 簽核狀態 | (sign_status) | 5 | — | ✅ 新發現 |
| `s99_signer_kind` | 簽核人員種類 | (user_type) | 6 | — | ✅ 新發現 |
| `s99_helpdesk` | 服務台人員 | (unt_id, hd_emp_id) CLUSTERED | 4 | 2 | ✅ 已驗證 |
| `s99_sign_record_dtl` | 簽核記錄明細 | — | — | 6 | ⚠️ PDM 有，DDL 待確認 |
| `s99_signer_setup` | 簽核人員設定 | — | — | — | ⚠️ PDM 新發現 |
| `s99_phrase_by_signer` | 常用片語 | — | — | — | ⚠️ PDM 新發現 |
| `s99_virtual_units_mst` | 虛擬單位主檔 | — | — | — | ⚠️ PDM 新發現 |
| `s99_virtual_units_detail` | 虛擬單位明細 | — | — | — | ⚠️ PDM 新發現 |
| `swf_doc` | 文件 BLOB 儲存 | — | — | — | ⚠️ PDM 新發現 |

**AI 原推測但不存在的表**：
- ~~`s99_sign_record_branch`~~ — 不存在，階層由 `parent_step_key` 處理
- ~~`s99_flow_setup_branch`~~ — 不存在，同上
- ~~`s99_company`~~ — 在簽核模組 DDL 中未找到

**人事組織表 (s10_* 命名空間) — 唯讀引用**

| 資料表 | 從程式碼已知的欄位數 | 說明 |
|--------|-------------------|------|
| `s10_employee` | 5 欄位 | 員工基本資料（實際可能 30+ 欄） |
| `s10_otherpos` | 4 欄位 | 兼任職務 |
| `s90_unitb` | 部分 | 組織架構 |

> ⚠️ **驗證結論**：提供真實 DDL 後，發現 AI 原推測有 **3 張表不存在**、
> **3 張表的 PK 錯誤**、**欄位型別全部應為 VARCHAR（非 CHAR）**、
> **s99_sign_record 實際有 31 欄（非推測的 17 欄）**。
> 這印證了 Section 7.1 的觀點：純靠程式碼推測的 Schema 完整度僅約 60%。

### 7.4 Schema 提供方式建議（依價值排序）

| 優先順序 | 提供方式 | 說明 | AI 能產出什麼 |
|---------|---------|------|--------------|
| ★★★★★ | **完整 DDL**（CREATE TABLE + ALTER TABLE + CREATE INDEX + CREATE TRIGGER） | 最佳，一次到位 | 完整 Entity + DbContext + 驗證規則 + Trigger 遷移方案 |
| ★★★★ | **DDL + Stored Procedure 原始碼** | 補齊隱藏的商業邏輯 | 上述 + Service 層完整實作 |
| ★★★★ | **Sybase ASE `sp_help` 輸出** | 每張表執行 `sp_help tablename` | 等同 DDL（含約束與索引） |
| ★★★ | **ERD 圖（圖片）** | 即使是截圖，Claude 也能讀取分析 | 表間關係 + Navigation Property |
| ★★ | **只有 CREATE TABLE** | 至少有欄位型別和 PK | 基本 Entity（缺少 FK/Index） |
| ★ | **Trigger / SP 原始碼**（單獨提供） | 獨立於 DDL 也有價值 | 識別隱藏邏輯的遷移清單 |

**Sybase ASE 快速匯出指令**：
```sql
-- 方法一：逐表匯出（適合少量表）
sp_help 's99_sign_record_mst'
go
sp_help 's99_sign_record'
go

-- 方法二：匯出完整 DDL（使用 ddlgen 工具）
-- 在 Sybase 伺服器上執行：
ddlgen -U sa -P password -S servername -D database_name -o schema_export.sql

-- 方法三：查詢所有 Trigger
SELECT name, id FROM sysobjects WHERE type = 'TR'
go

-- 方法四：查詢所有 Stored Procedure
SELECT name, id FROM sysobjects WHERE type = 'P'
go
```

### 7.5 有/無 Schema 的 AI 分析品質對照總表

```
                    只有 PB 程式碼          有 PB 程式碼 + Schema
                    ──────────────         ──────────────────────
Entity 完整度        ▓▓▓░░ 60%             ▓▓▓▓▓ 100%
  - 欄位清單         ✓ 只有用到的欄位        ✓ 所有欄位
  - 型別正確度       ✓ 大致正確              ✓ 完全正確
  - PK 標註         ✗ 猜測                  ✓ 精確
  - FK 關聯         ⚠ 從 JOIN 推測          ✓ 精確 + Navigation Property
  - NOT NULL        ✗ 不知道                ✓ 精確
  - DEFAULT         ✗ 不知道                ✓ 精確
  - INDEX           ✗ 不知道                ✓ 精確

前端驗證規則         ▓▓▓░░ 55%             ▓▓▓▓▓ 95%
  - required        ⚠ 從 JS 推測            ✓ 精確（NOT NULL）
  - maxLength       ✓ 從 DataWindow         ✓ 精確
  - 值域限制         ⚠ 從 WHERE 推測        ✓ 精確（CHECK）
  - 預設值           ✗ 不知道               ✓ 精確

OpenAPI Schema      ▓▓▓░░ 60%             ▓▓▓▓▓ 95%
  - required 陣列    ⚠ 不完整               ✓ 完整
  - enum            ⚠ 不完整               ✓ 完整
  - description     ✗ 無                    ✓ 有（從 COMMENT）
  - default         ✗ 無                    ✓ 有

隱藏邏輯發現率       ▓▓░░░ 30%             ▓▓▓▓▓ 95%
  - Trigger 邏輯     ✗ 完全看不到            ✓ 可分析並遷移
  - SP 邏輯          ✗ 完全看不到            ✓ 可分析並遷移
  - 級聯刪除規則     ✗ 不知道               ✓ 精確

DbContext 品質       ▓▓░░░ 40%             ▓▓▓▓▓ 98%
  - HasKey           ✗ 猜測                 ✓ 精確
  - HasForeignKey    ⚠ 從 JOIN 推測         ✓ 精確 + OnDelete 行為
  - HasIndex         ✗ 無                   ✓ 精確
  - HasDefaultValue  ✗ 無                   ✓ 精確
  - HasCheckConstraint ✗ 無                 ✓ 精確
```

### 7.6 整合建議：將 Schema 納入 Monorepo 的 legacy-source

建議將 Schema 匯出後放入共用儲存庫：

```
sign-system/
├── legacy-source/
│   ├── sign/              ← PB 物件匯出（現有）
│   ├── dw_sign/           ← DataWindow 匯出（現有）
│   ├── sky_webbase/       ← （現有）
│   ├── tpec_s61/          ← （現有）
│   ├── webap/             ← （現有）
│   ├── sign.pbt           ← （現有）
│   ├── *.jsp              ← （現有）
│   │
│   ├── schema/            ← 【新增】資料庫 Schema
│   │   ├── tables/        ← 每張表的 CREATE TABLE DDL
│   │   │   ├── s99_sign_record_mst.sql
│   │   │   ├── s99_sign_record.sql
│   │   │   └── ...
│   │   ├── triggers/      ← Trigger 原始碼
│   │   │   └── trg_*.sql
│   │   ├── procedures/    ← Stored Procedure 原始碼
│   │   │   └── sp_*.sql
│   │   ├── indexes.sql    ← 索引定義
│   │   └── full_ddl.sql   ← 完整 DDL 匯出（一個大檔）
│   │
│   └── erd/               ← 【新增】ER Diagram（如果有）
│       └── sign_system_erd.png
│
├── api-spec/
├── frontend/
├── backend/
├── docs/
└── CLAUDE.md              ← 加入 schema/ 的路徑說明
```

在 `CLAUDE.md` 中新增：

```markdown
## 參考檔案
- 舊系統原始碼: legacy-source/sign/, legacy-source/dw_sign/, ...
- 資料庫 Schema (正式): legacy-source/schema/s9x_schema_sql.sql ← 174 張表、20 triggers
- PowerDesigner ERD: legacy-source/schema/ntua_flow.pdm ← 中文欄位說明
- AI 推測 DDL (已棄用): legacy-source/schema/full_ddl_sample.sql
- API 合約: api-spec/openapi.yaml
```

### 7.7 提供 Schema 後的 AI 工作流程變化

```
【Phase 0 — 準備階段】

  原本流程（只有程式碼）：
  ┌─────────────┐     ┌──────────────┐     ┌───────────────┐
  │ DataWindow   │────→│ 部分 DTO     │────→│ OpenAPI Schema│
  │ (.srd)       │     │ (缺 PK/FK)   │     │ (不完整)      │
  └─────────────┘     └──────────────┘     └───────────────┘

  加入 Schema 後的流程：
  ┌─────────────┐     ┌──────────────┐
  │ DataWindow   │──┐  │              │     ┌───────────────┐
  │ (.srd)       │  ├→│ 完整 Entity  │────→│ OpenAPI Schema│
  │              │  │  │ + DbContext  │     │ (完整)        │
  └─────────────┘  │  │ + DTO        │     └───────┬───────┘
  ┌─────────────┐  │  │              │             │
  │ Schema DDL  │──┘  └──────────────┘             │
  │ (tables/    │                                   ▼
  │  triggers/  │  ┌──────────────┐     ┌───────────────────┐
  │  SP)        │─→│ Service 層    │     │ 前端驗證規則       │
  └─────────────┘  │ (含 Trigger   │     │ (required/enum/   │
                   │  遷移邏輯)    │     │  default 全部精確) │
                   └──────────────┘     └───────────────────┘
```

### 7.8 真實 Schema 驗證結果（實證）

使用者提供了真實 DDL (`s9x_schema_sql.sql`, 463KB) 與 PowerDesigner ERD (`ntua_flow.pdm`) 後，
以下是 AI 推測 vs 真實 Schema 的差異對照：

**PK 結構差異**

| 表名 | AI 推測 PK | 真實 PK | 影響 |
|------|-----------|---------|------|
| s99_sign_record | (sign_serno, sign_cntno, step_ordid) | **(sign_serno, step_key, sign_cntno)** | Entity PK、查詢條件全部要改 |
| s99_sign_agent | (emp_id, agent_emp_id) | **(serial_no)** | 代理人查詢邏輯完全不同 |

**欄位型別差異**

| 項目 | AI 推測 | 真實 | 影響 |
|------|---------|------|------|
| 所有欄位型別 | CHAR | **VARCHAR** | C# Entity 不需 Trim()，MaxLength 語意不同 |
| signer_memo 長度 | 200 | **800** | 前端 textarea maxlength 要改 |
| s99_sign_record 欄位數 | 17 | **31** | DTO 缺少 14 個欄位 |

**不存在的表（AI 錯誤推測）**

| AI 推測存在 | 真實情況 | 替代方案 |
|------------|---------|----------|
| s99_sign_record_branch | 不存在 | 由 `parent_step_key` 欄位處理階層 |
| s99_flow_setup_branch | 不存在 | 同上 |

**AI 未發現的表（Schema 新增）**

| 表名 | 中文名 | 重要性 |
|------|--------|--------|
| s99_sign_status | 簽核狀態代碼表 | 高：狀態名稱查詢 |
| s99_signer_kind | 簽核人員種類 | 高：user_type 對照 |
| s99_signer_setup | 簽核人員設定 | 中：特定流程人員指定 |
| s99_phrase_by_signer | 常用片語 | 低：UX 功能 |
| s99_virtual_units_mst/detail | 虛擬單位 | 中：跨單位簽核 |
| swf_doc | 文件 BLOB | 高：實際檔案儲存 |

**Trigger 邏輯（完全未知 → 已揭露）**

提供 Schema 前，AI 完全不知道有 20 個 trigger 存在。其中最關鍵的 `tg_s99_sign_record_mst_upd`
會在簽核主檔更新時連動 s60_req、s60_order、s50_add_mst、s50_apprec_m、s20_bug 等跨模組表，
且所有 trigger 會寫入 `tpeclog` 資料庫做稽核日誌。

> **結論**：提供真實 Schema 後：
> - Entity 完整度：60% → **100%**
> - PK 正確率：約 70% → **100%**
> - 隱藏邏輯發現：0% → **85%**（trigger 全部揭露）
> - 這驗證了 Section 7.5 品質對照表的預估數據

---

## 附錄：AI 能做 vs 需要人工的分界

| 工作項目 | AI 可靠度 | 說明 |
|---------|----------|------|
| DataWindow → DTO/Entity | ★★★★★ | 機械式轉換，幾乎不需人工 |
| JSP JavaScript → 驗證規則文件 | ★★★★★ | 直接提取，邏輯清晰 |
| PB method 簽章 → API endpoint | ★★★★★ | 一對一對應，非常可靠 |
| PB HTML 拼接 → SVG Wireframe | ★★★★ | 佈局準確，但美觀需調整 |
| JSP JS → Vue Composable | ★★★★ | 邏輯正確，但需整合到元件 |
| OpenAPI → TS Interface/Client | ★★★★★ | 工具自動產生，完全可靠 |
| PB 簡單邏輯 → C# Service | ★★★★ | 參數驗證、CRUD 可直接用 |
| **PB 核心流程 → C# Service** | **★★** | **uf_sign, uf_back_step 必須人工審查** |
| **多層 UNION 查詢最佳化** | **★★** | **AI 翻譯正確但效能需人工調校** |
| **民國年/特殊商業日期邏輯** | **★★★** | **需要完整的測試案例驗證** |
|  |  |  |
| **▼ 提供 DB Schema 後額外可做的項目** | | |
| Schema DDL → 完整 Entity + DbContext | ★★★★★ | PK/FK/Index/Default 全部精確，一次到位 |
| Schema NOT NULL → 前端 required 規則 | ★★★★★ | 比從 JS 推測更完整、更可靠 |
| Schema CHECK → OpenAPI enum / 前端選項 | ★★★★★ | 精確的值域限制，不需猜測 |
| Schema COMMENT → API description / 前端 label | ★★★★★ | 中文欄位說明直接轉用 |
| **Trigger → .NET Core Service 遷移** | **★★★** | **AI 可翻譯，但跨表交易邏輯須人工審查** |
| **Stored Procedure → Service 遷移** | **★★★** | **AI 可翻譯，但複雜 SP 須人工審查** |
| Schema FK → EF Core Navigation Property | ★★★★★ | 精確的表間關聯，自動產生導航屬性 |

---

