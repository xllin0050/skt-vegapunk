## 5. 後端 AI 能協助什麼

### 5.1 從 DataWindow (.srd) → Entity + DTO + DbContext

**這是 AI 最高效的轉換**。每個 `.srd` 檔案就是一個完整的資料契約。

**Claude Code 指令範本**：

```
讀取此 DataWindow .srd 檔案（UTF-16 LE，字元間有空格）。
提取 SQL 和欄位定義後，產出：

1. C# Entity class（對應實體資料表，含 [Table], [Column] attribute）
2. C# DTO class（對應查詢結果的形狀）
3. EF Core DbContext 中的 OnModelCreating 設定
4. Repository 的查詢方法（將 DataWindow SQL 轉為 LINQ 或 FromSqlRaw）
```

**實際範例 — `d_signkind.srd`**：

AI 從 .srd 提取：
- Table: `s99_sign_kind`
- Columns: `sign_kind (long, PK)`, `sign_kind_name (char 40)`
- SQL: `SELECT sign_kind, sign_kind_name FROM s99_sign_kind`

AI 產出：

```csharp
// Entity
[Table("s99_sign_kind")]
public class SignKind
{
    [Key]
    [Column("sign_kind")]
    public int SignKindId { get; set; }

    [Column("sign_kind_name")]
    [StringLength(40)]
    public string SignKindName { get; set; }
}
```

**實際範例 — `ds_pick_person.srd`（較複雜，含 UNION + JOIN）**：

AI 從 .srd 提取：
- 涉及表: `s10_employee`, `s90_unitb`, `s10_otherpos`
- UNION 查詢：正職 + 兼任
- 參數: `as_unit (string)`, `as_emp (string)`
- 條件: `emp_status LIKE '1%'`, `unt_use_yn = 'Y'`

AI 產出包含：
- Entity: `Employee`, `Unit`, `OtherPosition`
- DTO: `EmployeeSearchResultDto`
- Repository method: `SearchEmployees(string unitId, string keyword)`

### 5.2 DataWindow 全量清單 — 可批次餵給 AI

| # | .srd 檔案 | 來源表 | AI 產出目標 |
|---|----------|-------|-----------|
| 1 | `d_list.srd` | sign_record + sign_record_mst + flow_spec + flow_setup（含 UNION 分支） | `PendingSignItemDto` + 複雜 LINQ 查詢 |
| 2 | `d_signkind.srd` | s99_sign_kind | `SignKindDto`（簡單 CRUD） |
| 3 | `d_count.srd` | sign_kind + 子查詢 count（7種 user_type 判斷） | `SignKindCountDto` + 複雜計數邏輯 |
| 4 | `d_agent.srd` | s99_sign_agent | `AgentDto` |
| 5 | `d_branch.srd` | flow_setup_branch | `BranchOptionDto` |
| 6 | `d_back_stepkey.srd` | flow_setup | `BackStepOptionDto` |
| 7 | `d_doc.srd` | s99_sign_doc | `SignDocumentDto` |
| 8 | `d_countersign_signlist.srd` | sign_record (會簽) | `CountersignDetailDto` |
| 9 | `d_reject.srd` | 退回紀錄 | `RejectRecordDto` |
| 10 | `d_member.srd` | 成員查詢 | `MemberDto` |
| 11 | `d_set.srd` | flow_setup | `FlowSetupDto` |
| 12 | `ds_pick_person.srd` | employee + unit + otherpos (UNION) | `EmployeeSearchResultDto` |
| 13 | `ds_unt.srd` | s90_unitb | `UnitOptionDto` |
| 14 | `d_signrecord.srd` | sign_record 明細 (4段 UNION) | `SignRecordDetailDto` |
| 15 | `d_get_flow.srd` | 簽核流程 | `FlowDto` |
| 16 | `d_get_flow_history.srd` | 流程歷史 | `FlowHistoryDto` |
| 17 | `d_s99_sign_record_mst.srd` | 主檔 | `SignRecordMstEntity` |

### 5.3 從 PB method 商業邏輯 → .NET Core Service

**核心工作流程元件 `uo_sign_record.sru` 的方法對應**：

