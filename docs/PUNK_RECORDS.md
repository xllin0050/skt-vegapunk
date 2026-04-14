# PUNK RECORDS

## 實作摘要
- 完成第一版「單檔 PB 後端轉換 PoC」。
- 流程已落地為：`Preprocessing -> Generating -> Validating -> Repairing`。
- 支援回饋迴圈（build 失敗時自動帶錯誤訊息重試，直到上限）。

### 2026-04-14 Generation Readiness 最後四步補齊
- 目標與範圍：完成進入 generation phase 前最後缺的四步，包含後端 `blob / expression source tracing`，以及前端的 `control inventory`、`payload mapping`、`interaction graph`。
- 主要程式異動與決策：
  - `SktVegapunk.Core/Pipeline/Spec/RequestBindingAnalyzer.cs`
    - 補上遞迴來源追蹤，可把 `lb_vou_subject = ls_vou_subject.getBytes("UTF-8")` 這類 blob 參數回溯到原始 `request.getParameter(...)`。
    - Ajax payload 解析新增 object-style `myForm["key"] = ...` 支援，並會辨識 `$("#id").val()`、`document.getElementById("id").value` 對應到的控制項。
  - `SktVegapunk.Core/Pipeline/Spec/JspPrototypeExtractor.cs`
  - `SktVegapunk.Core/Pipeline/Spec/JspPrototypeArtifact.cs`
  - `SktVegapunk.Core/Pipeline/Spec/JspControlPrototype.cs`
    - JSP prototype JSON 新增 `controls`，把 `input/select/textarea/button/a` 抽成結構化控制項清單，供前端生成與 payload mapping 共用。
  - `SktVegapunk.Core/Pipeline/Spec/InteractionGraphAnalyzer.cs`
  - `SktVegapunk.Core/Pipeline/Spec/InteractionGraph.cs`
  - `SktVegapunk.Core/Pipeline/Spec/InteractionGraphEdge.cs`
    - 新增 interaction graph，從 `Click` 事件對回 JS handler，再串到 `Submit`、`Ajax`、`OpenWindow`、`Navigate`。
  - `SktVegapunk.Core/Pipeline/Spec/ControlInventoryArtifact.cs`
  - `SktVegapunk.Core/Pipeline/Spec/PayloadMappingArtifact.cs`
  - `SktVegapunk.Core/Pipeline/Spec/SpecArtifactsGenerator.cs`
    - spec 流程新增 `control-inventory.*`、`payload-mappings.*`、`interaction-graph.*` 輸出。
  - `SktVegapunk.Core/Pipeline/Spec/GenerationPhasePlanner.cs`
    - generation plan 改為反映四步已完成，後端與前端都可進入 generation phase；unresolved 仍保留 placeholder。
  - `SktVegapunk.Console/Program.cs`
    - `SpecArtifactsGenerator` 注入新 analyzer。
- 測試與驗證：
  - 新增 `SktVegapunk.Tests/Pipeline/Spec/InteractionGraphAnalyzerTests.cs`。
  - 更新 `SktVegapunk.Tests/Pipeline/Spec/JspPrototypeExtractorTests.cs`。
  - 更新 `SktVegapunk.Tests/Pipeline/Spec/RequestBindingAnalyzerTests.cs`，覆蓋 blob 來源追蹤與 object-style ajax payload。
  - 更新 `SktVegapunk.Tests/Pipeline/Spec/PageFlowAnalyzerTests.cs`。
  - 更新 `SktVegapunk.Tests/Pipeline/Spec/GenerationPhasePlannerTests.cs`。
  - 更新 `SktVegapunk.Tests/Pipeline/Spec/SpecArtifactsGeneratorTests.cs`。
  - `dotnet build SktVegapunk.slnx`：成功。
  - `dotnet test SktVegapunk.slnx /nr:false /m:1 /p:BuildInParallel=false /p:UseSharedCompilation=false`：成功（34 passed）。
  - `dotnet format SktVegapunk.slnx --verify-no-changes`：成功。
  - `dotnet run --project SktVegapunk.Console -- --spec-source source --spec-output output/source`：成功，產出 `control-inventory.*`、`payload-mappings.*`、`interaction-graph.*`。
- 已知取捨與後續建議：
  - 這一版 interaction graph 仍以 inline `onclick` 與 `function name(...) { ... }` 的最小 regex 規則為主，對匿名 callback 或動態綁定事件仍不完整。
  - control inventory 以靜態 HTML / JSP prototype 為主，對 `out.print(...)` 動態拼出的控制項仍可能漏抓。

