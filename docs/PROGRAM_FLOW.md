# Flowchart

本文件說明 `SktVegapunk.Console/Program.cs` 如何串接 Core 函式庫與 .NET 內建 API。

---

## 整體架構總覽

程式支援兩種執行模式：

```
SktVegapunk.Console            SktVegapunk.Core            .NET BCL / 外部
─────────────────              ──────────────────          ───────────────
Program.cs
  ├─ TryParseOptions()
  ├─ ConfigurationBuilder      ←────────────────────────── Microsoft.Extensions.Configuration
  │
  ├─ [spec 模式 --spec-source]
  │    └─ SpecArtifactsGenerator ← SpecArtifactsGenerator.cs
  │         ├─ PbSourceNormalizer ← PbSourceNormalizer.cs
  │         ├─ SrdExtractor / SruExtractor / JspExtractor
  │         ├─ SpecReportBuilder
  │         ├─ SchemaExtractor   ← SchemaExtractor.cs
  │         ├─ SchemaReconciliationAnalyzer
  │         └─ EndpointDataWindowAnalyzer
  │
  └─ [migration 模式 --source]
       ├─ GitHubCopilotClient    ←── GitHubCopilotClient.cs
       ├─ CopilotCodeGenerator   ← CopilotCodeGenerator.cs
       └─ MigrationOrchestrator  ← MigrationOrchestrator.cs
            ├─ FileTextStore     ← FileTextStore.cs ──── File.ReadAllBytesAsync / WriteAllTextAsync
            ├─ PbSourceNormalizer
            ├─ PbScriptExtractor ← PbScriptExtractor.cs ── StringReader / StringBuilder
            ├─ PromptBuilder     ← PromptBuilder.cs ────── StringBuilder
            ├─ CopilotCodeGenerator
            └─ DotnetBuildValidator ← DotnetBuildValidator.cs
                 └─ ProcessRunner ← ProcessRunner.cs ─── System.Diagnostics.Process
```

---

## 詳細流程圖

```mermaid
---
config:
  flowchart:
    subGraphTitleMargin:
      bottom: 30
---
flowchart TD
    A([▶ dotnet run]) --> B

    subgraph CONSOLE["**SktVegapunk.Console** Program.cs"]
        B["TryParseOptions(args)<br>解析 --spec-source/--spec-output<br>或 --source/--output/--target-project"]
        B -->|解析失敗| ERR1["PrintUsage()<br>return 1"]
        B -->|--spec-source 模式| SPEC["new SpecArtifactsGenerator(...)<br>await generator.GenerateAsync(specSrc, specOut)"]
        SPEC --> SPEC_OK["Console 輸出計數<br>return 0"]
        B -->|--source 模式| C

        C["ConfigurationBuilder<br>① appsettings.json<br>② AddUserSecrets&lt;Program&gt;()<br>③ AddEnvironmentVariables()"]
        C --> D["讀取必要設定<br>Agent:SystemPrompt<br>Agent:ModelName<br>GitHubCopilot:CliPath<br>GitHubCopilot:GitHubToken (optional)<br>GitHubCopilot:WorkingDirectory (optional)<br>Pipeline:MaxRetries<br>Pipeline:RunTestsAfterBuild<br>Pipeline:BuildConfiguration"]
        D -->|設定缺失| ERR2["throw InvalidOperationException"]

        D --> E["await using new GitHubCopilotClient(...)<br>new CopilotCodeGenerator(client, modelName)<br>new MigrationOrchestrator(...)"]
        E --> F["建立 MigrationRequest<br>{ SourceFilePath, OutputFilePath,<br>TargetPath, SystemPrompt,<br>MaxRetries, RunTestsAfterBuild,<br>BuildConfiguration }"]
        F --> G["await orchestrator.RunAsync(request)"]
    end

    subgraph ORCH["**SktVegapunk.Core** MigrationOrchestrator"]
        G --> H

        H["★ Preprocessing<br>FileTextStore.ReadAllBytesAsync(sourcePath)<br>→ PbSourceNormalizer.Normalize(bytes, path)<br>→ 自動偵測 BOM / UTF-16LE 解碼"]
        H --> I["PbScriptExtractor.Extract(normalizedText)<br>→ StringReader 逐行掃描<br>→ 找出 on event … end on 區塊"]
        I -->|找不到事件區塊| RET_FAIL0["return MigrationResult<br>FinalState = Failed<br>'未找到可轉換的事件邏輯區塊'"]

        I -->|取得 eventBlocks| J["PromptBuilder.BuildInitialPrompt(eventBlocks)<br>→ StringBuilder 組裝初始 Prompt"]

        J --> LOOP_START(["🔁 for attempt = 1 → MaxRetries"])

        LOOP_START --> K["★ Generating<br>CopilotCodeGenerator.GenerateAsync(systemPrompt, prompt)<br>→ GitHubCopilotClient.SendMessageAsync(model, system, user)<br>→ CopilotClient.CreateSessionAsync(... )<br>→ session.SendAndWaitAsync(...)<br>→ 取得 generatedCode"]

        K -->|回傳空內容| RET_FAIL1["return MigrationResult<br>FinalState = Failed<br>'模型回傳空內容'"]
        K -->|有內容| L

        L["FileTextStore.WriteAllTextAsync(outputPath, generatedCode)<br>→ Directory.CreateDirectory (BCL)<br>→ File.WriteAllTextAsync (BCL)"]

        L --> M["★ Validating<br>DotnetBuildValidator.ValidateAsync()<br>→ ProcessRunner.RunAsync('dotnet build')<br>→ System.Diagnostics.Process (BCL)"]

        M -->|RunTestsAfterBuild = true| N["ProcessRunner.RunAsync('dotnet test')<br>→ System.Diagnostics.Process (BCL)"]
        M -->|RunTestsAfterBuild = false| PASS_CHECK

        N --> PASS_CHECK{{"驗證通過？"}}

        PASS_CHECK -->|✅ 通過| RET_OK["return MigrationResult<br>FinalState = Completed<br>Attempts = attempt"]
        PASS_CHECK -->|❌ 失敗 & 還有次數| O["★ Repairing<br>PromptBuilder.BuildRepairPrompt(<br>  initialPrompt,<br>  generatedCode,<br>  validationOutput)<br>→ StringBuilder 組裝修復 Prompt"]
        O --> LOOP_START

        PASS_CHECK -->|❌ 失敗 & 次數耗盡| RET_FAIL2["return MigrationResult<br>FinalState = Failed<br>Attempts = MaxRetries"]
    end

    subgraph RESULT["回到 Program.cs：處理結果"]
        RET_OK --> SUCCESS["Console.WriteLine('轉換成功')<br>return 0"]
        RET_FAIL0 & RET_FAIL1 & RET_FAIL2 --> FAIL["Console.WriteLine(FailureReason)<br>return 2"]
    end

    G -->|HttpRequestException| ERR3["Console.WriteLine('[網路/API請求錯誤]')<br>return 3"]
    G -->|Exception| ERR4["Console.WriteLine('[系統錯誤]')<br>return 4"]
```

