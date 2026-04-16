## 6. 前後端協作工作流程

### 6.1 協作核心：OpenAPI YAML 就是合約

```
┌──────────┐                              ┌──────────┐
│  前端團隊  │                              │  後端團隊  │
│          │      ┌──────────────┐         │          │
│          │◄────→│ openapi.yaml │◄────────→│          │
│          │      │ (共同維護)    │          │          │
│          │      └──────────────┘          │          │
│          │              │                 │          │
│          │       ┌──────┴──────┐          │          │
│          │       │             │          │          │
│     自動產出      │        自動產出         │          │
│  TS Interface    │     Controller Stub    │          │
│  API Client      │     DTO Classes        │          │
│  Mock Data       │     Swagger UI         │          │
└──────────┘       │                        └──────────┘
                   │
              Git 版控
```

### 6.2 階段性工作流程

```
Phase 0: 分析 (Week 1-2)
├── AI 掃描所有 .srd → 產出 Entity/DTO 初稿
├── AI 掃描所有 PB method → 產出 API endpoint 對照表
├── AI 掃描所有 JSP → 產出 Wireframe SVG
├── 共同產出: openapi.yaml v1.0
└── 產出: 商業規則文件（AI 提取 + 人工審查）

Phase 1: 平行開發 - 讀取 (Week 3-5)
├── 前端: Mock Server 跑起來 → 開發 Dashboard + List + Detail 頁面
├── 後端: 實作 GET endpoints → 驗證資料正確性
└── 每週: 比對 OpenAPI 契約一致性

Phase 2: 平行開發 - 寫入 (Week 6-8)
├── 前端: 實作審核送出表單 + 驗證邏輯
├── 後端: 實作 POST endpoints (approve, countersign, helpdesk)
├── 後端: 核心 uf_sign 邏輯移植（需資深審查）
└── 每週: 契約版本同步

Phase 3: 整合測試 (Week 9-10)
├── 前端接上真實後端
├── E2E 測試完整簽核流程
└── 修正契約不一致處

Phase 4: 收尾 (Week 11-12)
├── 簽核歷史、代理人、邊界案例
├── 效能調校（d_list UNION 查詢最佳化）
└── 民國年轉換測試
```

### 6.3 前端使用 Mock Server 的方式

```bash
# 方案一：使用 Prism
npx @stoplight/prism-cli mock openapi.yaml --port 4010

# 方案二：請 AI 從 OpenAPI + DataWindow 欄位產出 mock data
# Claude Code 指令：
# "根據這個 OpenAPI 規格和 DataWindow 欄位定義，
#  產出符合欄位型別和長度的 mock JSON 資料。
#  使用中文姓名和政府公文格式的編號。"
```

### 6.4 共用儲存庫結構 — 完整說明

#### 6.4.1 總覽圖

