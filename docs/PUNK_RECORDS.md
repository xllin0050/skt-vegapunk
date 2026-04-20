# PUNK RECORDS

只記錄不易從程式碼或 git history 直接看出的架構決策與取捨。

---

## 長期架構決策

### PbSourceNormalizer：錯誤 BOM 的由來
PB 匯出檔的 BOM 有時是 `C3 BF C3 BE`（UTF-8 重新編碼的 UTF-16LE BOM `FF FE`）而非標準 `FF FE`。`PbSourceNormalizer` 需要先跳過這 2 bytes 再以 UTF-16LE 解碼。若解碼失敗，回傳 warning 不拋例外，確保流程不中斷。

### SchemaReconciliationAnalyzer：型別對映策略
SrdSpec 使用 PowerBuilder 型別（`string`、`long`、`number`），Sybase DDL 使用資料庫型別（`varchar`、`int`、`decimal`）。比對時以「類別」為單位，而非逐字比對：
- 字串類：`char / varchar / string / nchar / nvarchar / text`
- 整數類：`long / int / integer / smallint / tinyint / bigint`
- 數值類：`number / numeric / decimal / real / float / money`
- 日期時間類：`date / datetime / smalldatetime / timestamp`
同一類別內的型別視為等價，不回報差異。

### SchemaReconciliationAnalyzer：跨 DataWindow 累加
同一張 DB table 可能被多個 DataWindow 引用。分析器先掃描所有 SrdSpec 的欄位，依 `table.column` 格式累加欄位集合，最後再做一次統一比對，避免「先到先得」造成欄位漏算。

### SruExtractor：DataWindow 引用的正確識別模式
本系統的 PB 程式碼用兩種模式引用 DataWindow 物件（均為字串字面量）：
1. `xxx.dataobject = 'd_xxx'`
2. `libraryexport(pbl, "d_xxx", exportdatawindow!)`

`.retrieve(arg)` 的參數是 **檢索值**（如 `as_empid`），不是 DataWindow 名稱，不應被抓取。原始實作的 regex 混淆了兩者，已修正。

### SchemaExtractor：Standalone Index 合併策略
DDL 中 index 以獨立的 `-- DDL for Index` 段落定義，與 CREATE TABLE 分離。解析時先收集所有 standalone index，最後依 `target table` 合併進對應的 `SchemaTableSpec.Indexes`，同時保留 `SchemaArtifacts.StandaloneIndexes` 供需要完整清單的場合使用。

### SpecArtifactsGenerator：Schema DDL 編碼
Schema SQL 檔案（`source/schema/*.sql`）使用 ISO-8859-1（Latin-1）編碼，以 `ReadAllBytesAsync` 讀取後用 `Encoding.Latin1.GetString()` 解碼，不走 PbSourceNormalizer。目前硬編碼；若 schema 檔案改用 UTF-8，須調整。

### MigrationOrchestrator：ISourceNormalizer 為可選參數
`ISourceNormalizer` 以可選建構子參數注入（`= null` → 預設 `PbSourceNormalizer`），讓測試可注入 stub 而不需要真實 PB 編碼偵測邏輯，同時維持 production 路徑零改動。

---

## 2026-04-16 Schema Extractor / Reconciliation / Endpoint-DW Map

依 `docs/RECOMMENDATIONS.md` 實作的三個高優先項目，加上 review 發現的三個缺陷修正。

**主要新增**

| 元件 | 功能 |
|------|------|
| `SchemaExtractor` | 解析 Sybase ASE DDL：資料表（174）、Trigger（20）、Index（31） |
| `SchemaReconciliationAnalyzer` | SrdSpec 欄位 vs DB Schema 差異，支援 PowerBuilder/Sybase 型別類別比對 |
| `EndpointDataWindowAnalyzer` | resolved endpoint → DataWindow 交叉索引 |

**修正的缺陷**

1. `SchemaReconciliationEntry` 移除語意誤導的 `SrdPrimaryKey` / `PrimaryKeyMatch`（SrdSpec 無 PK 資訊）
2. `SchemaReconciliationAnalyzer.Analyze()` 改為跨 SrdSpec 累加欄位，修正「先到先得」漏算問題
3. `SruExtractor._dataWindowReferenceRegex` 移除誤抓 `.retrieve(arg)` 的模式，改為只匹配 `dataobject =` 與 `libraryexport`

**已知取捨**

- Regex 欄位解析以縮排深度（`\s{1,8}`）區分欄位與 CONSTRAINT 行，非標準縮排可能有漏解
- 本 schema 無 FOREIGN KEY，FK 清單永遠為空；介面已保留
- spec artifacts 目前需人工引用，尚未自動注入 migration prompt

---

## 2026-04-16 Spec Artifact Index

在 `--spec-source` 輸出流程新增 `spec/INDEX.md`，作為 artifact 目錄文件，避免使用者只看到大量 JSON / Markdown 但不知道每份檔案的用途與建議閱讀順序。

**主要異動**