---

## 各元件職責對照表

| 元件 | 所在檔案 | 職責 | 依賴的 .NET API |
|------|----------|------|----------------|
| `Program.Main` | `Console/Program.cs` | 入口點：解析參數、組裝依賴、呼叫 Orchestrator | `Console`, `Task`, `ConfigurationBuilder` |
| `TryParseOptions` | `Console/Program.cs` | 解析 CLI 參數（spec 模式：`--spec-source` / `--spec-output`；migration 模式：`--source` / `--output` / `--target-project`） | — |
| `ConfigurationBuilder` | .NET BCL | 分層載入設定（json → secrets → 環境變數） | `Microsoft.Extensions.Configuration` |
| `GitHubCopilotClient` | `Core/GitHubCopilotClient.cs` | 封裝 GitHub Copilot SDK session 建立與回應等待 | `GitHub.Copilot.SDK`, `CopilotClient`, `SendAndWaitAsync` |
| `CopilotCodeGenerator` | `Core/Pipeline/CopilotCodeGenerator.cs` | 實作 `ICodeGenerator`，委派給 `GitHubCopilotClient` | — |
| `PbSourceNormalizer` | `Core/Pipeline/PbSourceNormalizer.cs` | 實作 `ISourceNormalizer`，偵測 BOM 並以 UTF-16LE 解碼 PB 原始碼 | — |
| `FileTextStore` | `Core/Pipeline/FileTextStore.cs` | 實作 `ITextFileStore`，讀寫磁碟檔案 | `File.ReadAllBytesAsync`, `File.WriteAllTextAsync`, `Directory.CreateDirectory` |
| `PbScriptExtractor` | `Core/Pipeline/PbScriptExtractor.cs` | 實作 `IPbScriptExtractor`，從 PB 原始碼提取事件區塊 | `StringReader`, `StringBuilder` |
| `PromptBuilder` | `Core/Pipeline/PromptBuilder.cs` | 實作 `IPromptBuilder`，組裝初始與修復 Prompt | `StringBuilder` |
| `DotnetBuildValidator` | `Core/Pipeline/DotnetBuildValidator.cs` | 實作 `IBuildValidator`，呼叫 `dotnet build/test` | — |
| `ProcessRunner` | `Core/Pipeline/ProcessRunner.cs` | 實作 `IProcessRunner`，執行外部 CLI 程序 | `System.Diagnostics.Process` |
| `MigrationOrchestrator` | `Core/Pipeline/MigrationOrchestrator.cs` | 實作 `IMigrationOrchestrator`，協調整條 Pipeline | — |

---

## 狀態機轉換

`MigrationState` 描述 Orchestrator 每個階段的狀態：

```
Preprocessing → Generating → Validating
                                  │
                  ┌───────────────┤
                  ↓               ↓
              Repairing ──→  Completed
              (重新 Generating)
                  │
                  ↓（次數耗盡）
                Failed
```

---

## Exit Code 對照

| Exit Code | 意義 |
|-----------|------|
| `0` | 轉換成功並通過驗證 |
| `1` | CLI 參數解析失敗 |
| `2` | 轉換失敗（模型或驗證問題） |
| `3` | 網路 / API 請求錯誤 |
| `4` | 其他系統錯誤 |