```
sign-system/
│
├── api-spec/                  ★ 前後端的「合約中心」
│   ├── openapi.yaml           ← 唯一的 API 真相來源 (Single Source of Truth)
│   └── mock-data/             ← AI 產出的假資料，供前端離線開發
│       ├── sign-list.json
│       ├── sign-detail.json
│       ├── employees.json
│       └── units.json
│
├── frontend/                  ★ 前端團隊的工作區
│   ├── package.json
│   ├── vite.config.ts
│   ├── tsconfig.json
│   └── src/
│       ├── api/generated/     ← 工具從 openapi.yaml 自動產出（不手改）
│       │   ├── models/        ← TypeScript interface（對應後端 DTO）
│       │   └── services/      ← Axios 呼叫方法（對應每個 API endpoint）
│       ├── components/sign/   ← 簽核模組的 UI 元件
│       │   ├── SignList.vue
│       │   ├── ApprovalActionGroup.vue
│       │   ├── CountersignPickerDialog.vue
│       │   ├── HelpdeskAssignDialog.vue
│       │   ├── EmployeeSearchPanel.vue
│       │   └── AgentSelector.vue
│       ├── composables/       ← Vue 3 Composition API 的共用邏輯
│       │   ├── useApproval.ts
│       │   ├── useCountersignPicker.ts
│       │   └── useDocumentDownload.ts
│       ├── stores/            ← Pinia 狀態管理
│       │   ├── useSignStore.ts
│       │   ├── useSignDetailStore.ts
│       │   └── useAuthStore.ts
│       ├── views/             ← 路由級頁面元件
│       │   ├── SignDashboard.vue
│       │   ├── SignDetail.vue
│       │   └── SignHistory.vue
│       └── router/
│           └── index.ts
│
├── backend/                   ★ 後端團隊的工作區
│   ├── SignSystem.sln
│   └── src/
│       ├── SignSystem.Api/
│       │   ├── Controllers/       ← API 進入點（薄層，只做參數轉發）
│       │   │   ├── SignController.cs
│       │   │   ├── EmployeeController.cs
│       │   │   └── DocumentController.cs
│       │   ├── Program.cs
│       │   └── appsettings.json
│       ├── SignSystem.Application/
│       │   ├── Services/          ← 商業邏輯層（PB method 的靈魂在此）
│       │   │   ├── ISignService.cs
│       │   │   ├── SignService.cs
│       │   │   ├── ISignQueryService.cs
│       │   │   └── SignQueryService.cs
│       │   └── DTOs/              ← 資料傳輸物件（AI 從 .srd 產出）
│       │       ├── Request/
│       │       │   ├── ApproveRequestDto.cs
│       │       │   └── CountersignRequestDto.cs
│       │       └── Response/
│       │           ├── PendingSignItemDto.cs
│       │           ├── SignDetailDto.cs
│       │           └── EmployeeSearchResultDto.cs
│       ├── SignSystem.Domain/
│       │   └── Entities/          ← 資料庫實體（AI 從 .srd 產出）
│       │       ├── SignRecordMst.cs
│       │       ├── SignRecord.cs
│       │       ├── FlowSpec.cs
│       │       ├── FlowSetup.cs
│       │       ├── Employee.cs
│       │       └── Unit.cs
│       └── SignSystem.Infrastructure/
│           ├── Data/
│           │   └── SignDbContext.cs
│           └── Repositories/      ← 資料存取層（DataWindow SQL 轉 LINQ）
│               ├── ISignRecordRepository.cs
│               └── SignRecordRepository.cs
│
├── docs/                      ★ AI 產出的分析文件（人類閱讀用）
│   ├── wireframes/            ← SVG 雛型介面（從 PB HTML 拼接分析而來）
│   │   ├── sign_00_dashboard.svg
│   │   ├── sign_dtl_detail.svg
│   │   └── sign_history.svg
│   ├── business-rules/        ← 商業規則文件（從 PB 邏輯提取而來）
│   │   ├── VR-approval-rules.md
│   │   ├── VR-countersign-rules.md
│   │   └── VR-flow-transition.md
│   └── pb-analysis/           ← PB 原始碼分析報告
│       ├── n_sign_method_catalog.md
│       ├── uo_sign_record_analysis.md
│       ├── datawindow_catalog.md
│       └── api_endpoint_mapping.md
│
├── legacy-source/             ★ 舊系統原始碼（唯讀，不修改）
│   └── sign/                  ← 就是 D:\skyuni\sign 本目錄
│       ├── *.jsp              ← 18 個 JSP 頁面
│       ├── sign/              ← n_sign.sru 等 PB 元件
│       ├── dw_sign/           ← DataWindow .srd 檔案
│       ├── tpec_s61/          ← uo_sign_record.sru 工作流程引擎
│       ├── sky_webbase/       ← 共用工具庫
│       └── webap/             ← Web 應用框架
│
└── CLAUDE.md                  ★ Claude Code 的「專案記憶」
```

---

#### 6.4.2 每個目錄的詳細用途

##### (A) `api-spec/` — 前後端的合約中心

```
api-spec/
├── openapi.yaml
└── mock-data/
    ├── sign-list.json
    ├── sign-detail.json
    ├── employees.json
    └── units.json
```

**用途**：這是整個專案最重要的目錄。`openapi.yaml` 定義了前後端之間**所有溝通的格式**——每個 API 的 URL、HTTP method、request/response 的 JSON 結構。