| PB method | .NET Core Service method | 複雜度 | AI 輔助方式 |
|-----------|------------------------|--------|-----------|
| `uf_sign()` | `SignService.ApproveAsync()` | ★★★★★ | AI 提取規則 → 人工審查 → AI 產出骨架 |
| `uf_create_sign()` | `SignService.CreateSignAsync()` | ★★★★ | AI 分析參數解析邏輯 + 資料寫入順序 |
| `uf_back_step()` | `SignService.BackStepAsync()` | ★★★★ | AI 分析退回邏輯的狀態轉換 |
| `uf_set_helpdesk()` | `SignService.AssignHelpdeskAsync()` | ★★ | AI 可直接翻譯 |
| `uf_set_countersign()` | `SignService.SetCountersignAsync()` | ★★★ | AI 分析分隔符解析邏輯 |
| `uf_build_countersign()` | `SignService.BuildCountersignAsync()` | ★★★ | AI 解析 `@a@`, `@b@` 分隔符 |
| `uf_get_flow()` | `SignQueryService.GetFlowAsync()` | ★★ | AI 可直接翻譯 |
| `uf_get_flow_list()` | `SignQueryService.GetFlowListAsync()` | ★★ | DataWindow → LINQ |

**Claude Code 指令範本（Service 層）**：

```
分析以下 PowerBuilder method uf_sign 的邏輯。
這是簽核系統的核心審核方法。

請：
1. 用中文列出所有商業規則（不寫程式碼）
2. 列出所有資料庫讀寫操作及順序
3. 列出所有狀態轉換（sign_status, sign_yn, chk_yn 等欄位的變化）
4. 列出需要 Transaction 保護的操作範圍

然後產出：
- C# Service method 的骨架（interface + implementation）
- 標註哪些區塊需要人工審查商業邏輯
- 產出對應的 Unit Test 骨架
```

### 5.4 特殊轉換注意事項

**AI 在後端轉換時需要注意的 PB 特有模式**：

| PB 特有模式 | AI 的翻譯策略 |
|------------|-------------|
| `of_parsing(str, "@a@", arr[])` 自定分隔符解析 | → `string.Split("@a@")` 或自訂 Parser |
| 民國年 `yyy/mm/dd` | → `DateTimeHelper.FromRocDate()` 集中處理 |
| `isnull(col, '')` (Sybase) | → `COALESCE(col, '')` (SQL Server) 或 EF Core `?? ""` |
| DataWindow 的 retrieve + modify | → EF Core 的 LINQ query + tracking update |
| `it1` 次要資料庫連線 | → EF Core 多 DbContext 注入 |
| zlib.dll compress/uncompress | → `System.IO.Compression` |
| HTML 字串拼接回傳 | → **完全不翻譯**，改回傳 JSON，由前端渲染 |

### 5.5 Sybase ASE 15.7 → 新資料庫遷移指引

#### 5.5.1 目標資料庫選擇

| 候選資料庫 | 優點 | 缺點 | 建議 |
|-----------|------|------|------|
| **SQL Server** | Sybase ASE 語法最相近（同源）、EF Core 支援最成熟、校務系統常見 | 授權費用 | ★★★★★ **首選**（政府/學校通常有微軟授權） |
| **PostgreSQL** | 免費開源、功能強大、EF Core 支援完整 | 語法差異較大 | ★★★★ 如果預算考量 |
| **繼續用 Sybase ASE** | 零遷移成本 | Sybase 已被 SAP 收購，EF Core 無官方 Provider、未來維護風險高 | ★★ 不建議 |

#### 5.5.2 Sybase ASE 15.7 特有語法 → 新資料庫對照表

從本專案程式碼中實際發現的 Sybase 語法：