### 2026-04-14 規格提取 CLI 與樣本驗證
- 目標與範圍：確認根目錄 `source/` 內的原始檔是否能穩定產出規格報告與中介資料，並補齊 `Console` 的 spec artifacts 執行入口。
- 主要程式異動與決策：
  - `SktVegapunk.Core/Pipeline/Spec/SpecArtifactsGenerator.cs`
    - 新增最小規格提取流程，負責遞迴掃描 `.srd`、`.sru`、`.jsp`，串接 normalizer、extractor 與 report builder。
    - 將相對路徑寫回 `FileName` / `JspFileName`，讓報告與中介檔案可回指來源位置。
  - `SktVegapunk.Core/Pipeline/Spec/SpecArtifactsGenerationResult.cs`
    - 新增執行結果摘要，回傳 DataWindow、Component、JSP invocation 與 warning 數量。
  - `SktVegapunk.Core/Pipeline/Spec/SpecReportBuilder.cs`
    - JSON 輸出改為保留來源相對路徑，避免不同資料夾下的同名 `.srd` / `.sru` 互相覆蓋。
  - `SktVegapunk.Console/Program.cs`
    - 新增 `--spec-source` / `--spec-output` 模式，直接輸出 `output/<name>/spec/*`。
  - `SktVegapunk.Console/appsettings.json`
    - `Agent:SystemPrompt` 改為英文，內容改成以遷移、保留業務語意、輸出可編譯 C# 為目標。
- 測試與驗證：
  - 新增 `SktVegapunk.Tests/Pipeline/Spec/SpecArtifactsGeneratorTests.cs`。
  - 擴充 `SktVegapunk.Tests/Pipeline/Spec/SpecReportBuilderTests.cs`，驗證相對路徑輸出不覆蓋。
  - `dotnet build SktVegapunk.slnx`：成功。
  - `dotnet test SktVegapunk.slnx /nr:false /m:1 /p:BuildInParallel=false /p:UseSharedCompilation=false`：成功（24 passed）。
  - `dotnet run --project SktVegapunk.Console -- --spec-source source/sign --spec-output output/sign`：成功，產出 35 個 DataWindow、17 個 Component、18 個 JSP invocation。
  - `dotnet run --project SktVegapunk.Console -- --spec-source source/sign/webap --spec-output output/sign-webap`：成功，產出 6 個 Component。
  - `dotnet run --project SktVegapunk.Console -- --spec-source source/sign/tpec_s61 --spec-output output/sign-tpec-s61`：成功，產出 17 個 DataWindow、1 個 Component。
- 已知取捨與後續建議：
  - `JspExtractor` 目前每個 JSP 只提取第一個符合規則的 component 呼叫；對複雜 JSP 仍可能低估 invocation 數。
  - `SpecReportBuilder` 的 HTTP method / route 推導仍是啟發式規則，適合作為中介規格，不是最終 API 契約。

### 2026-04-14 JSP Prototype 與 Unresolved 根因輸出
- 目標與範圍：為 `output/source/spec` 補上 unresolved endpoint 根因說明，並從 JSP 直接輸出可供前端搬遷參考的 HTML、JavaScript、CSS prototype。
- 主要程式異動與決策：
  - `SktVegapunk.Core/Pipeline/Spec/JspPrototypeExtractor.cs`
    - 新增 JSP prototype extractor，會保留 HTML 結構，將 JSP scriptlet 以 placeholder 取代，並拆出 inline script / style。
    - 另外提取 `form`、外部 `script src` 與 `stylesheet href`，一起寫入 JSON 摘要。
  - `SktVegapunk.Core/Pipeline/Spec/JspPrototypeArtifact.cs`
  - `SktVegapunk.Core/Pipeline/Spec/JspFormPrototype.cs`
    - 新增 JSP 原型與表單資料模型。
  - `SktVegapunk.Core/Pipeline/Spec/UnresolvedEndpointAnalyzer.cs`
  - `SktVegapunk.Core/Pipeline/Spec/UnresolvedEndpointFinding.cs`
    - 新增 unresolved endpoint 根因分析，區分 `MissingComponentSource` 與 `MissingPrototype`。
  - `SktVegapunk.Core/Pipeline/Spec/SpecArtifactsGenerator.cs`
    - 在既有 spec 產生流程中加入 `jsp/*.html`、`jsp/*.js`、`jsp/*.css`、`jsp/*.json` 輸出。
    - 同步輸出 `spec/unresolved-causes.md`。
  - `SktVegapunk.Console/Program.cs`
    - spec mode 執行摘要新增 `JSP Prototype` 計數。