**誰維護**：兩個團隊共同維護。任何 API 變更都必須先改這裡，再各自更新程式碼。

**產出來源**：
```
legacy-source/sign/sign/n_sign.sru     (PB method 簽章)
legacy-source/sign/dw_sign/*.srd       (DataWindow 欄位定義)
legacy-source/sign/*.jsp               (JSP 傳遞的參數)
         │
         │  AI 分析提取
         ▼
    api-spec/openapi.yaml
```

**被誰使用**：
```
api-spec/openapi.yaml
    │
    ├──→ frontend/src/api/generated/    (自動產出 TS 型別 + API client)
    ├──→ backend/Controllers/           (Controller 骨架依此實作)
    ├──→ backend/DTOs/                  (DTO 結構必須與此一致)
    └──→ api-spec/mock-data/            (AI 依據 schema 產出假資料)
```

**`mock-data/`**：AI 根據 openapi.yaml 中的 schema 和 DataWindow 的欄位長度，產出仿真的測試資料。前端團隊在後端尚未完成時，可以用 Mock Server（如 Prism）載入這些 JSON 開發。

---

##### (B) `frontend/` — 前端團隊工作區

```
frontend/src/
├── api/generated/     ← 🤖 自動產生，不手改
├── components/sign/   ← 👨‍💻 前端團隊開發
├── composables/       ← 👨‍💻 前端團隊開發（AI 輔助）
├── stores/            ← 👨‍💻 前端團隊開發（AI 輔助）
├── views/             ← 👨‍💻 前端團隊開發
└── router/            ← 👨‍💻 前端團隊開發
```

**各子目錄說明**：

| 子目錄 | 用途 | 產出來源 | 關係 |
|--------|------|---------|------|
| `api/generated/` | TypeScript interface + Axios 呼叫方法 | 工具從 `openapi.yaml` 自動產出 | **不要手改**，改 openapi.yaml 後重新產出 |
| `components/sign/` | 簽核模組的可重用 UI 元件 | AI 從 `docs/wireframes/` SVG 產出骨架 → 前端團隊調整 | 每個 SVG 中的區塊對應一個 component |
| `composables/` | 跨元件共用的邏輯（驗證、搜尋、下載） | AI 從 `legacy-source/sign/*.jsp` 中的 JS 函式翻譯 | `goNext()` → `useApproval.ts` |
| `stores/` | Pinia 集中狀態管理 | AI 從 `openapi.yaml` 的 endpoint 結構產出 | 每個 GET endpoint 對應一個 state + action |
| `views/` | 路由級別的頁面（對應舊的 JSP 頁面） | AI 從 `docs/wireframes/` 產出頁面骨架 | `sign_00.jsp` → `SignDashboard.vue` |

**資料流向**：
```
api-spec/openapi.yaml
    │ 自動產出
    ▼
frontend/src/api/generated/models/PendingSignItem.ts     ← 型別定義
frontend/src/api/generated/services/SignService.ts        ← API 呼叫方法
    │ 被引用
    ▼
frontend/src/stores/useSignStore.ts                       ← 狀態管理
    │ 被引用
    ▼
frontend/src/views/SignDashboard.vue                      ← 頁面元件
    │ 使用
    ▼
frontend/src/components/sign/SignList.vue                  ← UI 子元件
frontend/src/composables/useApproval.ts                   ← 共用邏輯
```

---

##### (C) `backend/` — 後端團隊工作區

```
backend/src/
├── SignSystem.Api/            ← 最外層：HTTP 進出口
├── SignSystem.Application/    ← 中間層：商業邏輯
├── SignSystem.Domain/         ← 核心層：領域實體
└── SignSystem.Infrastructure/ ← 底層：資料庫存取
```

**四層架構說明**：

