## 8. AI 輔助的真實限制、資安考量與正確率評估

> **核心問題**：AI 輔助開發常被詬病三點：
> 1. 無法通盤了解系統，只能一小部分一小部分處理
> 2. 沒有考慮資安問題（OWASP Top 10、資通安全責任等級）
> 3. 產生的程式碼後續難以維護
>
> 以下基於**本專案實際程式碼的分析結果**，誠實回答這三個問題，
> 並說明「從 PB 舊程式碼轉換 + 前後端分層」為什麼能緩解這些問題。

### 8.1 痛點一：AI 無法通盤了解 — 用 PB 原始碼當「骨架」來緩解

#### 問題本質

AI 的 context window 有限（目前 Claude 約 200K tokens），一次無法讀取整個系統。
如果從零開始寫，AI 確實只能「盲人摸象」。

#### 為什麼「從 PB 轉換」比「從零開始」好

```
從零開始寫（AI 盲人摸象）          從 PB 轉換（AI 有藍圖）
─────────────────────            ─────────────────────
❌ AI 不知道有哪些功能              ✓ sign.pbt 列出所有 PBL
❌ AI 不知道頁面之間的關係           ✓ JSP 的 form action 定義了頁面流程
❌ AI 不知道資料表結構              ✓ DataWindow (.srd) 定義了 SQL + 欄位
❌ AI 不知道商業規則                ✓ PB method 包含完整的 if/else 邏輯
❌ AI 不知道權限控制                ✓ of_chkfncpermission() 定義了權限模型
❌ AI 猜測 API 需要什麼參數          ✓ PB method 簽章明確定義了輸入輸出
```

#### 本專案的具體策略：分批但有系統

```
Phase 1: 全域地圖（一次完成，不分批）
  ├── 讀取 sign.pbt → 取得 5 個 PBL 的完整清單
  ├── 掃描 18 個 JSP → 建立頁面流程圖
  ├── 掃描 36 個 .srd → 建立資料表關聯圖
  └── 掃描 14 個 PB method 簽章 → 建立 API 端點清單
  （以上 AI 可以在一個 session 內完成，因為只讀「目錄+簽章」，不讀內部實作）

Phase 2: 逐模組深入（分批但有全域地圖引導）
  ├── 模組 A: 待簽清單（sign_00 + sign_content + d_list + d_count）
  ├── 模組 B: 簽核明細（sign_dtl + sign_select + of_sign_dtl）
  ├── 模組 C: 會簽選人（sign_pick_api_1/2/3 + ds_pick_person）
  └── 模組 D: 簽核送出（sign_ins + uf_sign + uf_back_step）
  （每個模組 AI 都能對照 Phase 1 的全域地圖，知道自己在整體中的位置）
```

**關鍵差異**：AI 不是在「猜測」系統長什麼樣，而是在「翻譯」已經存在的系統。
翻譯可以分段做，但翻譯者手上有完整的原文。

### 8.2 痛點二：資安問題 — 舊系統的資安債 + 新系統的資安設計

#### 本專案舊系統實際發現的資安問題

從程式碼分析中，我們發現舊系統存在以下資安問題：

