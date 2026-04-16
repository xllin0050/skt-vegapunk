## 2. AI 如何產出程式規格

### 2.1 方法一：餵 DataWindow (.srd) → 產出 API Schema

**原理**：每個 `.srd` 檔案包含完整的 SQL 查詢與欄位定義，這是資料契約的最佳來源。

**Claude Code 指令範本**：

```
讀取這個 DataWindow .srd 檔案，它是 UTF-16 LE 編碼，字元間有空格。
請提取：
1. 所有 column 定義（name, type, maxlength）
2. retrieve= 中的 SQL 查詢（去除多餘空格還原為正常SQL）
3. arguments= 中的查詢參數
4. 涉及的資料表和 JOIN 關係

然後產出：
A) 一個 C# DTO class
B) 一個 OpenAPI 3.0 的 Schema 定義 (YAML)
C) 一個 TypeScript interface（給前端用）
```

**實際範例 — 以 `d_list.srd` 為例**：

AI 讀取後可提取出：

```
【提取結果】
SQL: SELECT s99_sign_record_mst.sign_serno, s99_flow_spec.flow_name, ...
     FROM s99_sign_record
     INNER JOIN s99_sign_record_mst ON ...
     INNER JOIN s99_flow_spec ON ...
     INNER JOIN s99_flow_setup ON ...
     WHERE s99_sign_record.emp_id = :as_emp_id
     AND (s99_flow_spec.sign_kind = :ai_signkind OR :ai_signkind = -1)
     UNION
     SELECT ... FROM s99_sign_record_branch ...

參數: as_emp_id (string), ai_signkind (number)

欄位:
  sign_serno       char(13)   -- 簽核序號
  flow_name        char(40)   -- 流程名稱
  flow_id          char(8)    -- 流程代碼
  ver_id           long       -- 版本號
  sign_status      char(1)    -- 簽核狀態
  vou_id           char(20)   -- 公文編號
  vou_subject      char(60)   -- 公文主旨
  remark           char(100)  -- 備註
  sign_max_cntno   long       -- 最大簽核次數
  pre_step_ordid   long       -- 前一關卡序號
  step_ordid       long       -- 當前關卡序號
  branch_id        long       -- 分支代碼
  branch_step_ordid long      -- 分支關卡序號
  ser_no           long       -- 序號
  step_name        char(40)   -- 關卡名稱
```

**AI 產出的 DTO**：

```csharp
public class PendingSignItemDto
{
    public string SignSerno { get; set; }       // char(13) 簽核序號
    public string FlowName { get; set; }        // char(40) 流程名稱
    public string FlowId { get; set; }          // char(8)  流程代碼
    public int VerId { get; set; }              // 版本號
    public string SignStatus { get; set; }      // char(1)  簽核狀態
    public string VouId { get; set; }           // char(20) 公文編號
    public string VouSubject { get; set; }      // char(60) 公文主旨
    public string Remark { get; set; }          // char(100)
    public int SignMaxCntno { get; set; }       // 最大簽核次數
    public int StepOrdid { get; set; }          // 當前關卡序號
    public int BranchId { get; set; }           // 分支代碼
    public string StepName { get; set; }        // char(40) 關卡名稱
}
```

**AI 產出的 TypeScript interface**：

```typescript
export interface PendingSignItem {
  signSerno: string
  flowName: string
  flowId: string
  verId: number
  signStatus: string
  vouId: string
  vouSubject: string
  remark: string
  signMaxCntno: number
  stepOrdid: number
  branchId: number
  stepName: string
}
```

### 2.2 方法二：餵 PB method → 產出 API Endpoint 規格

**原理**：每個 PB public method 對應一個 API endpoint。AI 分析方法簽章+JSP 呼叫方式，就能產出完整的 REST API 定義。

**Claude Code 指令範本**：

```
分析這個 PowerBuilder method 和對應的 JSP 呼叫方式。
PB method 簽章和 JSP 中的呼叫程式碼我會一起提供。

請產出：
1. REST API endpoint 定義（HTTP method, URL, 參數來源）
2. Request 參數規格（哪些從 URL path, 哪些從 query string, 哪些從 body）
3. Response 結構（根據 PB return 的內容判斷）
4. OpenAPI YAML 片段
```

**實際對照表 — 全部 PB method → REST API**：

