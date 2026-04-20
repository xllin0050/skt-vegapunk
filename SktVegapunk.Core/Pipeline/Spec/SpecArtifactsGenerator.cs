using SktVegapunk.Core.Pipeline;
using System.Text;
using System.Text.Json;

namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// 從來源資料夾提取規格報告與中介資料。
/// </summary>
public sealed class SpecArtifactsGenerator
{
    private readonly ITextFileStore _textFileStore;
    private readonly ISourceNormalizer _sourceNormalizer;
    private readonly ISrdExtractor _srdExtractor;
    private readonly ISruExtractor _sruExtractor;
    private readonly IJspExtractor _jspExtractor;
    private readonly JspPrototypeExtractor _jspPrototypeExtractor;
    private readonly ISpecReportBuilder _specReportBuilder;
    private readonly UnresolvedEndpointAnalyzer _unresolvedEndpointAnalyzer;
    private readonly PageFlowAnalyzer _pageFlowAnalyzer;
    private readonly GenerationPhasePlanner _generationPhasePlanner;
    private readonly RequestBindingAnalyzer _requestBindingAnalyzer;
    private readonly ResponseClassificationAnalyzer _responseClassificationAnalyzer;
    private readonly InteractionGraphAnalyzer _interactionGraphAnalyzer;
    private readonly ISchemaExtractor _schemaExtractor;
    private readonly SchemaReconciliationAnalyzer _schemaReconciliationAnalyzer;
    private readonly EndpointDataWindowAnalyzer _endpointDataWindowAnalyzer;
    private readonly UnresolvedEndpointInferrer? _unresolvedEndpointInferrer;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SpecArtifactsGenerator(
        ITextFileStore textFileStore,
        ISourceNormalizer sourceNormalizer,
        ISrdExtractor srdExtractor,
        ISruExtractor sruExtractor,
        IJspExtractor jspExtractor,
        JspPrototypeExtractor jspPrototypeExtractor,
        ISpecReportBuilder specReportBuilder,
        UnresolvedEndpointAnalyzer unresolvedEndpointAnalyzer,
        PageFlowAnalyzer pageFlowAnalyzer,
        GenerationPhasePlanner generationPhasePlanner,
        RequestBindingAnalyzer requestBindingAnalyzer,
        ResponseClassificationAnalyzer responseClassificationAnalyzer,
        InteractionGraphAnalyzer interactionGraphAnalyzer,
        ISchemaExtractor? schemaExtractor = null,
        SchemaReconciliationAnalyzer? schemaReconciliationAnalyzer = null,
        EndpointDataWindowAnalyzer? endpointDataWindowAnalyzer = null,
        UnresolvedEndpointInferrer? unresolvedEndpointInferrer = null)
    {
        ArgumentNullException.ThrowIfNull(textFileStore);
        ArgumentNullException.ThrowIfNull(sourceNormalizer);
        ArgumentNullException.ThrowIfNull(srdExtractor);
        ArgumentNullException.ThrowIfNull(sruExtractor);
        ArgumentNullException.ThrowIfNull(jspExtractor);
        ArgumentNullException.ThrowIfNull(jspPrototypeExtractor);
        ArgumentNullException.ThrowIfNull(specReportBuilder);
        ArgumentNullException.ThrowIfNull(unresolvedEndpointAnalyzer);
        ArgumentNullException.ThrowIfNull(pageFlowAnalyzer);
        ArgumentNullException.ThrowIfNull(generationPhasePlanner);
        ArgumentNullException.ThrowIfNull(requestBindingAnalyzer);
        ArgumentNullException.ThrowIfNull(responseClassificationAnalyzer);
        ArgumentNullException.ThrowIfNull(interactionGraphAnalyzer);

        _textFileStore = textFileStore;
        _sourceNormalizer = sourceNormalizer;
        _srdExtractor = srdExtractor;
        _sruExtractor = sruExtractor;
        _jspExtractor = jspExtractor;
        _jspPrototypeExtractor = jspPrototypeExtractor;
        _specReportBuilder = specReportBuilder;
        _unresolvedEndpointAnalyzer = unresolvedEndpointAnalyzer;
        _pageFlowAnalyzer = pageFlowAnalyzer;
        _generationPhasePlanner = generationPhasePlanner;
        _requestBindingAnalyzer = requestBindingAnalyzer;
        _responseClassificationAnalyzer = responseClassificationAnalyzer;
        _interactionGraphAnalyzer = interactionGraphAnalyzer;
        _schemaExtractor = schemaExtractor ?? new SchemaExtractor();
        _schemaReconciliationAnalyzer = schemaReconciliationAnalyzer ?? new SchemaReconciliationAnalyzer();
        _endpointDataWindowAnalyzer = endpointDataWindowAnalyzer ?? new EndpointDataWindowAnalyzer();
        _unresolvedEndpointInferrer = unresolvedEndpointInferrer;
    }