| OWASP Top 10 分類 | 舊系統現狀 | 嚴重度 | 程式碼證據 |
|---|---|---|---|
| **A03: 注入攻擊** | DataWindow 用參數化查詢 ✓，但 `setFilter()` 和 `find()` 用字串串接 ✗ | 🟡 中 | `uo_sign_record.sru` 的 `setFilter("sign_kind=" + string(sign_kind))` |
| **A03: 注入攻擊** | JSP 未做 input validation，只做 null 檢查 | 🟡 中 | `createSign.jsp` 的 `if (ls_flow_id == null) ls_flow_id = ""` |
| **A07: XSS** | PB 組裝 HTML 時未編碼使用者資料，JSP 用 `out.print()` 直接輸出 | 🔴 高 | `n_sign.sru` 的 `shtml += '{"ShowText":"' + of_chknull(...)` |
| **A07: XSS** | CORBA 回傳的 HTML 未經過 output encoding | 🔴 高 | `sign_00.jsp` 的 `out.print(ls_getrtn)` |
| **A01: 存取控制** | CORBA 連線使用寫死的 `jagadmin` + 空密碼 | 🔴 高 | 所有 JSP 的 `factory.create("jagadmin","")` |
| **A02: 加密機制** | Session 驗證僅比對字串 `"okey"` | 🟡 中 | `sign_00.jsp` 的 `if ("okey".equals(ls_online))` |
| **A05: 安全設定** | 無 CSRF Token、無 Content-Security-Policy | 🔴 高 | 全部 JSP 均未見 CSRF 防護 |
| **A09: 安全紀錄** | 有 `s90_auth_log` 表但僅部分使用，關鍵操作（簽核/退回）未記錄 | 🟡 中 | `n_sky_webbase.sru` 的 `of_log()` 只在權限檢查時呼叫 |
| **A04: 不安全設計** | 空的 catch block 吞掉例外 | 🟡 中 | `createSign.jsp` 的 `catch (CTS.PBUserException aException) {}` |

#### 為什麼「前後端分層」天然解決部分資安問題

```
舊架構的資安問題                     前後端分層後天然解決
──────────────────                  ──────────────────────

PB 組裝 HTML（XSS 風險）            → Vue 3 的 template 自動 HTML encode
                                     {{ userData }} 自動轉義，不需手動處理

JSP out.print() 直接輸出             → API 只回傳 JSON，不回傳 HTML
                                     XSS 攻擊面從後端完全消除

CORBA 寫死帳密                       → JWT / OAuth 2.0 標準認證
                                     每個請求帶 Bearer Token

Session 比對 "okey"                  → .NET Core Authentication Middleware
                                     標準的 Claims-based 認證

無 CSRF 防護                         → SPA + API 架構天然免疫 CSRF
                                     (API 用 Authorization header，非 cookie)

字串串接 SQL（部分）                  → EF Core 參數化查詢 + LINQ
                                     從架構層面杜絕 SQL Injection

空 catch block                       → .NET Core 全域 Exception Middleware
                                     + 結構化 Logging (Serilog)

無稽核紀錄                           → .NET Core Action Filter 自動記錄
                                     每個 API 呼叫自動寫 audit log
```

#### AI 在資安方面能做什麼、不能做什麼

| 資安工作 | AI 能力 | 建議做法 |
|---------|--------|---------|
| 產生有基本防護的程式碼骨架 | ★★★★ | 在 CLAUDE.md 中明確要求：「所有 API 必須加 `[Authorize]`、所有輸入必須加 `[Required]` `[MaxLength]`」 |
| 套用 OWASP Top 10 防護模式 | ★★★★ | Prompt 中直接引用 OWASP 指引，AI 會產出對應的 middleware 和 filter |
| 資通安全責任等級附表十（中級） | ★★★ | 提供附表十的檢核清單，AI 可逐項產出對應的實作方案 |
| 找出舊程式碼的資安漏洞 | ★★★★ | 如本次分析，AI 可以掃描並標記風險點 |
| **完整的滲透測試** | **★** | **AI 無法取代專業資安稽核，必須由人工執行** |
| **設計整體資安架構** | **★★** | **AI 可提供範本，但架構決策須由資安人員審核** |

#### 具體建議：在 CLAUDE.md 中加入資安規範

```markdown
## 資安規範（所有 AI 產出的程式碼必須遵守）

### 後端 (.NET Core)
- 所有 Controller action 必須加 [Authorize] 或明確標註 [AllowAnonymous]
- 所有 DTO 的 string 屬性必須加 [MaxLength] 和 [Required]（依 Schema）
- 所有資料庫操作必須使用 EF Core（參數化），禁止原生 SQL 字串串接
- 全域 Exception Handler 必須記錄完整 stack trace 但只回傳 generic 訊息
- 啟用 Rate Limiting middleware
- 啟用 CORS 白名單（僅允許前端 domain）
- 所有 API 回傳值禁止包含 HTML，只允許 JSON
- Audit Log：每個寫入操作記錄 userId + action + timestamp + IP

### 前端 (Vue 3)
- 禁止使用 v-html（除非內容來自可信來源且經過 DOMPurify 處理）
- 所有表單使用 Element Plus 的 el-form validation
- API Token 存放在 httpOnly cookie 或 memory，不放 localStorage
- 路由守衛檢查 JWT 過期時間
```