- 測試與驗證：
  - 新增 `SktVegapunk.Tests/Pipeline/Spec/JspPrototypeExtractorTests.cs`。
  - 新增 `SktVegapunk.Tests/Pipeline/Spec/UnresolvedEndpointAnalyzerTests.cs`。
  - 更新 `SktVegapunk.Tests/Pipeline/Spec/SpecArtifactsGeneratorTests.cs`。
  - `dotnet build SktVegapunk.slnx`：成功。
  - `dotnet test SktVegapunk.slnx /nr:false /m:1 /p:BuildInParallel=false /p:UseSharedCompilation=false`：成功（26 passed）。
  - `dotnet run --project SktVegapunk.Console -- --spec-source source --spec-output output/source`：成功，產出 18 個 JSP prototype，並寫出 `output/source/spec/unresolved-causes.md`。
- 已知取捨與後續建議：
  - 這次的 JSP prototype 仍屬 deterministic prototype，不會執行 JSP，也不會把 `out.print(...)` 真正還原成最終 DOM。
  - 若要真的支撐前端搬遷，下一步應補抽 `onclick` / `submit` / `ajax` / `window.open` 等互動事件的結構化模型，而不只是保留原始 JS。

### 2026-04-14 JSP 互動事件結構化抽取
- 目標與範圍：在既有 JSP prototype JSON 內加入可供前端流程分析使用的互動事件資料，並確認 `source/schema` 不會改寫既有 unresolved 根因。
- 主要程式異動與決策：
  - `SktVegapunk.Core/Pipeline/Spec/JspInteractionEvent.cs`
    - 新增互動事件模型，保留事件種類、觸發方式、目標、值與原始片段。
  - `SktVegapunk.Core/Pipeline/Spec/JspPrototypeArtifact.cs`
    - 在 JSP prototype JSON 中加入 `Events` 欄位。
  - `SktVegapunk.Core/Pipeline/Spec/JspPrototypeExtractor.cs`
    - 新增 regex 抽取 `Click`、`FormActionChange`、`Submit`、`Ajax`、`OpenWindow`、`Navigate`。
    - 先保留 deterministic regex 策略，不引入 JS AST，降低複雜度。
- 驗證與觀察：
  - `source/schema/README.md` 與 schema 檔只補資料庫 DDL / ERD 背景，沒有 `n_sign_history` 或 `of_sign_select*` 的新證據；`unresolved-causes.md` 維持原判定。
  - `output/source/spec/jsp/sign/sign_00.json`
    - 已可看到 `events` 陣列，包含按鈕點擊、form action 改寫、submit、ajax、navigate。
  - `output/source/spec/jsp/sign/sign_dtl.json`
    - 已可看到 `OpenWindow` 與多個 `FormActionChange` / `Submit` 事件。
- 測試與驗證：
  - 更新 `SktVegapunk.Tests/Pipeline/Spec/JspPrototypeExtractorTests.cs`。
  - `dotnet build SktVegapunk.slnx`：成功。
  - `dotnet test SktVegapunk.slnx /nr:false /m:1 /p:BuildInParallel=false /p:UseSharedCompilation=false`：成功（27 passed）。
  - `dotnet run --project SktVegapunk.Console -- --spec-source source --spec-output output/source`：成功，JSP prototype 仍為 18 份。
- 已知取捨與後續建議：
  - 目前 `Ajax` 只抽 `url/type/dataType`，還沒結構化 `data` payload keys。
  - 目前事件不帶行號；若後續要做更精準的頁面流程圖或 traceability，建議再補 source location。

### 2026-04-14 頁面 Flow Graph 輸出
- 目標與範圍：把 JSP prototype 內的互動事件進一步轉成頁面流程圖，讓 `output/source/spec` 可直接看到 `JSP -> JSP/API/HTML` 的 flow edges。
- 主要程式異動與決策：
  - `SktVegapunk.Core/Pipeline/Spec/PageFlowAnalyzer.cs`
    - 新增 flow analyzer，會從 `FormActionChange + Submit`、`Ajax`、`OpenWindow`、`Navigate` 與 `ComponentCall` 推導邊。
  - `SktVegapunk.Core/Pipeline/Spec/PageFlowGraph.cs`
  - `SktVegapunk.Core/Pipeline/Spec/PageFlowEdge.cs`
    - 新增 page flow graph 資料模型。
  - `SktVegapunk.Core/Pipeline/Spec/JspInteractionEvent.cs`
    - 補上 `Order`，確保事件以原始出現順序推導流程，而不是按事件種類分組。
  - `SktVegapunk.Core/Pipeline/Spec/SpecArtifactsGenerator.cs`
    - 新增 `spec/page-flow.md` 與 `spec/page-flow.json` 輸出。
