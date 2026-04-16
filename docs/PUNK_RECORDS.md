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