| Sybase ASE 15.7 語法 | 本專案出處 | SQL Server 對應 | PostgreSQL 對應 | EF Core / C# 對應 |
|---|---|---|---|---|
| `ISNULL(col, '')` | d_list.srd, d_signrecord.srd（大量使用） | `ISNULL(col, '')` ✓ 相同 | `COALESCE(col, '')` | `col ?? ""` 或 `.HasDefaultValue("")` |
| `GETDATE()` | n_sign.sru `select getdate() into :lt_srv_date` | `GETDATE()` ✓ 相同 | `NOW()` 或 `CURRENT_TIMESTAMP` | `DateTime.Now` 或 `DateTime.UtcNow` |
| `CONVERT(type, expr)` | 可能存在於 SP/Trigger | `CONVERT(type, expr)` ✓ 相同 | `CAST(expr AS type)` | C# 型別轉換 |
| `:parameter` 參數前綴 | 所有 .srd 的 arguments | `@parameter` | `@parameter` 或 `$1` | EF Core 自動處理 |
| `CHAR(n)` 固定長度字串 | 全部 DataWindow 欄位定義 | `CHAR(n)` ✓ 相同 | `CHAR(n)` ✓ 相同 | `[MaxLength(n)]` + `IsFixedLength()` |
| `BETWEEN dt_s AND dt_e` | d_agent.srd 代理人日期區間 | ✓ 相同 | ✓ 相同 | `.Where(x => x.Date >= start && x.Date <= end)` |
| `EXISTS (SELECT 1 ...)` | d_list.srd, d_count.srd | ✓ 相同 | ✓ 相同 | `.Where(x => context.Table.Any(...))` |

> **好消息**：Sybase ASE 和 SQL Server 同源（都源自 Sybase SQL Server），
> 語法相容度約 **90%+**。選擇 SQL Server 作為目標資料庫，SQL 遷移成本最低。

#### 5.5.3 Sybase ASE 15.7 的特殊注意事項

**A. 字元集與排序規則**

```
Sybase ASE 15.7 (校務系統)          新資料庫
─────────────────────              ─────────
Big5 或 UTF-8 字元集                → SQL Server: nvarchar (UTF-16)
                                    → PostgreSQL: UTF-8
排序規則: 可能是 big5bin 或 utf8    → SQL Server: Chinese_Taiwan_Stroke_CI_AS
                                    → PostgreSQL: zh_TW.UTF-8

⚠ 注意：
- PB DataWindow 匯出是 UTF-16 LE（已確認，每個字元間有空格）
- Sybase ASE 的 CHAR(n) 在 Big5 下，n 是 byte 數不是字元數
  例如 CHAR(40) 存繁體中文只能放 20 個字
- 遷移到 SQL Server 的 NVARCHAR(40) 則可放 40 個中文字
- 這會影響 [MaxLength] 的設定值！

AI 轉換指令補充：
  在產出 Entity 時，如果原始欄位是 CHAR(n)，
  需確認是否為中文欄位：
  - 中文欄位: CHAR(40) → [MaxLength(20)] (byte ÷ 2)
  - 英數欄位: CHAR(13) → [MaxLength(13)] (不變)
  需搭配 Schema 或實際資料確認
```

**B. 民國年處理（校務系統特有）**

```
Sybase ASE 中的日期儲存方式（從程式碼發現）：

  sign_da      CHAR(13)   ← 民國年字串，格式不明（13位可能含時間）
  agent_dt_s   DATETIME   ← 標準 datetime
  upd_dt       CHAR(13)   ← 民國年字串
  doc_uploaddt CHAR(13)   ← 民國年字串

  混合使用 CHAR 字串和 DATETIME 型別！

新系統建議：
  - 統一使用 DateTime / DateTimeOffset
  - 民國年只在「顯示層」轉換，不存入資料庫
  - 建立 RocDateConverter：
    public static class RocDateConverter
    {
        public static DateTime? FromRocString(string? rocDate)
        public static string ToRocString(DateTime date, string format)
    }
```

**C. Jaguar Connection Pool → EF Core Connection Pool**

```
舊架構 (從 uo_trans_y.sru 發現)：
  DBParm = "CacheName='#', UseContextObject='Yes',
            ReleaseConnectionOption='JAG_CM_UNUSED',
            GetConnectionOption='JAG_CM_NOWAIT',
            DisableBind=1"
  → Jaguar (EAServer) 管理連線池，PB 不直接管理

新架構：
  // appsettings.json
  "ConnectionStrings": {
      "SchoolDb": "Server=...;Database=school_db;
                   Pooling=true;Min Pool Size=5;Max Pool Size=100;
                   Connection Timeout=30;"
  }
  → EF Core / ADO.NET 內建連線池，設定更簡單
  → 不需要 Jaguar 中介層
```

**D. 多資料庫連線（校務系統跨子系統）**