- 驗證與觀察：
  - `output/source/spec/page-flow.md`
    - `sign/sign_00.jsp` 已可看到 `Ajax -> sign_pick_api_*.jsp`、`Submit -> sign_dtl.jsp / sign_ins.jsp`、`ComponentCall -> n_sign.of_sign_00`。
    - `sign/sign_dtl.jsp` 已可看到 `Submit -> sign_ins.jsp`、`OpenWindow -> sign_select.jsp?...`。
  - `source/schema` 仍只補資料庫 schema 背景，對 unresolved 根因沒有新影響。
- 測試與驗證：
  - 新增 `SktVegapunk.Tests/Pipeline/Spec/PageFlowAnalyzerTests.cs`。
  - `dotnet build SktVegapunk.slnx`：成功。
  - `dotnet test SktVegapunk.slnx /nr:false /m:1 /p:BuildInParallel=false /p:UseSharedCompilation=false`：成功（28 passed）。
  - `dotnet run --project SktVegapunk.Console -- --spec-source source --spec-output output/source`：成功。
- 已知取捨與後續建議：
  - 目前 `page-flow` 仍是 deterministic 推導，對動態拼接 URL 只保留可辨識的 `.jsp/.html` 前綴。
  - 若後續要畫更完整的使用者操作流程，下一步應把 `Click -> handler -> action change -> submit` 串成更細的 interaction graph。

### 2026-04-14 文件進度整理
- 目標與範圍：回顧並標示 `docs/1 - Multi-Agent System.md` 中 `Analysis Agent` 與 `Decoupling Agent` 的實際完成度，並同步更新 `README.md` 的目前進度說明。
- 主要文件異動與決策：
  - `README.md`
    - 新增 `1 - Multi-Agent System.md` 的文件索引。
    - 補上「目前進度」區塊，直接標示 `Analysis Agent` 已落地、`Decoupling Agent` 仍只完成事件區塊拆解。
  - `docs/PUNK_RECORDS.md`
    - 補錄這次的文件整理結果，方便後續快速查到專案目前階段。

### 2026-04-14 Generation Phase 補件計畫
- 目標與範圍：接受 unresolved endpoint 先保留 placeholder，不阻塞 generation phase，並把目前進入 generation phase 所需的最小補件整理成可直接跟進的輸出文件。
- 主要程式異動與決策：
  - `SktVegapunk.Core/Pipeline/Spec/UnresolvedEndpointAnalyzer.cs`
    - `unresolved-causes.md` 改為 placeholder 口徑，明確標示這些 endpoint 先生成 stub，不作為當前阻塞項。
  - `SktVegapunk.Core/Pipeline/Spec/GenerationPhasePlanner.cs`
    - 新增 generation phase 規劃器，依據 endpoint、JSP prototype、page flow 與 unresolved findings 輸出 `generation-phase-plan.md`。
  - `SktVegapunk.Core/Pipeline/Spec/SpecArtifactsGenerator.cs`
    - 在既有 spec 流程中串接 `generation-phase-plan.md` 輸出。
  - `SktVegapunk.Console/Program.cs`
    - 更新 `SpecArtifactsGenerator` 建構，注入 `GenerationPhasePlanner`。
- 測試與驗證：
  - 新增 `SktVegapunk.Tests/Pipeline/Spec/GenerationPhasePlannerTests.cs`。
  - 更新 `SktVegapunk.Tests/Pipeline/Spec/UnresolvedEndpointAnalyzerTests.cs`。
  - 更新 `SktVegapunk.Tests/Pipeline/Spec/SpecArtifactsGeneratorTests.cs`。
- 驗證結果：
  - `dotnet build SktVegapunk.slnx`：成功。
  - `dotnet test SktVegapunk.slnx /nr:false /m:1 /p:BuildInParallel=false /p:UseSharedCompilation=false`：成功（30 passed）。
  - `dotnet format SktVegapunk.slnx --verify-no-changes`：成功。
  - `dotnet run --project SktVegapunk.Console -- --spec-source source --spec-output output/source`：成功，更新 `output/source/spec/unresolved-causes.md` 與 `output/source/spec/generation-phase-plan.md`。
- 已知取捨與後續建議：
  - `generation-phase-plan.md` 目前是依現有 deterministic artifacts 彙整的執行計畫，不會自動補齊 request binding、payload mapping 或 response classification。
  - 下一步若要真正進 generation phase，應先補 `request binding artifact`、`response classification`、`control inventory` 與 `payload mapping`。