| 層級 | 目錄 | 對應的舊系統元件 | 責任 |
|------|------|---------------|------|
| **API 層** | `SignSystem.Api/Controllers/` | JSP 頁面（接收 request、回傳 response） | 參數驗證、呼叫 Service、回傳 JSON |
| **應用層** | `SignSystem.Application/Services/` | PB method 的商業邏輯（`of_sign_ins`, `uf_sign`） | 商業規則、流程控制、交易管理 |
| **應用層** | `SignSystem.Application/DTOs/` | DataWindow 查詢結果的形狀 | Request/Response 的資料結構定義 |
| **領域層** | `SignSystem.Domain/Entities/` | 資料庫 table 的實體對應 | 一個 Entity class 對應一張實體表 |
| **基礎層** | `SignSystem.Infrastructure/Repositories/` | DataWindow 的 retrieve SQL | 資料庫查詢（LINQ / FromSqlRaw） |
| **基礎層** | `SignSystem.Infrastructure/Data/` | PB 的 Transaction 物件 | EF Core DbContext 設定 |

**產出來源**：
```
legacy-source/sign/dw_sign/*.srd
    │ AI 提取 column 定義
    ▼
backend/Entities/SignRecordMst.cs        ← 實體資料表 → Entity
backend/DTOs/PendingSignItemDto.cs       ← 查詢結果形狀 → DTO

legacy-source/sign/sign/n_sign.sru
    │ AI 分析 method 簽章
    ▼
backend/Controllers/SignController.cs     ← PB method → Controller action

legacy-source/sign/tpec_s61/uo_sign_record.sru
    │ AI 提取商業規則 + 產出骨架
    ▼
backend/Services/SignService.cs           ← PB 邏輯 → Service（需人工審查）
```

**Entity vs DTO 的區別**：
```
Entity (Entities/)                   DTO (DTOs/)
─────────────────                    ─────────────
對應「一張實體表」                      對應「一個 API 回傳的 JSON」
例：SignRecordMst.cs                  例：PendingSignItemDto.cs
  → 對應 s99_sign_record_mst 表         → 對應 d_list.srd 的查詢結果
  → 欄位 = 表的所有 column               → 欄位 = JOIN 後的 SELECT 結果
  → EF Core 用來做 ORM mapping          → Controller 用來回傳 JSON

一個 DTO 可能取自多個 Entity 的 JOIN：
  PendingSignItemDto
    ├── 來自 SignRecordMst (sign_serno, vou_subject, ...)
    ├── 來自 FlowSpec (flow_name)
    └── 來自 FlowSetup (step_name)
```

---

##### (D) `docs/` — AI 產出的分析文件

```
docs/
├── wireframes/         ← SVG 雛型介面圖
├── business-rules/     ← 商業規則文件
└── pb-analysis/        ← PB 原始碼分析報告
```

**這個目錄是「AI 分析結果的快取」**。它連接了舊系統和新系統：

```
legacy-source/ (舊系統原始碼)
    │
    │  AI 分析
    ▼
docs/ (AI 產出的中間文件)
    │
    │  人工審查 + AI 協助
    ▼
frontend/ + backend/ (新系統程式碼)
```

**各子目錄詳解**：

| 子目錄 | 內容 | 來源 | 使用者 |
|--------|------|------|--------|
| `wireframes/` | 每個頁面的 SVG 線框圖，標註舊操作→新 API 的對應 | AI 讀取 PB 的 HTML 拼接 + JSP 的 JS 函式 | 前端團隊看圖開發元件；PM/設計師審查 UI 佈局 |
| `business-rules/` | 條列式商業規則（如 VR-001：不同意必填原因） | AI 讀取 PB method 的 if/else 邏輯 + JSP 的驗證函式 | 後端團隊實作 Service 的依據；QA 寫測試案例的依據 |
| `pb-analysis/` | PB 方法清單、DataWindow 目錄、API 對應表 | AI 掃描 .sru + .srd 全量分析 | 前後端都會參考；是 openapi.yaml 的設計依據 |

**重要觀念**：`docs/` 裡的文件是**分析過程中的產物**，不是最終規格。最終規格是 `api-spec/openapi.yaml`。但當開發者對 API 設計有疑問時，會回頭查 `docs/pb-analysis/` 了解舊系統原本怎麼做。

---

##### (E) `legacy-source/` — 舊系統原始碼（唯讀博物館）