    /// <summary>
    /// 掃描來源資料夾並輸出規格報告與中介 JSON。
    /// </summary>
    public async Task<SpecArtifactsGenerationResult> GenerateAsync(
        string sourceDirectory,
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"找不到來源資料夾: {sourceDirectory}");
        }

        var rootPath = Path.GetFullPath(sourceDirectory);
        var dataWindows = new List<SrdSpec>();
        var components = new List<SruSpec>();
        var jspInvocations = new List<JspInvocation>();
        var jspPrototypes = new List<JspPrototypeArtifact>();
        var jspSources = new List<JspSourceArtifact>();
        var warnings = new List<string>();
        var schemaDdlTexts = new List<string>();

        foreach (var path in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var extension = Path.GetExtension(path);

            // 收集 schema DDL 檔案（位於 schema/ 子目錄的 .sql 檔）
            if (extension.Equals(".sql", StringComparison.OrdinalIgnoreCase)
                && IsInSchemaDirectory(rootPath, path))
            {
                // SQL DDL 為 ISO-8859-1 編碼
                var sqlBytes = await _textFileStore.ReadAllBytesAsync(path, cancellationToken);
                schemaDdlTexts.Add(System.Text.Encoding.Latin1.GetString(sqlBytes));
                continue;
            }

            if (!IsSupportedSource(extension))
            {
                continue;
            }

            if (extension.Equals(".jsp", StringComparison.OrdinalIgnoreCase))
            {
                var jspText = await _textFileStore.ReadAllTextAsync(path, cancellationToken);
                var normalizedRelativePath = GetRelativePath(rootPath, path);
                var jspInvocation = _jspExtractor.Extract(jspText) with
                {
                    JspFileName = normalizedRelativePath
                };
                var jspPrototype = _jspPrototypeExtractor.Extract(jspText) with
                {
                    JspFileName = normalizedRelativePath
                };

                if (!string.IsNullOrWhiteSpace(jspInvocation.ComponentName) && !string.IsNullOrWhiteSpace(jspInvocation.MethodName))
                {
                    jspInvocations.Add(jspInvocation);
                }

                jspPrototypes.Add(jspPrototype);
                jspSources.Add(new JspSourceArtifact(normalizedRelativePath, jspText, jspInvocation, jspPrototype));

                continue;
            }

            var rawBytes = await _textFileStore.ReadAllBytesAsync(path, cancellationToken);
            var sourceArtifact = _sourceNormalizer.Normalize(rawBytes, path);
            foreach (var warning in sourceArtifact.Warnings)
            {
                warnings.Add($"{GetRelativePath(rootPath, path)}: {warning}");
            }

            if (string.IsNullOrWhiteSpace(sourceArtifact.NormalizedText))
            {
                continue;
            }

            var relativePath = GetRelativePath(rootPath, path);
            if (extension.Equals(".srd", StringComparison.OrdinalIgnoreCase))
            {
                dataWindows.Add(_srdExtractor.Extract(sourceArtifact.NormalizedText) with
                {
                    FileName = relativePath
                });
                continue;
            }

            if (extension.Equals(".sru", StringComparison.OrdinalIgnoreCase))
            {
                components.Add(_sruExtractor.Extract(sourceArtifact.NormalizedText) with
                {
                    FileName = relativePath
                });
            }
        }

        var migrationSpec = _specReportBuilder.Build(dataWindows, components, jspInvocations);
        await _specReportBuilder.WriteReportAsync(migrationSpec, outputDirectory, cancellationToken);
        await WriteJspPrototypeArtifactsAsync(outputDirectory, jspPrototypes, cancellationToken);
        var unresolvedFindings = await WriteUnresolvedEndpointCausesAsync(outputDirectory, migrationSpec, rootPath, cancellationToken);
        var pageFlowGraph = await WritePageFlowArtifactsAsync(outputDirectory, jspPrototypes, migrationSpec, cancellationToken);
        var requestBindings = await WriteRequestBindingArtifactsAsync(outputDirectory, migrationSpec, jspSources, cancellationToken);
        var responseClassifications = await WriteResponseClassificationArtifactsAsync(outputDirectory, migrationSpec, jspSources, cancellationToken);
        await WriteControlInventoryArtifactsAsync(outputDirectory, jspPrototypes, cancellationToken);
        await WritePayloadMappingArtifactsAsync(outputDirectory, requestBindings, cancellationToken);
        await WriteInteractionGraphArtifactsAsync(outputDirectory, jspPrototypes, cancellationToken);
        await WriteGenerationPhasePlanAsync(outputDirectory, migrationSpec, jspPrototypes, pageFlowGraph, unresolvedFindings, requestBindings, responseClassifications, cancellationToken);
        await WriteEndpointDataWindowMapAsync(outputDirectory, migrationSpec, components, cancellationToken);

        // 若有 schema DDL，執行 schema 提取與 reconciliation
        var schemaArtifacts = default(SchemaArtifacts);
        if (schemaDdlTexts.Count > 0)
        {
            var combinedDdl = string.Join("\n", schemaDdlTexts);
            schemaArtifacts = _schemaExtractor.Extract(combinedDdl);
            await WriteSchemaArtifactsAsync(outputDirectory, schemaArtifacts, cancellationToken);
            await WriteSchemaReconciliationAsync(outputDirectory, dataWindows, schemaArtifacts.Tables, cancellationToken);
        }

        // 若有 LLM 推導器且存在 unresolved endpoint，嘗試從 JSP + schema 補齊規格
        var inferredCount = 0;
        if (_unresolvedEndpointInferrer is not null && unresolvedFindings.Count > 0)
        {
            var inferredSpecs = await _unresolvedEndpointInferrer.InferAndWriteAsync(
                unresolvedFindings,
                jspSources,
                schemaArtifacts,
                outputDirectory,
                cancellationToken);
            inferredCount = inferredSpecs.Count(s => s.InferenceSucceeded);
        }

        if (warnings.Count > 0)
        {
            var warningsPath = Path.Combine(outputDirectory, "spec", "warnings.md");
            var warningsContent = string.Join(Environment.NewLine, warnings.Select(warning => $"- {warning}"));
            await _textFileStore.WriteAllTextAsync(warningsPath, warningsContent, cancellationToken);
        }

        await WriteSpecIndexAsync(outputDirectory, schemaDdlTexts.Count > 0, warnings.Count > 0, cancellationToken);

        return new SpecArtifactsGenerationResult(
            DataWindowCount: dataWindows.Count,
            ComponentCount: components.Count,
            JspInvocationCount: jspInvocations.Count,
            JspPrototypeCount: jspPrototypes.Count,
            WarningCount: warnings.Count,
            Warnings: warnings.AsReadOnly(),
            SchemaTableCount: schemaArtifacts?.Tables.Count ?? 0,
            SchemaTriggerCount: schemaArtifacts?.Triggers.Count ?? 0,
            InferredEndpointCount: inferredCount);
    }

    private async Task WriteJspPrototypeArtifactsAsync(
        string outputDirectory,
        IReadOnlyList<JspPrototypeArtifact> jspPrototypes,
        CancellationToken cancellationToken)
    {
        var tasks = new List<Task>();
        foreach (var prototype in jspPrototypes)
        {
            var basePath = Path.Combine(outputDirectory, "spec", "jsp", BuildArtifactBaseRelativePath(prototype.JspFileName));
            tasks.Add(_textFileStore.WriteAllTextAsync($"{basePath}.json", JsonSerializer.Serialize(prototype, _jsonOptions), cancellationToken));
            tasks.Add(_textFileStore.WriteAllTextAsync($"{basePath}.html", prototype.HtmlPrototype, cancellationToken));
            tasks.Add(_textFileStore.WriteAllTextAsync($"{basePath}.js", prototype.JavaScriptPrototype, cancellationToken));
            tasks.Add(_textFileStore.WriteAllTextAsync($"{basePath}.css", prototype.CssPrototype, cancellationToken));
        }

        await Task.WhenAll(tasks);
    }

    private async Task<IReadOnlyList<UnresolvedEndpointFinding>> WriteUnresolvedEndpointCausesAsync(
        string outputDirectory,
        MigrationSpec migrationSpec,
        string sourceDirectory,
        CancellationToken cancellationToken)
    {
        var findings = _unresolvedEndpointAnalyzer.Analyze(migrationSpec, sourceDirectory);
        var markdown = _unresolvedEndpointAnalyzer.GenerateMarkdown(findings);
        var path = Path.Combine(outputDirectory, "spec", "unresolved-causes.md");
        await _textFileStore.WriteAllTextAsync(path, markdown, cancellationToken);
        return findings;
    }

    private async Task<PageFlowGraph> WritePageFlowArtifactsAsync(
        string outputDirectory,
        IReadOnlyList<JspPrototypeArtifact> jspPrototypes,
        MigrationSpec migrationSpec,
        CancellationToken cancellationToken)
    {
        var graph = _pageFlowAnalyzer.Analyze(jspPrototypes, migrationSpec);
        var markdownPath = Path.Combine(outputDirectory, "spec", "page-flow.md");
        var jsonPath = Path.Combine(outputDirectory, "spec", "page-flow.json");

        await _textFileStore.WriteAllTextAsync(markdownPath, _pageFlowAnalyzer.GenerateMarkdown(graph), cancellationToken);
        await _textFileStore.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(graph, _jsonOptions), cancellationToken);
        return graph;
    }

    private async Task WriteGenerationPhasePlanAsync(
        string outputDirectory,
        MigrationSpec migrationSpec,
        IReadOnlyList<JspPrototypeArtifact> jspPrototypes,
        PageFlowGraph pageFlowGraph,
        IReadOnlyList<UnresolvedEndpointFinding> unresolvedFindings,
        IReadOnlyList<RequestBindingArtifact> requestBindings,
        IReadOnlyList<ResponseClassificationArtifact> responseClassifications,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(outputDirectory, "spec", "generation-phase-plan.md");
        var markdown = _generationPhasePlanner.GenerateMarkdown(
            migrationSpec,
            jspPrototypes,
            pageFlowGraph,
            unresolvedFindings,
            requestBindings,
            responseClassifications);

        await _textFileStore.WriteAllTextAsync(path, markdown, cancellationToken);
    }

    private async Task<IReadOnlyList<RequestBindingArtifact>> WriteRequestBindingArtifactsAsync(
        string outputDirectory,
        MigrationSpec migrationSpec,
        IReadOnlyList<JspSourceArtifact> jspSources,
        CancellationToken cancellationToken)
    {
        var bindings = _requestBindingAnalyzer.Analyze(migrationSpec, jspSources);
        var markdownPath = Path.Combine(outputDirectory, "spec", "request-bindings.md");
        var jsonPath = Path.Combine(outputDirectory, "spec", "request-bindings.json");

        await _textFileStore.WriteAllTextAsync(markdownPath, _requestBindingAnalyzer.GenerateMarkdown(bindings), cancellationToken);
        await _textFileStore.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(bindings, _jsonOptions), cancellationToken);
        return bindings;
    }

    private async Task WriteControlInventoryArtifactsAsync(
        string outputDirectory,
        IReadOnlyList<JspPrototypeArtifact> jspPrototypes,
        CancellationToken cancellationToken)
    {
        var artifacts = jspPrototypes
            .Select(prototype => new ControlInventoryArtifact(prototype.JspFileName, prototype.Controls))
            .ToList();
        var markdown = new StringBuilder()
            .AppendLine("# Control Inventory")
            .AppendLine()
            .AppendLine($"總計: {artifacts.Sum(artifact => artifact.Controls.Count)} 個控制項")
            .AppendLine()
            .ToString();

        var builder = new StringBuilder(markdown);
        foreach (var artifact in artifacts)
        {
            builder.AppendLine($"## {artifact.JspSource}");
            builder.AppendLine();
            if (artifact.Controls.Count == 0)
            {
                builder.AppendLine("- 無可辨識控制項");
                builder.AppendLine();
                continue;
            }

            builder.AppendLine("| Tag | Type | Id | Name | Form | OnClick |");
            builder.AppendLine("|-----|------|----|------|------|---------|");
            foreach (var control in artifact.Controls)
            {
                builder.AppendLine($"| {control.TagName} | {control.Type ?? string.Empty} | {control.Id ?? string.Empty} | {control.Name ?? string.Empty} | {control.FormKey ?? string.Empty} | {control.OnClickHandler ?? string.Empty} |");
            }

            builder.AppendLine();
        }

        await _textFileStore.WriteAllTextAsync(Path.Combine(outputDirectory, "spec", "control-inventory.md"), builder.ToString().Trim(), cancellationToken);
        await _textFileStore.WriteAllTextAsync(Path.Combine(outputDirectory, "spec", "control-inventory.json"), JsonSerializer.Serialize(artifacts, _jsonOptions), cancellationToken);
    }

    private async Task WritePayloadMappingArtifactsAsync(
        string outputDirectory,
        IReadOnlyList<RequestBindingArtifact> requestBindings,
        CancellationToken cancellationToken)
    {
        var artifacts = requestBindings
            .SelectMany(binding => binding.OutgoingRequests.Select(request => new PayloadMappingArtifact(
                binding.JspSource,
                request.Kind,
                request.HttpMethod,
                request.Target,
                request.PayloadExpression,
                request.PayloadFields)))
            .ToList();

        var builder = new StringBuilder();
        builder.AppendLine("# Payload Mappings");
        builder.AppendLine();
        builder.AppendLine($"總計: {artifacts.Count} 個 outgoing request");
        builder.AppendLine();

        foreach (var artifact in artifacts)
        {
            builder.AppendLine($"## {artifact.JspSource} -> {artifact.Target}");
            builder.AppendLine();
            builder.AppendLine($"- Kind: {artifact.Kind}");
            builder.AppendLine($"- Method: {artifact.HttpMethod}");
            builder.AppendLine($"- Payload: {artifact.PayloadExpression}");
            if (artifact.Fields.Count == 0)
            {
                builder.AppendLine("- Fields: 無法解析");
                builder.AppendLine();
                continue;
            }

            builder.AppendLine("| Field | Source | Expression | Control |");
            builder.AppendLine("|-------|--------|------------|---------|");
            foreach (var field in artifact.Fields)
            {
                builder.AppendLine($"| {field.Name} | {field.SourceKind} | {field.SourceExpression} | {field.SourceControl ?? string.Empty} |");
            }

            builder.AppendLine();
        }

        await _textFileStore.WriteAllTextAsync(Path.Combine(outputDirectory, "spec", "payload-mappings.md"), builder.ToString().Trim(), cancellationToken);
        await _textFileStore.WriteAllTextAsync(Path.Combine(outputDirectory, "spec", "payload-mappings.json"), JsonSerializer.Serialize(artifacts, _jsonOptions), cancellationToken);
    }

    private async Task WriteInteractionGraphArtifactsAsync(
        string outputDirectory,
        IReadOnlyList<JspPrototypeArtifact> jspPrototypes,
        CancellationToken cancellationToken)
    {
        var graph = _interactionGraphAnalyzer.Analyze(jspPrototypes);
        await _textFileStore.WriteAllTextAsync(Path.Combine(outputDirectory, "spec", "interaction-graph.md"), _interactionGraphAnalyzer.GenerateMarkdown(graph), cancellationToken);
        await _textFileStore.WriteAllTextAsync(Path.Combine(outputDirectory, "spec", "interaction-graph.json"), JsonSerializer.Serialize(graph, _jsonOptions), cancellationToken);
    }

    private async Task<IReadOnlyList<ResponseClassificationArtifact>> WriteResponseClassificationArtifactsAsync(
        string outputDirectory,
        MigrationSpec migrationSpec,
        IReadOnlyList<JspSourceArtifact> jspSources,
        CancellationToken cancellationToken)
    {
        var classifications = _responseClassificationAnalyzer.Analyze(migrationSpec, jspSources);
        var markdownPath = Path.Combine(outputDirectory, "spec", "response-classifications.md");
        var jsonPath = Path.Combine(outputDirectory, "spec", "response-classifications.json");

        await _textFileStore.WriteAllTextAsync(markdownPath, _responseClassificationAnalyzer.GenerateMarkdown(classifications), cancellationToken);
        await _textFileStore.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(classifications, _jsonOptions), cancellationToken);
        return classifications;
    }

    private async Task WriteSpecIndexAsync(
        string outputDirectory,
        bool hasSchemaArtifacts,
        bool hasWarnings,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(outputDirectory, "spec", "INDEX.md");
        var builder = new StringBuilder();

        builder.AppendLine("# Spec Artifact Index");
        builder.AppendLine();
        builder.AppendLine("這份目錄用來快速理解 `spec/` 下每個檔案與子目錄的用途，方便判斷應該先讀哪一份 artifact。");
        builder.AppendLine();
        builder.AppendLine("## 核心報告");
        builder.AppendLine();
        builder.AppendLine("- `report.md`：規格盤點總報告，摘要列出 endpoint、DataWindow、Component 與 unresolved methods。");
        builder.AppendLine("- `generation-phase-plan.md`：進入 generation phase 前的執行計畫，說明目前可生成範圍、stub 策略與前後端最低門檻。");
        builder.AppendLine("- `unresolved-causes.md`：未解析 endpoint 的根因分析，幫助決定哪些需要人工補件。");
        builder.AppendLine("- `endpoint-datawindow-map.md` / `endpoint-datawindow-map.json`：endpoint 對應到 DataWindow、component method 與資料表的交叉索引。");
        builder.AppendLine();
        builder.AppendLine("## 前端相關");
        builder.AppendLine();
        builder.AppendLine("- `jsp/`：每個 JSP 拆出的原型檔。");
        builder.AppendLine("- `jsp/*.json`：頁面結構化摘要，包含 form、control、event 等資訊。");
        builder.AppendLine("- `jsp/*.html`：HTML 骨架原型。");
        builder.AppendLine("- `jsp/*.js`：JavaScript 原型。");
        builder.AppendLine("- `jsp/*.css`：CSS 原型。");
        builder.AppendLine("- `control-inventory.md` / `control-inventory.json`：控制項清單，適合用來盤點欄位、按鈕與 form 關係。");
        builder.AppendLine("- `page-flow.md` / `page-flow.json`：頁面導頁、submit、ajax、popup 與 component call 的流向。");
        builder.AppendLine("- `interaction-graph.md` / `interaction-graph.json`：UI 事件到行為的互動鏈，用於回推前端 handler 邏輯。");
        builder.AppendLine("- `payload-mappings.md` / `payload-mappings.json`：前端送出 payload 的欄位映射，用來生成 API client 與表單 state。");
        builder.AppendLine();
        builder.AppendLine("## 後端相關");
        builder.AppendLine();
        builder.AppendLine("- `request-bindings.md` / `request-bindings.json`：JSP component call 對應的 request 參數、來源欄位與 outgoing request。");
        builder.AppendLine("- `response-classifications.md` / `response-classifications.json`：endpoint 回應型態分類，例如 `json/html/file/script-redirect/text`。");
        builder.AppendLine("- `datawindows/`：每個 `.srd` 的結構化 JSON，包含 SQL、資料表、欄位、參數。");
        builder.AppendLine("- `components/`：每個 `.sru` 的結構化 JSON，包含 prototype、routine、event block 與 DataWindow 引用。");

        if (hasSchemaArtifacts)
        {
            builder.AppendLine();
            builder.AppendLine("## Schema 相關");
            builder.AppendLine();
            builder.AppendLine("- `schema/tables/*.json`：資料表、欄位、索引與 trigger 的結構化輸出。");
            builder.AppendLine("- `schema/triggers/*.json`：trigger 的結構化輸出。");
            builder.AppendLine("- `schema/relationships.json`：外鍵關聯總表。");
            builder.AppendLine("- `schema/indexes.json`：獨立 index 清單。");
            builder.AppendLine("- `schema-reconciliation.md` / `schema-reconciliation.json`：DataWindow 欄位與 DB schema 的差異比對報告。");
        }

        if (hasWarnings)
        {
            builder.AppendLine();
            builder.AppendLine("## 其他");
            builder.AppendLine();
            builder.AppendLine("- `warnings.md`：編碼正規化或提取時的警告，出現時應優先檢查來源檔品質。");
        }

        builder.AppendLine();
        builder.AppendLine("## 建議閱讀順序");
        builder.AppendLine();
        builder.AppendLine("1. 先讀 `report.md`，確認整體盤點結果。");
        builder.AppendLine("2. 再讀 `unresolved-causes.md` 與 `generation-phase-plan.md`，確認是否能進 generation phase。");
        builder.AppendLine("3. 後端實作優先看 `request-bindings`、`response-classifications`、`endpoint-datawindow-map`、`datawindows/`、`components/`。");
        builder.AppendLine("4. 前端實作優先看 `jsp/`、`control-inventory`、`page-flow`、`interaction-graph`、`payload-mappings`。");

        await _textFileStore.WriteAllTextAsync(path, builder.ToString().Trim(), cancellationToken);
    }

    private static bool IsSupportedSource(string extension) =>
        extension.Equals(".srd", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".sru", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".jsp", StringComparison.OrdinalIgnoreCase);

    private static bool IsInSchemaDirectory(string rootPath, string filePath)
    {
        var relative = Path.GetRelativePath(rootPath, filePath);
        var firstSegment = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return firstSegment.Equals("schema", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetRelativePath(string rootPath, string fullPath) =>
        Path.GetRelativePath(rootPath, fullPath)
            .Replace(Path.DirectorySeparatorChar, '/');

    private static string BuildArtifactBaseRelativePath(string fileName)
    {
        var normalizedPath = fileName
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            throw new ArgumentException("FileName 不可為空白。", nameof(fileName));
        }

        return Path.Combine(
            Path.GetDirectoryName(normalizedPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(normalizedPath));
    }

    private async Task WriteSchemaArtifactsAsync(
        string outputDirectory,
        SchemaArtifacts schema,
        CancellationToken cancellationToken)
    {
        var tasks = new List<Task>();

        // 每張表一個 JSON
        foreach (var table in schema.Tables)
        {
            var tablePath = Path.Combine(outputDirectory, "spec", "schema", "tables", $"{table.TableName}.json");
            tasks.Add(_textFileStore.WriteAllTextAsync(tablePath, JsonSerializer.Serialize(table, _jsonOptions), cancellationToken));
        }

        // 每個 trigger 一個 JSON
        foreach (var trigger in schema.Triggers)
        {
            var triggerPath = Path.Combine(outputDirectory, "spec", "schema", "triggers", $"{trigger.TriggerName}.json");
            tasks.Add(_textFileStore.WriteAllTextAsync(triggerPath, JsonSerializer.Serialize(trigger, _jsonOptions), cancellationToken));
        }

        // FK 關聯總表（從各 table 收集）
        var allForeignKeys = schema.Tables
            .SelectMany(t => t.ForeignKeys.Select(fk => new
            {
                sourceTable = t.TableName,
                fk.Columns,
                fk.ReferencedTable,
                fk.ReferencedColumns,
                fk.OnDelete
            }))
            .ToList();

        tasks.Add(_textFileStore.WriteAllTextAsync(
            Path.Combine(outputDirectory, "spec", "schema", "relationships.json"),
            JsonSerializer.Serialize(allForeignKeys, _jsonOptions),
            cancellationToken));

        // 索引總表
        tasks.Add(_textFileStore.WriteAllTextAsync(
            Path.Combine(outputDirectory, "spec", "schema", "indexes.json"),
            JsonSerializer.Serialize(schema.StandaloneIndexes, _jsonOptions),
            cancellationToken));

        await Task.WhenAll(tasks);
    }

    private async Task WriteSchemaReconciliationAsync(
        string outputDirectory,
        IReadOnlyList<SrdSpec> dataWindows,
        IReadOnlyList<SchemaTableSpec> schemaTables,
        CancellationToken cancellationToken)
    {
        var entries = _schemaReconciliationAnalyzer.Analyze(dataWindows, schemaTables);

        await Task.WhenAll(
            _textFileStore.WriteAllTextAsync(
                Path.Combine(outputDirectory, "spec", "schema-reconciliation.md"),
                _schemaReconciliationAnalyzer.GenerateMarkdown(entries),
                cancellationToken),
            _textFileStore.WriteAllTextAsync(
                Path.Combine(outputDirectory, "spec", "schema-reconciliation.json"),
                JsonSerializer.Serialize(entries, _jsonOptions),
                cancellationToken));
    }

    private async Task WriteEndpointDataWindowMapAsync(
        string outputDirectory,
        MigrationSpec migrationSpec,
        IReadOnlyList<SruSpec> components,
        CancellationToken cancellationToken)
    {
        var entries = _endpointDataWindowAnalyzer.Analyze(migrationSpec, components);

        await Task.WhenAll(
            _textFileStore.WriteAllTextAsync(
                Path.Combine(outputDirectory, "spec", "endpoint-datawindow-map.md"),
                _endpointDataWindowAnalyzer.GenerateMarkdown(entries),
                cancellationToken),
            _textFileStore.WriteAllTextAsync(
                Path.Combine(outputDirectory, "spec", "endpoint-datawindow-map.json"),
                JsonSerializer.Serialize(entries, _jsonOptions),
                cancellationToken));
    }
}