### 2026-04-14 Request Binding Artifact
- 目標與範圍：補上 generation phase 最缺的橋接層，將 JSP component call 的參數來源、form submit、ajax payload 摘要輸出成可供後端生成使用的 request binding artifact。
- 主要程式異動與決策：
  - `SktVegapunk.Core/Pipeline/Spec/RequestBindingAnalyzer.cs`
    - 新增 request binding analyzer，會將 JSP 中的 `request/session/application` 賦值、component call 參數、form submit 與 ajax payload 對齊成結構化輸出。
    - 對 `request.getParameter -> 預設 literal fallback` 的案例保留 heuristic note，避免把外部輸入誤判成常值。
  - `SktVegapunk.Core/Pipeline/Spec/RequestBindingArtifact.cs`
  - `SktVegapunk.Core/Pipeline/Spec/RequestBindingParameter.cs`
  - `SktVegapunk.Core/Pipeline/Spec/RequestBindingTransport.cs`
  - `SktVegapunk.Core/Pipeline/Spec/RequestPayloadField.cs`
  - `SktVegapunk.Core/Pipeline/Spec/JspSourceArtifact.cs`
    - 新增 request binding 所需的中介模型。
  - `SktVegapunk.Core/Pipeline/Spec/SpecArtifactsGenerator.cs`
    - spec 流程新增 `request-bindings.md` 與 `request-bindings.json` 輸出。
  - `SktVegapunk.Core/Pipeline/Spec/GenerationPhasePlanner.cs`
    - generation plan 改為讀取已產出的 request bindings，不再把它列為待辦，而是把下一步調整成 `response classification` 與 blob/expression 來源追蹤。
  - `SktVegapunk.Console/Program.cs`
    - 更新 `SpecArtifactsGenerator` 建構，注入 `RequestBindingAnalyzer`。
- 測試與驗證：
  - 新增 `SktVegapunk.Tests/Pipeline/Spec/RequestBindingAnalyzerTests.cs`。
  - 更新 `SktVegapunk.Tests/Pipeline/Spec/SpecArtifactsGeneratorTests.cs`。
  - 更新 `SktVegapunk.Tests/Pipeline/Spec/GenerationPhasePlannerTests.cs`。
  - `dotnet build SktVegapunk.slnx`：成功。
  - `dotnet test SktVegapunk.slnx /nr:false /m:1 /p:BuildInParallel=false /p:UseSharedCompilation=false`：成功（31 passed）。
  - `dotnet format SktVegapunk.slnx --verify-no-changes`：成功。
  - `dotnet run --project SktVegapunk.Console -- --spec-source source --spec-output output/source`：成功，產出 `output/source/spec/request-bindings.md` 與 `output/source/spec/request-bindings.json`。
- 已知取捨與後續建議：
  - 目前 `FormSubmit` 的 payload 欄位只在表單內有可辨識 `name/id` 時才能列出；像 `thisform` 這類由 JSP 動態輸出的表單仍可能只留下 `form:<name>` 佔位。
  - `blob` 與 `.getBytes("UTF-8")` 這類 expression 目前仍標為 `Variable`，下一步應補 expression source tracing。

### 2026-04-14 Response Classification Artifact
- 目標與範圍：補上 generation phase 所需的 response contract 粗分類，將 endpoint 依現有 PB routine / JSP 線索分類為 `json`、`html`、`file`、`script-redirect`、`text`。
- 主要程式異動與決策：
  - `SktVegapunk.Core/Pipeline/Spec/ResponseClassificationAnalyzer.cs`
    - 新增 response classification analyzer，優先以 PB routine body 判斷，找不到 routine 時才退回 JSP fallback。
    - 目前採最小規則集，避免過度推論；若同時有多種線索，優先順序為 `script-redirect -> file -> json -> html -> text`。
  - `SktVegapunk.Core/Pipeline/Spec/ResponseClassificationArtifact.cs`
    - 新增 response classification 中介模型。
  - `SktVegapunk.Core/Pipeline/Spec/SpecArtifactsGenerator.cs`
    - spec 流程新增 `response-classifications.md` 與 `response-classifications.json` 輸出。
  - `SktVegapunk.Core/Pipeline/Spec/GenerationPhasePlanner.cs`
    - generation plan 改為讀取已產出的 response classifications，不再把它列為待辦，而是把下一步收斂到 blob/expression 來源追蹤。
  - `SktVegapunk.Console/Program.cs`
    - 更新 `SpecArtifactsGenerator` 建構，注入 `ResponseClassificationAnalyzer`。
- 測試與驗證：
  - 新增 `SktVegapunk.Tests/Pipeline/Spec/ResponseClassificationAnalyzerTests.cs`。
  - 更新 `SktVegapunk.Tests/Pipeline/Spec/SpecArtifactsGeneratorTests.cs`。
  - 更新 `SktVegapunk.Tests/Pipeline/Spec/GenerationPhasePlannerTests.cs`。
  - `dotnet build SktVegapunk.slnx`：成功。
  - `dotnet test SktVegapunk.slnx /nr:false /m:1 /p:BuildInParallel=false /p:UseSharedCompilation=false`：成功（32 passed）。
  - `dotnet format SktVegapunk.slnx --verify-no-changes`：成功。
  - `dotnet run --project SktVegapunk.Console -- --spec-source source --spec-output output/source`：成功，產出 `output/source/spec/response-classifications.md` 與 `output/source/spec/response-classifications.json`。