```
legacy-source/
└── sign/                  ← D:\skyuni\sign 的完整複本
    ├── *.jsp              ← 18 個 JSP 頁面
    ├── sign/n_sign.sru    ← 核心 PB 元件
    ├── dw_sign/*.srd      ← 16 個 DataWindow
    ├── tpec_s61/          ← 工作流程引擎
    ├── sky_webbase/       ← 共用工具庫
    └── webap/             ← Web 框架層
```

**用途**：純參考用，**永遠不修改**。開發者遇到問題時用 Claude Code 讀取這裡的檔案來釐清舊系統行為。

**為什麼需要放在同一個 Git repo**：
1. AI 需要同時存取舊原始碼和新程式碼才能做比對分析
2. Claude Code 的 CLAUDE.md 可以指向這些路徑，讓 AI 自動知道去哪裡找舊邏輯
3. 新人 onboard 時，一個 `git clone` 就能取得完整上下文

**被誰使用**：
```
legacy-source/sign/dw_sign/d_list.srd
    │
    ├──→ AI 產出 → docs/pb-analysis/datawindow_catalog.md
    ├──→ AI 產出 → backend/Entities/SignRecordMst.cs
    ├──→ AI 產出 → backend/DTOs/PendingSignItemDto.cs
    └──→ AI 產出 → api-spec/openapi.yaml 中的 schema

legacy-source/sign/sign_00.jsp
    │
    ├──→ AI 產出 → docs/wireframes/sign_00_dashboard.svg
    ├──→ AI 產出 → docs/business-rules/VR-approval-rules.md
    └──→ AI 產出 → frontend/composables/useApproval.ts
```

---

##### (F) `CLAUDE.md` — Claude Code 的專案記憶

**用途**：當團隊成員啟動 Claude Code 時，AI 會自動讀取這個檔案，瞬間理解整個專案的背景、慣例、和重要參考檔案的位置。

**為什麼重要**：沒有 CLAUDE.md，每次對話都要重新解釋「這是一個從 PB 改寫到 .NET Core 的專案...」。有了它，AI 從第一句話就知道上下文。

---

#### 6.4.3 目錄之間的關係全貌圖

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                                                                             │
│                        legacy-source/ (舊系統)                               │
│                    ┌────────────────────────────┐                           │
│                    │  .srd    .sru    .jsp       │                           │
│                    │ (資料)  (邏輯)  (介面+JS)    │                           │
│                    └──┬────────┬────────┬────────┘                           │
│                       │        │        │                                    │
│            ┌──────────┼────────┼────────┼──────────┐                         │
│            │     AI 分析（Phase 0，Week 1-2）      │                         │
│            └──────────┼────────┼────────┼──────────┘                         │
│                       │        │        │                                    │
│                       ▼        ▼        ▼                                    │
│   ┌──────────────────────────────────────────────────────┐                  │
│   │                    docs/ (分析產物)                    │                  │
│   │                                                      │                  │
│   │  .srd ──→ pb-analysis/datawindow_catalog.md          │                  │
│   │  .sru ──→ pb-analysis/n_sign_method_catalog.md       │                  │
│   │  .sru ──→ business-rules/VR-approval-rules.md        │                  │
│   │  .jsp ──→ wireframes/sign_00_dashboard.svg           │                  │
│   │  全部  ──→ pb-analysis/api_endpoint_mapping.md        │                  │
│   └────────────────┬────────────────┬────────────────────┘                  │
│                    │                │                                        │
│                    │   ┌────────────┘                                        │
│                    │   │                                                     │
│                    ▼   ▼                                                     │
│   ┌──────────────────────────────┐                                          │
│   │     api-spec/openapi.yaml    │ ← 兩個團隊共同討論產出                      │
│   │     (前後端合約中心)           │                                          │
│   └──────────┬──────────┬────────┘                                          │
│              │          │                                                    │
│     ┌────────┘          └─────────┐                                         │
│     ▼                             ▼                                         │
│   ┌─────────────────┐   ┌──────────────────┐                               │
│   │   frontend/      │   │    backend/       │                               │
│   │   (前端團隊)      │   │    (後端團隊)      │                               │
│   │                  │   │                   │                               │
│   │ api/generated/ ◄─┤   │  Controllers/ ◄───┤ 依 openapi.yaml 實作         │
│   │ (自動產出型別)    │   │  (API 進入點)      │                               │
│   │                  │   │                   │                               │
│   │ views/ ◄─────────┤   │  Services/ ◄──────┤ 依 business-rules/ 實作       │
│   │ (從 wireframe)   │   │  (從 PB 邏輯)      │                               │
│   │                  │   │                   │                               │
│   │ composables/ ◄───┤   │  Entities/ ◄──────┤ 依 .srd 自動產出              │
│   │ (從 JSP 的 JS)   │   │  DTOs/ ◄──────────┤ 依 .srd 自動產出              │
│   │                  │   │                   │                               │
│   │ stores/ ◄────────┤   │  Repositories/ ◄──┤ DataWindow SQL → LINQ        │
│   │ (從 openapi)     │   │  (從 .srd SQL)    │                               │
│   └─────────────────┘   └──────────────────┘                               │
│                                                                             │
│   ┌─────────────────────────────────────────┐                               │
│   │              CLAUDE.md                   │                               │
│   │  (告訴 AI 所有目錄的位置和專案慣例)       │                               │
│   │   讓 AI 在任何目錄工作時都知道全貌         │                               │
│   └─────────────────────────────────────────┘                               │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