```
從 n_sign.sru 發現：

  of_connectdb1(as_trans, as_database, as_cache, as_dbms, as_func, as_message)
  → 支援動態切換資料庫（校務系統不同子系統可能在不同 DB）

  it1 = create using as_trans   ← 主要連線 (簽核 DB)
  it  = inherited               ← 繼承的連線 (可能是共用 DB)

新架構對應：
  // 多 DbContext 注入
  services.AddDbContext<SignDbContext>(opt => opt.UseSqlServer(signConn));
  services.AddDbContext<HrDbContext>(opt => opt.UseSqlServer(hrConn));

  // Service 注入多個 Context
  public class SignService
  {
      private readonly SignDbContext _signDb;   // 簽核相關表 (s99_*)
      private readonly HrDbContext _hrDb;       // 人事相關表 (s10_*, s90_*)
  }
```

**E. 資料遷移策略**

```
Sybase ASE 15.7 資料匯出工具：
  1. BCP (Bulk Copy Program) — Sybase 原生，速度最快
     bcp database..tablename out data.csv -c -t"," -S server -U user -P pass

  2. ASE → SQL Server Migration Assistant (SSMA)
     微軟官方工具，支援 Schema + Data 一次遷移

  3. 手動 DDL 轉換 + BCP 資料匯入
     適合需要重新設計 Schema 的場景（本專案建議此方式）

建議的遷移順序：
  Phase 0: 匯出 Sybase DDL + 資料 dictionary
  Phase 1: AI 協助轉換 DDL → EF Core Entity + Migration
  Phase 2: 建立新 DB Schema (Code-First Migration)
  Phase 3: BCP 匯出資料 → Bulk Insert 匯入新 DB
  Phase 4: 驗證資料完整性（row count + checksum）
```

#### 5.5.4 本專案涉及的 Sybase ASE 資料表命名空間

```
校務系統資料庫（推測的子系統分區）：

  s10_*  — 人事子系統（employee, otherpos）
           → 其他校務子系統也會用，是共用 reference data
           → 新系統可能是「唯讀」存取，不應修改

  s90_*  — 系統管理（unitb 組織、auth_log 稽核）
           → 權限和組織架構，跨子系統共用
           → 新系統需要同步或 API 介接

  s99_*  — 簽核子系統（sign_record, flow_spec, flow_setup...）
           → 本模組的核心表，新系統「完全擁有」
           → 可以重新設計 Schema

  命名規則：s{子系統編號}_{物件名稱}
  → 新系統建議改用有意義的名稱：
     s99_sign_record_mst  →  SignRecordMaster (或 ApprovalRequest)
     s99_flow_spec        →  WorkflowDefinition
     s10_employee         →  Employee (或從 HR API 取得)
```

### 5.6 後端 AI 輔助總覽圖

```
┌─────────────────────────────────────────────────────────────┐
│                    後端 AI 輔助流程                           │
│                                                             │
│  ┌───────────────┐     ┌───────────────┐                    │
│  │ DataWindow    │────→│ Entity Class  │                    │
│  │ (.srd 檔案)   │ AI  │ + DTO         │                    │
│  │ 含 SQL+欄位   │     │ + DbContext   │                    │
│  └───────────────┘     └───────────────┘                    │
│                                                             │
│  ┌───────────────┐     ┌───────────────┐                    │
│  │ PB method     │────→│ 商業規則文件   │──→ Service 骨架    │
│  │ 簽章+邏輯     │ AI  │ (條列式規則)   │ AI                │
│  └───────────────┘     └───────────────┘                    │
│                                                             │
│  ┌───────────────┐     ┌───────────────┐                    │
│  │ PB method     │────→│ Controller    │                    │
│  │ + JSP 對應    │ AI  │ + OpenAPI     │                    │
│  │              │     │ + Swagger     │                    │
│  └───────────────┘     └───────────────┘                    │
│                                                             │
│  ┌───────────────┐     ┌───────────────┐                    │
│  │ 商業規則文件   │────→│ Unit Test     │                    │
│  │ (上面產出的)   │ AI  │ 骨架+案例     │                    │
│  └───────────────┘     └───────────────┘                    │
│                                                             │
│  ⚠️  uf_sign / uf_back_step 等核心流程                      │
│     AI 產出骨架後必須由資深工程師人工審查！                     │
└─────────────────────────────────────────────────────────────┘
```

---