- 已知取捨與後續建議：
  - 這一版 classification 只做 coarse-grained 類別判定，還不是完整 response schema。
  - `script-redirect` 與 `html` 都可能回傳字串型 HTML 片段；若後續要生成 ASP.NET action result，仍需補 response payload/schema 細節。

### 2026-03-24 GitHub Copilot SDK 遷移
- 目標與範圍：將原本直連 OpenRouter REST API 的生成路徑，替換為 `github/copilot-sdk` 的 .NET SDK，並維持既有 `ICodeGenerator` / orchestrator 流程不變。
- 主要程式異動與決策：
  - `SktVegapunk.Core/GitHubCopilotClient.cs`
    - 新增 `GitHubCopilotClient`，以 `CopilotClient` + `SessionConfig` + `SendAndWaitAsync` 封裝單次生成。
    - `SystemMessage` 改用 `SystemMessageConfig`，避免把系統提示詞拼進 user prompt，讓模型上下文責任明確分離。
    - 保留最小內部介面 `IGitHubCopilotExecutor`，讓測試可替換掉 SDK/CLI 側效果，不把 CLI 啟動耦合進單元測試。
  - `SktVegapunk.Core/Pipeline/CopilotCodeGenerator.cs`
    - 以 `CopilotCodeGenerator` 取代 `OpenRouterCodeGenerator`，其餘 `ICodeGenerator` 合約不變，讓 pipeline 不需要知道 provider 已切換。
  - `SktVegapunk.Console/Program.cs`
    - 移除 `OpenRouter:ApiKey` 與 `HttpClient` 初始化，改讀取 `GitHubCopilot:*` 設定並以 `await using` 管理 SDK client 生命週期。
  - `SktVegapunk.Console/appsettings.json`
    - 預設模型改為 `gpt-5`，新增 `GitHubCopilot:CliPath`。
  - `SktVegapunk.Core/SktVegapunk.Core.csproj`
    - 新增 `GitHub.Copilot.SDK` 套件相依。
  - `SktVegapunk.Core/Properties/AssemblyInfo.cs`
    - 加入 `InternalsVisibleTo("SktVegapunk.Tests")`，只對測試公開最小內部面。
  - `SktVegapunk.Tests/GitHubCopilotClientTests.cs`
    - 以 stub executor 驗證 client 參數傳遞、例外防呆與 disposal 行為。
- 文件異動：
  - `README.md`
    - 移除 OpenRouter API key 設定，改為 GitHub Copilot CLI / token 驗證說明。
    - 更新模型範例與新增 `GitHubCopilot:*` 設定鍵。
  - `docs/PROGRAM_FLOW.md`
    - 流程圖與元件職責改為 `GitHubCopilotClient` / `CopilotCodeGenerator`。
- 驗證結果：
  - `dotnet build SktVegapunk.slnx`：成功。
  - `dotnet test SktVegapunk.slnx /nr:false /m:1 /p:BuildInParallel=false /p:UseSharedCompilation=false`：成功（22 passed）。
  - `dotnet format SktVegapunk.slnx --verify-no-changes --no-restore`：成功。
- 已知取捨與後續建議：
  - GitHub Copilot SDK 目前為 preview，API 與相依 CLI 版本仍可能變動；這次選擇用最薄封裝降低後續升級面。
  - 這次先使用 `PermissionHandler.ApproveAll` 讓 SDK 可在非互動流程下運作；若之後要更嚴格控管工具權限，應再明確限制 session 可用工具集合。
  - README 已更新，因為執行前提與 secrets 鍵名都已改變，屬於直接影響使用者的變更。

### 2026-02-23 Phase 0：編碼正規化
- 新增 `ISourceNormalizer` / `PbSourceNormalizer`，支援錯誤 BOM (`C3 BF C3 BE`) 自動跳過並以 UTF-16LE 解碼，失敗時回傳 warning 不丟例外。
- 新增 `SourceArtifact` record；`ITextFileStore` 擴充 `ReadAllBytesAsync`，並更新 `FileTextStore` 與測試 stub。
- 新增 `PbSourceNormalizerTests`（含 `d_signkind.srd`、`n_sign.sru` golden 取樣解碼）。
- `MigrationState` 預先納入 `Normalizing`/`Analyzing` 枚舉值供後續擴充。
- 測試：本機 build 成功；`dotnet test` 因 sandbox Socket 限制無法啟動 vstest，需在 CI 或可開啟 socket 的環境重跑。