### 8.3 痛點三：產出程式碼難以維護 — 用「轉換」而非「生成」

#### 為什麼 AI「從零生成」的程式碼難維護

```
典型 AI 生成的問題：
1. 命名不一致（一會兒 camelCase、一會兒 snake_case）
2. 同一個功能在不同地方用不同寫法
3. 沒有統一的錯誤處理模式
4. 缺少註解或註解是 AI 的「解釋風格」而非工程風格
5. 沒有考慮到其他模組的存在
```

#### 為什麼「從 PB 轉換」能減輕此問題

```
PB 轉換的優勢：
1. 命名有來源 — PB 的 of_sign_00() → SignController.GetSignList()
   AI 不需要「發明」名字，而是「翻譯」名字

2. 結構有範本 — PB 的 DataWindow → Entity，PB 的 method → Service method
   AI 不需要「設計」架構，而是「對應」架構

3. 邏輯有依據 — PB 的 if/else → C# 的 if/else
   AI 不需要「猜測」商業規則，而是「搬運」商業規則

4. 欄位有定義 — DataWindow 的 column → DTO 的 property
   AI 不需要「創造」資料結構，而是「轉換」資料結構
```

#### 但仍需要「人工制定規範」讓 AI 遵守

AI 不會自動產生一致的程式碼。需要在 CLAUDE.md 中明確制定：

```markdown
## 程式碼規範

### 命名對照規則（PB → .NET Core）
| PB 命名 | C# 命名 | 範例 |
|---------|---------|------|
| of_sign_00() | GetSignList() | of_ 前綴去掉，改為 HTTP 動詞 |
| uf_sign() | ProcessApproval() | uf_ 前綴去掉，改為業務動詞 |
| ds_pick_person | PickPersonDto | ds_ 改為 Dto 後綴 |
| d_list | SignListEntity | d_ 改為業務名稱 + Entity 後綴 |
| ls_empid | empId | 去掉 ls_ 前綴，camelCase |
| sign_serno | SignSerno | PascalCase（Entity property）|

### 檔案結構規範
每個 Controller 對應一個 PB component：
  n_sign.sru → SignController.cs + SignService.cs + ISignService.cs
  uo_sign_record.sru → SignRecordService.cs + ISignRecordService.cs

每個 DataWindow 對應一個 DTO：
  d_list.srd → SignListDto.cs
  ds_pick_person.srd → PickPersonDto.cs

### 錯誤處理統一模式
所有 Service method 使用 Result<T> pattern：
  public async Task<Result<SignListDto>> GetSignListAsync(string empId)
  不允許 throw exception 作為正常流程控制

### 註解規範
- Entity property: /// <summary>中文說明（來自 Schema COMMENT 或 DataWindow）</summary>
- Service method: /// <summary>對應 PB: n_sign.of_sign_00()</summary>
- Controller action: /// <summary>對應 JSP: sign_00.jsp</summary>
  加上 [ProducesResponseType] 標註
```

### 8.4 PB 轉換正確率誠實評估

#### 依轉換類型分層評估