#### 6.4.4 「誰產出 → 誰消費」的交叉關係表

| 產出者 | 產出物 | 消費者 | 說明 |
|--------|-------|--------|------|
| AI + .srd | `docs/pb-analysis/datawindow_catalog.md` | 後端團隊 | 後端看此文件了解有哪些資料表和查詢 |
| AI + .sru | `docs/pb-analysis/n_sign_method_catalog.md` | 前後端 | 確認 14 支 API 是否完整覆蓋所有功能 |
| AI + .sru + .jsp | `docs/pb-analysis/api_endpoint_mapping.md` | 前後端 | openapi.yaml 的設計依據 |
| AI + .sru | `docs/business-rules/VR-*.md` | 後端 + QA | 後端依此實作，QA 依此寫測試 |
| AI + .jsp + PB HTML | `docs/wireframes/*.svg` | 前端 + PM + 設計師 | 前端依此開發元件，PM/設計師審查 UI |
| 前後端共同 | `api-spec/openapi.yaml` | 前端(自動產出) + 後端(實作依據) | **整個專案的唯一合約** |
| openapi.yaml | `api-spec/mock-data/*.json` | 前端團隊 | Mock Server 用的假資料 |
| 工具自動 | `frontend/api/generated/` | 前端元件/store | TS 型別 + Axios client，不手改 |
| AI + .srd | `backend/Entities/` | backend/Repositories | EF Core ORM mapping |
| AI + .srd | `backend/DTOs/` | backend/Controllers + Services | API 的 request/response 結構 |
| AI + .sru | `backend/Services/` (骨架) | 後端團隊 (填入實作) | ⚠️ 核心邏輯需人工審查 |
| AI + .jsp JS | `frontend/composables/` (骨架) | 前端團隊 (整合到元件) | 驗證邏輯、互動邏輯 |
| legacy-source/ | (唯讀參考) | AI + 所有開發者 | 遇問題時查原始邏輯 |
| CLAUDE.md | (AI 記憶) | Claude Code | 每次啟動自動讀取 |

---

#### 6.4.5 開發場景範例：一個功能從舊到新的完整資料流

**場景：實作「審核送出（批次）」功能**