- `SpecArtifactsGenerator` 在所有 artifacts 寫完後追加輸出 `spec/INDEX.md`
- 目錄內容固定說明核心報告、前端相關、後端相關，以及有 schema / warnings 時才出現的區段
- `SpecArtifactsGeneratorTests` 新增 `INDEX.md` 存在性驗證
- `README.md` 補充 `--spec-source` 會產生 `spec/INDEX.md`

**驗證**

- 以 `SpecArtifactsGeneratorTests` 驗證 `spec/INDEX.md` 會隨 spec pipeline 一起生成

**已知取捨**

- 目錄內容目前是依既有 artifact 類型靜態描述，不逐檔列舉每一個 JSP/DataWindow/Component 實體檔名

---

## 2026-04-20 UnresolvedEndpointInferrer：JSP-first LLM 推導

靜態分析在缺少 `.sru` 時會產生 unresolved endpoint，但 JSP 本身已包含足夠資訊（元件名、方法名、AJAX 目標、session 參數）供 LLM 推導規格。本次實作 `UnresolvedEndpointInferrer` 補齊這個缺口。

**非顯然設計決策**

- **選擇性啟用（optional）**：`UnresolvedEndpointInferrer` 以 `null` 注入 `SpecArtifactsGenerator`。若 `appsettings.json` 沒有 `Agent:ModelName`，spec 模式靜默略過推導步驟，向下相容舊有行為，不需要額外 flag。

- **AJAX 目標自動展開**：主 JSP 透過 AJAX 呼叫的相關 JSP（如 `sign_history_01.jsp`、`sign_history_flow_otpion.jsp`）會被一起附入 prompt。這樣 LLM 能看到完整的前端互動鏈，而不只是入口 JSP。

- **Schema 表名關鍵字比對**：以 PB method name 拆分（按 `.` 和 `_`，過濾 `of`、`uf`、純數字等雜訊）取出關鍵字，再比對 schema 表名。不做全表附入，避免 prompt 過長。最多取 10 張表。

- **JSON 解析容錯**：LLM 回應先嘗試從 markdown code block（` ```json ``` `）擷取，失敗再搜尋第一個 `{` 到最後一個 `}`。避免因 LLM 格式變化導致整批推導失敗。

- **逐筆容錯，不中止**：每個 finding 獨立呼叫 LLM，任一筆失敗回傳 `InferenceSucceeded: false` 的 Stub，不影響其他筆，也不中斷整個 spec pipeline。

**已知限制**

- LLM 推導的 `suggestedRoute` 與 `relatedTables` 僅供參考，不保證與最終 C# 實作吻合，需人工驗證。
- Schema 關鍵字比對採簡單字串包含，可能有誤匹配或漏匹配；無 schema DDL 時直接略過表格資訊。

---

## 2026-04-20 GitHubCopilotClient：Linux CLI cache 目錄預備

GitHub Copilot CLI 在 Linux 會把解壓縮中的暫存檔寫到 `~/.cache/copilot/pkg/<platform>/`。實測若父目錄不存在，CLI 會直接在 `mkdir .extracting-*` 階段崩潰，導致 spec 模式雖已產出 artifact，整體流程仍以 exit code 4 結束。

**非顯然設計決策**

- **啟動前主動補齊 cache 父目錄**：`GitHubCopilotClient` 建立 `CopilotClientOptions` 前，會先確保 Linux 上的 `copilot/pkg/<platform>` 目錄存在，避免把穩定性寄託在 CLI 自己遞迴建目錄。

- **家目錄不可寫時退回 `/tmp`**：若預設 cache root 建立失敗，改建立 `/tmp/skt-vegapunk/copilot-cache` 對應目錄，並透過 `Environment` 傳給 SDK。這讓受限環境至少有可用 fallback，而不是直接在啟動階段中止。

**驗證**

- `GitHubCopilotClientTests` 驗證 Linux 下會先建立對應 cache 目錄
- 實際執行 `dotnet run --project SktVegapunk.Console -- --spec-source "source/sign" --spec-output "/tmp/skt-vegapunk-spec-check-fixed-3"`，流程成功結束，`LLM 推導 Endpoint: 5`

---

## 2026-04-16 移除 LLM Spec Enrichment

`--llm-spec-from` 已從專案移除。原因不是功能正確性，而是使用體驗與維運成本不符目前需求：即使改成 attachments 與分批呼叫，仍增加 CLI 複雜度、設定面與測試面，而使用者已明確決定暫時不走這條路。

**主要異動**

- 移除 `Program.cs` 中的 `--llm-spec-from` 參數與執行分支
- 移除 `LlmSpecEnricher`、`LlmSpecPromptBuilder` 與相關測試
- `GitHubCopilotClient` 回復為只支援 `systemPrompt + userPrompt + timeout` 的最小介面
- 移除 `Agent:SpecEnrichmentSystemPrompt` 與 `GitHubCopilot:SpecResponseTimeoutSeconds`
- README 刪除 LLM spec enrichment 的使用說明

**驗證**

- 後續以 `dotnet build` 與 `dotnet test` 驗證移除後仍可正常編譯與通過測試