```
轉換類型                              正確率    信心度   說明
────────────────────────────────────  ──────   ──────  ─────────────────────

【機械式轉換 — 幾乎不需人工】

DataWindow (.srd) → C# Entity/DTO     95%     ★★★★★  欄位名稱、型別直接對應
                                                       失敗原因：UTF-16 編碼偶爾亂碼

DataWindow (.srd) → TypeScript         95%     ★★★★★  同上，型別對應明確
  interface                                             char→string, long→number

PB method 簽章 → API endpoint 定義     95%     ★★★★★  of_sign_00(a,b,c)
                                                       → GET /api/sign?a=&b=&c=

JSP 頁面結構 → Vue Router 路由表        90%     ★★★★★  sign_00.jsp → /sign
                                                       sign_dtl.jsp → /sign/:id

OpenAPI YAML → TS API Client           98%     ★★★★★  工具自動產生 (openapi-ts)
                                                       幾乎零錯誤

【邏輯式轉換 — 需要人工審查】

PB 簡單 CRUD 邏輯 → C# Service         85%     ★★★★   INSERT/UPDATE/DELETE 直接對應
                                                       需檢查：交易邊界、並行控制

JSP JavaScript → Vue Composable        80%     ★★★★   jQuery → Composition API
                                                       需檢查：事件綁定、DOM 操作

PB HTML 拼接 → SVG Wireframe           85%     ★★★★   表格結構、按鈕位置正確
                                                       需調整：CSS 樣式、RWD 佈局

PB 字串分隔符解析                       80%     ★★★    §, (#), ($), @a@ 等分隔符
→ JSON 序列化/反序列化                                  需檢查：巢狀結構、邊界案例

DataWindow SQL (簡單) → EF Core LINQ    80%     ★★★★   單表 + WHERE 條件
                                                       直接對應 .Where().Select()

【高風險轉換 — 必須人工深度審查】

PB 核心簽核流程 (uf_sign)               60%     ★★     多表交易 + 狀態機 + 分支邏輯
→ C# Service                                           AI 能產出骨架，邏輯需逐行驗證

PB 退回/分支流程 (uf_back_step)         55%     ★★     遞迴查詢 + 複合條件
→ C# Service                                           需要完整的測試案例驗證

DataWindow SQL (複雜 UNION)             65%     ★★★    d_list.srd 的 UNION + 7 種 user_type
→ EF Core LINQ                                         建議保留原生 SQL 或用 Dapper

PB setFilter() 動態過濾                 50%     ★★     字串串接的動態條件
→ LINQ Expression                                      需要重新設計，不只是翻譯

民國年日期邏輯                          70%     ★★★    轉換規則簡單 (year-1911)
                                                       但散佈在各處，容易遺漏

Trigger / SP 邏輯遷移                   50%     ★★     完全看不到程式碼（需 Schema）
                                                       AI 可翻譯但業務正確性需人工確認
```

#### 綜合正確率

```
加權平均正確率（以本專案的程式碼比例估算）：

  機械式轉換 (佔工作量 40%) × 95% = 38%
  邏輯式轉換 (佔工作量 35%) × 80% = 28%
  高風險轉換 (佔工作量 25%) × 60% = 15%
  ─────────────────────────────────
  整體加權正確率 ≈ 81%

  意義：AI 可以完成約 80% 的「初版」程式碼，
       剩下 20% 需要人工審查修正。
       但這 80% 省下的是最費時的「打字工作」，
       而人工專注在最有價值的「判斷工作」。
```

### 8.5 實際建議：給專案負責人的行動方案

#### 建議一：建立「轉換 → 審查 → 測試」三段流程

```
┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│  AI 轉換      │────→│  人工審查     │────→│  自動測試     │
│  (Claude Code)│     │  (資深工程師)  │     │  (CI/CD)      │
│               │     │              │     │              │
│ ✓ Entity/DTO  │     │ ✓ 商業邏輯    │     │ ✓ 單元測試    │
│ ✓ Controller  │     │ ✓ 資安檢查    │     │ ✓ 整合測試    │
│ ✓ 基本 Service│     │ ✓ 效能考量    │     │ ✓ 資安掃描    │
│ ✓ Vue 元件    │     │ ✓ 命名一致性  │     │ ✓ OWASP ZAP  │
│ ✓ 前端驗證    │     │              │     │              │
│  ~2 天/模組   │     │  ~1 天/模組   │     │  自動化持續    │
└──────────────┘     └──────────────┘     └──────────────┘
```

#### 建議二：資安不靠 AI，靠架構和工具

