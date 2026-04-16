## 4. 前端 AI 能協助什麼

### 4.1 從 Wireframe → Vue 3 元件骨架

**Claude Code 指令**：

```
根據這個 SVG Wireframe 和 API 規格，
產生一個 Vue 3 Composition API + TypeScript 的元件。
使用 Element Plus 作為 UI 元件庫。
```

**AI 可以直接產出的東西**：
- `<template>` 結構（從 SVG 佈局轉換）
- `<script setup>` 中的 reactive state
- API 呼叫的 composable 函式
- 基本的 CSS/SCSS 排版

### 4.2 從 JSP 中的 JavaScript → Vue 3 Composable

**這是 AI 最擅長的轉換之一**。以 `sign_00.jsp` 中的函式為例：

| 舊 JS 函式 | AI 產出的 Vue 3 對應 |
|-----------|-------------------|
| `goNext()` (line 533-597) — 收集勾選項目、驗證、組裝分隔字串、submit | → `useApproval()` composable — reactive 表單狀態、computed 驗證、axios POST JSON |
| `setDisabled(obj)` (line 625-647) — checkbox 互斥模擬 radio | → Element Plus `<el-radio-group>` 直接取代，不需寫邏輯 |
| `selAll(obj)` (line 650-663) — 全選/取消全選 | → `<el-table>` 的 `@selection-change` 事件，內建支援 |
| `pickPersonOpen()` / `find_person()` / `save_pickPerson()` (line 283-436) — 加簽 Dialog 的開啟、搜尋、存檔 | → `useCountersignPicker()` composable — dialog 狀態管理、搜尋防抖、樂觀更新 |
| `go_reload()` (line 671-706) — AJAX 重新載入 | → Pinia store 的 `fetchList()` action，元件 watch signKind 自動重取 |
| `go_card_type()` (line 708-730) — 讀取 radio 值 | → `v-model` 綁定到 `ref<string>`，零程式碼 |
| `go_ass_doc()` (line 826-902) — XMLHttpRequest 下載附件 | → `useDocumentDownload()` composable — axios + blob 處理 |

**Claude Code 指令範本（驗證邏輯遷移）**：

```
以下是舊系統 sign_00.jsp 中的 goNext() JavaScript 函式。
它做了以下事情：
1. 遍歷 form 中所有 id 以 "chk" 開頭的 checkbox
2. 收集被勾選的項目，用 "§" 分隔欄位，"(#)" 分隔每筆
3. 驗證至少勾選一筆
4. 不同意(N)/退回(R) 需要 confirm 確認
5. N 和 R 的項目必須填寫 note 原因
6. 防止重複送出（鎖按鈕）

請轉換為 Vue 3 Composition API 的 composable。
要求：
- 使用 TypeScript
- 輸入改為結構化 JSON，不要用分隔字串
- 驗證用 reactive 判斷，不用 alert
- 確認對話框用 ElMessageBox.confirm
- 送出用 axios POST /api/sign/approve
```

### 4.3 從 OpenAPI 規格 → 自動產生 API Client

```bash
# 工具鏈：openapi-typescript-codegen 或 orval
npx openapi-typescript-codegen --input ./openapi.yaml --output ./src/api/generated --client axios
```

**AI 也可以直接從 OpenAPI YAML 產出 Pinia Store**：

```
根據這個 OpenAPI 規格中 /api/sign 相關的 endpoints，
產出一個 Pinia store (useSignStore)，包含：
- State: 對應每個 GET endpoint 的回傳資料
- Actions: 對應每個 endpoint 的呼叫方法
- Getters: 常用的衍生狀態（如 pendingCount）
```

### 4.4 前端 AI 輔助總覽圖

```
┌─────────────────────────────────────────────────────────────┐
│                    前端 AI 輔助流程                           │
│                                                             │
│  ┌───────────────┐     ┌───────────────┐                    │
│  │ PB HTML 拼接   │────→│ SVG Wireframe │──→ Vue 元件骨架    │
│  │ (of_sign_00)  │ AI  │ (含API標註)    │ AI                │
│  └───────────────┘     └───────────────┘                    │
│                                                             │
│  ┌───────────────┐     ┌───────────────┐                    │
│  │ JSP JavaScript│────→│ 驗證規則文件   │──→ Vue Composable  │
│  │ (goNext 等)   │ AI  │ (VR-001~005)  │ AI                │
│  └───────────────┘     └───────────────┘                    │
│                                                             │
│  ┌───────────────┐     ┌───────────────┐                    │
│  │ OpenAPI YAML  │────→│ TS Interface  │──→ Pinia Store     │
│  │ (共同契約)     │ AI  │ + API Client  │ AI                │
│  └───────────────┘     └───────────────┘                    │
│                                                             │
│  ┌───────────────┐                                          │
│  │ jQuery UI     │────→ Element Plus Dialog/Table/Tabs       │
│  │ Dialog 結構   │ AI  （元件庫對應建議）                      │
│  └───────────────┘                                          │
└─────────────────────────────────────────────────────────────┘
```

---