```
Step 1: 分析（docs/ 產出）
────────────────────────────
AI 讀取 legacy-source/sign/sign_00.jsp 中的 goNext() 函式
    → 產出 docs/business-rules/VR-approval-rules.md
    (規則 VR-001~VR-005：至少勾一筆、不同意需確認、必填原因...)

AI 讀取 legacy-source/sign/sign/n_sign.sru 中的 of_sign_ins()
    → 產出 docs/pb-analysis/ 中的方法分析
    (輸入：content 字串用§分隔，輸出：HTML 結果頁)

Step 2: API 設計（api-spec/ 產出）
────────────────────────────
兩團隊根據 docs/ 的分析，共同定義 api-spec/openapi.yaml：

    POST /api/sign/approve
    Request Body:
      { items: [{ signSerno, decision, note, backStepKey }] }
    Response:
      { success: boolean, message: string }

Step 3: 前端開發（frontend/ 開發）
────────────────────────────
工具從 openapi.yaml 自動產出:
    → frontend/api/generated/services/SignService.ts
      (已有 signApprove(body) 方法)
    → frontend/api/generated/models/ApproveRequest.ts
      (已有 TypeScript interface)

AI 從 goNext() 翻譯出:
    → frontend/composables/useApproval.ts
      (驗證邏輯：至少一筆、N/R 必填原因...)

前端開發者在 frontend/views/SignDashboard.vue 中：
    → 引用 useApproval composable
    → 引用 generated API client
    → 搭配 Element Plus 的 ElTable + ElMessageBox

Step 4: 後端開發（backend/ 開發）
────────────────────────────
AI 從 d_list.srd 產出:
    → backend/Entities/SignRecordMst.cs, SignRecord.cs
    → backend/DTOs/Response/PendingSignItemDto.cs

AI 從 uo_sign_record.uf_sign() 產出:
    → backend/Services/SignService.cs (骨架)
    → 標註 ⚠️「以下狀態轉換邏輯需人工審查」

後端開發者：
    → 實作 Controllers/SignController.cs 的 [HttpPost("approve")]
    → 依 docs/business-rules/VR-approval-rules.md 完善 Service 邏輯
    → 寫 Unit Test 覆蓋 VR-001~VR-005

Step 5: 整合
────────────────────────────
前端把 Mock Server 改指向真實後端 URL
    → 驗證 JSON 格式是否與 openapi.yaml 一致
    → E2E 測試完整批次審核流程
```

---

#### 6.4.6 為什麼不拆成多個 Git Repo？

| 方案 | 優點 | 缺點 |
|------|------|------|
| **單一 Repo (monorepo)** ✅ 建議 | AI 可跨目錄比對新舊程式碼；openapi.yaml 改動在同一個 PR 可看到前後端影響；新人一次 clone 完整 | repo 較大；需設定目錄權限 |
| 多個 Repo（前端/後端/spec 各一個） | 團隊獨立部署 | AI 無法同時看到舊原始碼和新程式碼；openapi 變更需跨 repo 同步；一致性維護困難 |

**以 Claude Code 為主力 AI 工具的情況下，monorepo 的優勢更大**——AI 可以在同一個工作階段中讀取 `legacy-source/` 的舊邏輯，對照 `api-spec/` 的契約，然後直接寫入 `frontend/` 或 `backend/` 的程式碼。

### 6.5 CLAUDE.md 專案設定檔（讓 AI 理解專案上下文）

```markdown
# 簽核系統改寫專案

## 背景
將政府公文簽核系統從 JSP+PowerBuilder 改寫為 Vue 3 + .NET Core。

## 慣例
- API 回傳使用 camelCase JSON
- 日期一律用 ISO 8601，民國年在後端內部轉換
- 中文欄位說明必須寫在 OpenAPI 的 description
- 前端使用 Composition API + TypeScript + Element Plus
- 後端使用 Controller / Service / Repository 模式 + EF Core

## 參考檔案
- 舊 PB 商業邏輯: legacy-source/sign/sign/n_sign.sru
- 舊工作流程引擎: legacy-source/sign/tpec_s61/uo_sign_record.sru
- 舊 DataWindow: legacy-source/sign/dw_sign/*.srd
- 舊 JSP 互動: legacy-source/sign/*.jsp
- API 合約: api-spec/openapi.yaml

## AI 使用原則
- DataWindow → Entity/DTO：可信賴，直接使用
- JSP JavaScript → Vue Composable：可信賴，直接使用
- PB 商業邏輯 → Service：AI 產出骨架，核心邏輯須人工審查
- Wireframe → Vue 元件：可信賴，但佈局細節需設計師調整
```

---