| # | PB Method | JSP 來源 | REST API | 說明 |
|---|-----------|---------|----------|------|
| 1 | `of_sign_00(pblpath, year, sms, loginid, agent, sign_kind, card_type)` | sign_00.jsp | `GET /api/sign/dashboard` | 主頁面：頁籤+代理人+初始清單 |
| 2 | `of_sign_content(pblpath, year, sms, loginid, agent, signKind, card_type)` | sign_content.jsp | `GET /api/sign/list?signKind={id}` | 分類頁籤內容 |
| 3 | `of_sign_dtl(pblpath, year, sms, loginid, agent, svalue, card_type)` | sign_dtl.jsp | `GET /api/sign/{signSerno}/detail` | 案件明細+PDF |
| 4 | `of_sign_ins(pblpath, year, sms, loginid, agent, value, card_type)` | sign_ins.jsp | `POST /api/sign/approve` | 審核送出（批次/單筆） |
| 5 | `of_sign_doc(serno, seq)` | sign_doc.jsp | `GET /api/sign/{serno}/document/{seq}` | 附件下載 |
| 6 | `of_sign_pick_api_1(pblpath, ...)` | sign_pick_api_1.jsp | `GET /api/units` | 單位下拉選單 (JSON) |
| 7 | `of_sign_pick_api_2(pblpath, year, sms, choice, value)` | sign_pick_api_2.jsp | `GET /api/employees/search?type={1|2|3}&q={val}` | 搜尋人員 |
| 8 | `of_sign_pick_api_3(pblpath, year, sms, loginid, agent, value)` | sign_pick_api_3.jsp | `GET /api/sign/{serno}/countersigners` | 目前加簽清單 |
| 9 | `of_sign_pick_api_ins(...)` | sign_pick_api_ins.jsp | `POST /api/sign/{serno}/countersigners` | 儲存加簽人員 |
| 10 | `of_sign_pick_api_ins_hd(...)` | sign_pick_api_ins_hd.jsp | `POST /api/sign/{serno}/helpdesk` | 登記桌指派 |
| 11 | `of_sign_countersign_dtl(pblpath, year, sms, loginid, serno, stepkey)` | sign_countersign_dtl.jsp | `GET /api/sign/{serno}/countersign-history` | 會簽歷程 |
| 12 | `of_sign_select(pblpath, year, sms, loginid, agent, value)` | sign_select.jsp | `GET /api/sign/{serno}/branches` | 分支選項 |
| 13 | `of_sign_select_ins(pblpath, year, sms, loginid, value)` | sign_select_ins.jsp | `POST /api/sign/{serno}/select-branch` | 儲存分支選擇 |
| 14 | `uf_create_sign(flow_id, vou_id, ...)` | createSign.jsp | `POST /api/sign/create` | 建立新簽核 |

**注意**：舊系統中 `pblpath`, `year`, `sms` 這三個參數在新架構中不需要了：
- `pblpath`：DataWindow 路徑，改用 EF Core 直接查 DB
- `year`, `sms`：改從 JWT token 或系統設定取得
- `loginid`：改從 JWT token 取得

### 2.3 方法三：餵 PB 商業邏輯 → 產出商業規則文件

**Claude Code 指令範本**：

```
分析這段 PowerBuilder 程式碼中的商業邏輯。
不要翻譯成程式碼，而是用條列式中文描述出所有商業規則。
格式：
- 規則編號
- 規則描述
- 觸發條件
- 處理動作
- 影響的資料表/欄位
```

**範例 — 從 `goNext()` JS 函式提取的驗證規則**：

```
【規則 VR-001】至少勾選一筆
  條件：使用者按下「審核送出」
  規則：必須至少勾選一筆案件
  違反時：alert("您未選擇要批次核決的項目，請勾選。")

【規則 VR-002】不同意需確認
  條件：勾選的案件中有 value 以 "N" 開頭的項目
  規則：顯示確認對話框
  違反時：confirm("您所審核的項目中，有勾選『不同意』的項目，確定要不同意嗎？")

【規則 VR-003】退回需確認
  條件：勾選的案件中有 value 以 "R" 開頭的項目
  規則：顯示確認對話框

【規則 VR-004】不同意/退回必填原因
  條件：chkN 或 chkR 被勾選
  規則：對應的 note 欄位不可為空
  違反時：alert("第X筆 請輸入原因說明！")

【規則 VR-005】防重複送出
  條件：按下審核送出
  規則：立即 disable 所有 btnc_next 按鈕
  恢復：驗證失敗時 UnLock
```

---