| 資安需求 | 不要靠 AI | 要靠這個 |
|---------|----------|---------|
| SQL Injection 防護 | ~~AI 記得用參數化~~ | EF Core 強制參數化（架構層面） |
| XSS 防護 | ~~AI 記得 encode~~ | Vue 3 template 自動 encode（框架層面） |
| CSRF 防護 | ~~AI 記得加 token~~ | SPA + Bearer Token 架構天然免疫 |
| 認證授權 | ~~AI 記得加檢查~~ | .NET Core `[Authorize]` + JWT middleware |
| 資安掃描 | ~~AI 自我檢查~~ | CI/CD 整合 OWASP ZAP / SonarQube |
| 附表十合規 | ~~AI 讀過法規~~ | 專案初期建立 checklist，每個 PR 檢核 |

**核心原則**：**資安寫在架構裡，不寫在 AI 的 prompt 裡。**
框架和工具強制執行，不依賴 AI 的「記憶力」。

#### 建議三：用「對照表」確保可維護性

每個轉換後的新程式碼檔案，開頭加上：

```csharp
/// <summary>
/// 簽核清單 API
///
/// 【遷移對照】
/// 原始 JSP:  sign_00.jsp
/// 原始 PB:   n_sign.of_sign_00()
/// 原始 DW:   d_list.srd, d_count.srd
/// 轉換日期:  2025-xx-xx
/// 轉換方式:  AI (Claude Code) 產出初版 + 人工審查
/// 審查人員:  _______________
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SignController : ControllerBase
{
    // ...
}
```

這樣做的好處：
- 未來維護者可以回頭查舊程式碼理解商業邏輯
- 可以追蹤哪些是 AI 產出的、哪些是人工修改的
- 稽核時可以證明每段程式碼都經過人工審查

#### 建議四：分階段導入，先做低風險模組

```
Phase 1（第 1-2 個月）— 驗證 AI 轉換流程
  └── 選 1 個低風險模組（例如：會簽選人 picker）
      ├── AI 轉換 → 人工審查 → 測試 → 上線
      ├── 計算實際正確率和工時
      └── 調整 CLAUDE.md 中的規範

Phase 2（第 3-4 個月）— 批次轉換中風險模組
  └── 待簽清單、簽核明細、歷史查詢
      ├── 套用 Phase 1 調整後的流程
      └── 建立自動化測試套件

Phase 3（第 5-6 個月）— 轉換核心簽核流程
  └── uf_sign, uf_back_step, uf_create_sign
      ├── AI 產出骨架，人工逐行審查
      ├── 完整的整合測試 + 資安掃描
      └── 與舊系統平行運行驗證

Phase 4（第 7 個月）— 資安稽核 + 上線
  └── OWASP ZAP 掃描 + 附表十 checklist + 滲透測試
```

### 8.6 回答核心問題：用 PB 轉換 + 前後端分層是否減少三大痛點？

| 痛點 | 從零開始 | PB 轉換 + 分層 | 改善幅度 |
|------|---------|---------------|---------|
| **1. AI 無法通盤了解** | 嚴重 — AI 在猜測系統該長什麼樣 | 大幅緩解 — AI 在翻譯已存在的系統，有 PBL 清單、JSP 流程圖、DataWindow 欄位定義當藍圖 | 🟢 70% 改善 |
| **2. 未考慮資安** | 嚴重 — AI 經常忘記加防護 | 部分緩解 — 前後端分層天然解決 XSS/CSRF，EF Core 天然防 SQL Injection，**但仍需人工建立資安架構** | 🟡 50% 改善 |
| **3. 程式碼難維護** | 嚴重 — 每次 AI 產出風格不同 | 大幅緩解 — PB 提供了命名來源和結構範本，加上 CLAUDE.md 規範可強制一致性，**但核心邏輯的可讀性仍需人工潤色** | 🟢 60% 改善 |

**最終結論**：

> 「從 PB 轉換」不是萬靈丹，但它把 AI 從「創作者」變成「翻譯者」。
> 翻譯的品質遠高於創作，因為有原文可以對照、有結構可以依循。
> 資安和維護性則需要靠**架構設計、框架選擇、CI/CD 工具**來保障，
> 不能依賴 AI 的「自覺」。