### 2026-02-23 Phase 1：規格提取（Deterministic Extractors）
- 實作 `.srd` / `.sru` / `.jsp` 的機械式規格提取，避免依賴 AI 做事實提取。
- **1a. SrdExtractor**：解析 DataWindow 定義，提取欄位、SQL、參數與資料表。
  - 資料模型：`SrdColumn`、`SrdArgument`、`SrdSpec`。
  - 支援 `char(40)` 等類型長度解析、`dbname` 提取資料表名。
  - 介面：`ISrdExtractor`、實作：`SrdExtractor`。
- **1b. SruExtractor**：解析 PowerScript 類別，提取原型、函式本文、事件區塊。
  - 資料模型：`SruPrototype`、`SruRoutine`、`SruSpec`。
  - 支援 `global type ... from ...` 繼承解析、`forward prototypes` 提取、`function/subroutine` 本文解析。
  - 掃描 DataWindow 引用（`datawindow=`、`.retrieve(`）與 SQL 關鍵字。
  - 介面：`ISruExtractor`、實作：`SruExtractor`（內部復用 `IPbScriptExtractor`）。
- **1c. JspExtractor**：解析 JSP 檔案，提取 CORBA 呼叫與 HTTP 參數。
  - 資料模型：`JspInvocation`。
  - 支援 `component.of_xxx(...)` 方法呼叫解析、`request.getParameter("xxx")` HTTP 參數提取。
  - 介面：`IJspExtractor`、實作：`JspExtractor`。
- **1d. SpecReportBuilder**：組裝 `MigrationSpec` 並輸出可審查報告。
  - 資料模型：`EndpointCandidate`（含狀態 `Resolved`/`Unresolved`）、`MigrationSpec`。
  - 實作 JSP → PB → DataWindow 對齊邏輯，標記繼承鏈缺口。
  - 輸出：`output/spec/report.md`、`output/spec/datawindows/*.json`、`output/spec/components/*.json`。
  - 介面：`ISpecReportBuilder`、實作：`SpecReportBuilder`。
- 命名規則：所有 `static readonly Regex` 欄位使用 `_` 前綴。
- 測試：`dotnet build`（成功）、`dotnet test`（13 passed）、`dotnet format --verify-no-changes`（成功）。

### 2026-02-23 Phase 1 Review 修正（Extractor / ReportBuilder）
- 目標與範圍：修正 `docs/REVIEW.md` 與 reviewer 指出的規格提取誤判與中斷風險，聚焦 `JspExtractor`、`SrdExtractor`、`SruExtractor`、`SpecReportBuilder`。
- 主要程式異動與決策：
  - `SktVegapunk.Core/Pipeline/Spec/JspExtractor.cs`
    - 僅匹配 `of_*/uf_*` component 呼叫，避免誤抓 `request.getParameter`、`session.getAttribute` 等 Servlet API。
    - 先解析 receiver 變數宣告，再以「型別名」回填 `ComponentName`（例如 `n_sign iJagComponent`）。
    - 參數定位改用 `Match.Index`，避免 `IndexOf` 多次匹配時錯位。
  - `SktVegapunk.Core/Pipeline/Spec/SrdExtractor.cs`
    - `column` 解析放寬為可選 `update=` / `updatewhereclause=` / `key=`，覆蓋實際 `.srd` 欄位定義。
    - `retrieve` 改為逐字元解析，支援 PBSELECT 的 `~"` 跳脫引號。
    - `arguments` 改為括號平衡掃描，修正只抓到第一個參數的問題。
  - `SktVegapunk.Core/Pipeline/Spec/SruExtractor.cs`
    - `prototype/function start` 正則修正為可正確匹配無回傳型別 `subroutine`。
    - routine 掃描前先移除 `forward prototypes` 區塊，避免把 prototype 誤當函式實作。
  - `SktVegapunk.Core/Pipeline/Spec/SpecReportBuilder.cs`
    - 重複 `ClassName` 改用分組 map，不再因 `ToDictionary` 重鍵直接拋例外。
    - 優先用「含目標 method 的 component」做對齊，降低同名 component 誤判。
    - JSON 輸出改走 `ITextFileStore`（不再直接 `File.WriteAllTextAsync`），符合 DIP 且可測試。
    - 移除偽非同步目錄建立，時間改注入 `TimeProvider`，報告輸出可重現。
