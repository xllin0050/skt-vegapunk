## 3. AI 如何產出雛型介面

### 3.1 來源：PB 的 HTML 字串拼接 → SVG Wireframe

**核心觀念**：PB method（如 `of_sign_00`）中大量的 `ls_html += "<table>..."` 就是頁面佈局的規格書。

**Claude Code 指令範本**：

```
讀取這個 PB method 的原始碼。
此 method 用字串拼接產生 HTML 回傳給 JSP 顯示。
請分析 HTML 字串拼接的內容，提取：

1. 頁面結構（form, table, div 的層次關係）
2. 所有表單元素（input, select, checkbox, button）及其 id/name
3. 所有 JavaScript 函式呼叫（onclick, onchange 等事件）
4. 隱藏欄位中攜帶的狀態資料

然後產出一個 SVG Wireframe，需標註：
- 每個互動元素對應的新 API endpoint
- 例如：「審核送出」按鈕 → POST /api/sign/approve
```

**產出範例**（已在 `wireframe_sign_00.svg` 中展示）：

Wireframe 中的每個操作標註格式：

```
┌─────────────────────────────────────────────────┐
│ 舊操作: goDtl(seat) → form submit sign_dtl.jsp  │
│ 新操作: Vue Router → /sign/:signSerno            │
│ 呼叫 API: GET /api/sign/{signSerno}/detail       │
└─────────────────────────────────────────────────┘
```

### 3.2 JSP 頁面 → Vue 3 路由/元件 對應

| 舊 JSP 頁面 | Vue 3 路由 | Vue 3 元件 |
|-------------|-----------|-----------|
| sign_00.jsp | `/sign` | `SignDashboard.vue` |
| sign_content.jsp (AJAX) | (子元件) | `SignList.vue` |
| sign_dtl.jsp | `/sign/:serno` | `SignDetail.vue` |
| sign_select.jsp (popup) | (Modal) | `BranchSelectModal.vue` |
| sign_countersign_dtl.jsp | `/sign/:serno/countersign` | `CountersignHistory.vue` |
| sign_history_00.jsp | `/sign/history` | `SignHistory.vue` |
| createSign.jsp | (外部系統呼叫) | `CreateSign.vue` |

### 3.3 Dialog → Vue 3 元件對應

| 舊 jQuery UI Dialog | Vue 3 共用元件 |
|---------------------|--------------|
| `#child_window` (加簽人員挑選) | `CountersignPickerDialog.vue` |
| `#helpdesk_window` (登記桌指派) | `HelpdeskAssignDialog.vue` |
| 右半部搜尋面板 | `EmployeeSearchPanel.vue` |
| 左半部清單面板 | `CountersignListPanel.vue` |
| Y/N/R checkbox-as-radio | `ApprovalActionGroup.vue` |
| 代理人下拉 | `AgentSelector.vue` |

---

