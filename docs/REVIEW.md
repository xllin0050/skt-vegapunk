Phase 1 Code Review 報告

架構與設計：做得好的部分
資料模型簡潔一致——所有 record（SrdSpec、SruSpec、JspInvocation、EndpointCandidate、MigrationSpec）都用 sealed record + IReadOnlyList，immutable 且符合既有風格。
介面隔離——ISrdExtractor、ISruExtractor、IJspExtractor、ISpecReportBuilder 各自獨立，符合 PLANS_OPUS 3.5 的設計原則。
SruExtractor 復用 IPbScriptExtractor——不重複造 event block 提取邏輯，符合既有架構。
EndpointStatus enum + StatusReason——Resolved/Unresolved 清晰標記繼承鏈缺口。
Regex 字段命名——統一 _ 前綴，RegexOptions.Compiled，符合效能最佳實踐。

🔴 Critical — 會導致真實資料提取失敗
C1. JspExtractor._componentCallRegex 太寬泛，第一筆 match 幾乎一定不是 CORBA 呼叫
實際 JSP 裡的呼叫鏈是：

```
uo_sign_record iJagComponent = null;
// ... CORBA boilerplate ...
iJagComponent.uf_create_sign(...)
```

但 _componentCallRegex（(?<component>\w+)\.(?<method>\w+)\s*\(）會先匹配到 ls_vou_subject.getBytes(、props.put(、ORB.init(、NamingContextHelper.narrow( 等，第一筆 match 不是實際的 component 呼叫。

此外，componentName 拿到的是「變數名 iJagComponent」而非「型別名 uo_sign_record」。PLANS_OPUS 要的是型別名對應到 .sru 檔案。

建議：

改為二步驟：先用 regex 找 (?<type>\w+)\s+iJagComponent\s*= 提取型別名。
再用 iJagComponent\.(?<method>\w+)\s*\( 專門匹配實際呼叫。
或更穩健地，找 //呼叫 component 註解所在行來定位。

C2. SrdExtractor._columnRegex 要求 key=\w+ 必須存在，但多數欄位沒有 key=
比對 d_signkind.srd 真實內容：

```
column=(type=char(40) update=yes updatewhereclause=yes name=sign_kind_name dbname="s99_sign_kind.sign_kind_name" )
```

此欄位 沒有 key= 屬性，但 regex 要求 key=\w+ 為必填。結果是：只有主鍵欄位能被提取，非主鍵欄位全部遺漏。

建議：將 key=\w+\s+ 改為 (?:key=\w+\s+)?。同時結尾加 \s* 允許 ) 前有空格。

C3. SrdExtractor._retrieveRegex 無法處理 PBSELECT 中的 ~" 跳脫引號
真實 retrieve SQL：

```
retrieve="PBSELECT( VERSION(400) TABLE(NAME=~"s99_sign_kind~" ) ...)"
```
Regex retrieve="(?<sql>[^"]*)" 在遇到第一個 ~" 的 " 時就停止，只會擷取到 PBSELECT( VERSION(400) TABLE(NAME=~。

建議：將 [^"]* 改為 (?:[^"~]|~.)* 或改用非 regex 的手動 parser 逐字元掃描，遇 ~ 跳過下一字元。

🟡 Significant — 違反設計原則或影響可測試性

S1. SpecReportBuilder.WriteJsonAsync 繞過 ITextFileStore 直接用 File.WriteAllTextAsync
SpecReportBuilder.cs:215 的 WriteJsonAsync 方法直接呼叫 File.WriteAllTextAsync，而 WriteReportAsync 寫 markdown 時卻用 _textFileStore.WriteAllTextAsync。這違反 DIP，且讓 JSON 輸出無法被測試替身攔截。

同樣，SpecReportBuilder.cs:220 直接用 Directory.CreateDirectory。

建議：JSON 寫入也走 _textFileStore，目錄建立抽象到介面或改為讓 ITextFileStore 自行處理。

S2. EnsureDirectoryExists 是偽非同步

```
private static async Task EnsureDirectoryExists(string directory, CancellationToken cancellationToken)
{
    if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
    await Task.CompletedTask;
}
```
async + await Task.CompletedTask 無意義，直接回傳 Task.CompletedTask 或改為同步方法即可。

S3. DateTime.UtcNow 直接寫死在 GenerateMarkdownReport
報告內嵌時間戳但無法注入時鐘，導致輸出不確定（non-deterministic），無法寫 snapshot/golden test。

建議：透過建構子或參數注入 TimeProvider（.NET 8+）或 Func<DateTimeOffset>。

S4. Phase 1 完全沒有單元測試
PUNK_RECORDS 記錄「13 passed」，但實際測試組成是：

3 個 PbScriptExtractorTests (Phase 0 以前)
3 個 MigrationOrchestratorTests (Phase 0 以前)
2 個 OpenRouterClientTests (Phase 0 以前)
5 個 PbSourceNormalizerTests (Phase 0)

Phase 1 新增的 4 個元件（SrdExtractor、SruExtractor、JspExtractor、SpecReportBuilder）沒有任何一個測試。PLANS_OPUS 明確要求用真實 .srd/.sru/.jsp 做 golden test，這是目前最大的品質缺口。

🟢 Minor — 建議改善
| # |位置|	問題|	建議|
|---|---|---|---|
|M1|	SrdExtractor.Extract 第 41 行|	Regex.Match(type, ...) 每次呼叫都重新編譯 regex|	提取為 static readonly Regex|
|M2 |	SrdExtractor / SruSpec / JspInvocation |	FileName 都回傳 "" 要求外部設定	| 考慮讓 Extract 接受 fileName 參數，或用 with 表達式在外層設定 |
|M3 |	SruExtractor._functionStartRegex |	雖然用 ; 區分 prototype vs. definition，但如果 forward prototypes 區塊也含 ; 結尾可能誤匹配 |	可先移除 prototypes 區段文字再跑 routine regex |
|M4|	SpecReportBuilder.DetermineRoute	|接收 methodName 參數但沒使用 |	移除或加 _ 前綴|
| M5|	JspExtractor 參數提取	|使用 IndexOf(callText) 找位置，但如果同一 pattern 出現多次會永遠找到第一次出現|	應使用 match.Index 取得精確位置（已有 Match 物件）|

建議優先處理順序
C1 + C2 + C3：修復三個 regex，否則對真實資料全部提取失敗。