- 新增測試（Phase 1 首批）：
  - `SktVegapunk.Tests/Pipeline/Spec/JspExtractorTests.cs`
  - `SktVegapunk.Tests/Pipeline/Spec/SrdExtractorTests.cs`
  - `SktVegapunk.Tests/Pipeline/Spec/SruExtractorTests.cs`
  - `SktVegapunk.Tests/Pipeline/Spec/SpecReportBuilderTests.cs`
- 驗證結果：
  - `dotnet test SktVegapunk.slnx /nr:false /m:1 /p:BuildInParallel=false /p:UseSharedCompilation=false`：成功（21 passed）。
  - `dotnet format SktVegapunk.slnx --verify-no-changes`：失敗（Restore operation failed，需在可完整 restore 的環境重跑）。
- 已知取捨與後續建議：
  - `JspExtractor` 目前以 `of_*/uf_*` 為 PB 方法命名慣例；若未來有其他前綴，需擴充匹配規則。
  - `SpecReportBuilder` 對同名 component 仍採「方法優先，其次首個」策略；若需更強一致性，建議後續加入命名空間/來源路徑權重。

## 本次範圍
- 僅後端 PoC。
- 僅單檔輸入（`.srw` / `.sru`）。
- 不含 RAG、不含前端 Vue 轉換、不含多代理並行調度。

## 核心架構變更

### 1) OpenRouter Client 重構（DIP）
- `SktVegapunk.Core/OpenRouterClient.cs`
- 變更重點：
  - 改為注入 `HttpClient`，移除內部 `new HttpClient()`。
  - `SendMessageAsync` 增加 `CancellationToken`。
  - 保留並統一設定 `Authorization`、`HTTP-Referer`、`X-Title`。

### 2) Pipeline 模組新增
- 新增資料模型與介面：
  - `PbEventBlock`
  - `MigrationRequest`, `MigrationResult`, `MigrationState`
  - `ICodeGenerator`, `IPbScriptExtractor`, `IPromptBuilder`
  - `IBuildValidator`, `IProcessRunner`, `ITextFileStore`
- 新增實作：
  - `PbScriptExtractor`：逐行狀態機提取 `event/on ... end event/end on` 區塊。
  - `PromptBuilder`：初始 prompt 與修復 prompt 組裝。
  - `OpenRouterCodeGenerator`：封裝模型呼叫。
  - `ProcessRunner`：執行子程序命令。
  - `DotnetBuildValidator`：執行 `dotnet build`，可選擇串接 `dotnet test`。
  - `FileTextStore`：檔案讀寫抽象。
  - `MigrationOrchestrator`：流程編排與重試控制。

### 3) Console 入口改造
- `SktVegapunk.Console/Program.cs`
- 新增 CLI 參數：
  - `--source <pb-file>`
  - `--output <generated-cs-file>`
  - `--target-project <project-or-sln>`
- 入口責任：
  - 讀取設定與 secrets。
  - 初始化依賴。
  - 呼叫 orchestrator，根據結果回傳 exit code。

### 4) 設定新增
- `SktVegapunk.Console/appsettings.json`
- 新增：
  - `Pipeline:MaxRetries`（預設 `3`）
  - `Pipeline:RunTestsAfterBuild`（預設 `false`）
  - `Pipeline:BuildConfiguration`（預設 `Debug`）

## 測試與品質

### 新增測試
- `SktVegapunk.Tests/Pipeline/PbScriptExtractorTests.cs`
  - 混合內容提取、多事件順序、無事件回空集合。
- `SktVegapunk.Tests/Pipeline/MigrationOrchestratorTests.cs`
  - 首次成功、先失敗後成功、達最大重試失敗。
- `SktVegapunk.Tests/OpenRouterClientTests.cs`
  - 成功回應解析、非成功狀態拋例外、Header 驗證。

### 移除測試
- 刪除空白測試：`SktVegapunk.Tests/UnitTest1.cs`

### 驗證結果
- `dotnet build SktVegapunk.slnx`：成功（0 warning / 0 error）
- `dotnet test SktVegapunk.slnx`：成功（8 passed）
- `dotnet format SktVegapunk.slnx --verify-no-changes`：成功

## 文件更新
- `README.md`
  - 新增 Pipeline 設定說明。
  - 更新 CLI 執行方式為帶參數模式。

## 已知取捨
- PB 解析目前採「結構化事件區塊提取」，非完整語法 AST。
- 驗證目標依賴外部 `.sln/.csproj`，本階段不自動建立新專案骨架。
- 狀態雖有 enum，但未導入事件追蹤或持久化狀態儲存。

## 建議下一步
1. 增加批次模式（資料夾掃描、多檔排程）。
2. 加入轉換結果與錯誤輸出的檔案化紀錄（audit trail）。
3. 導入 RAG（團隊範本、命名規範、API 樣板）降低輸出漂移。
