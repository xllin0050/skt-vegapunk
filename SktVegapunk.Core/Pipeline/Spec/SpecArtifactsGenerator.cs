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
        InteractionGraphAnalyzer interactionGraphAnalyzer)
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

        foreach (var path in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var extension = Path.GetExtension(path);
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

        if (warnings.Count > 0)
        {
            var warningsPath = Path.Combine(outputDirectory, "spec", "warnings.md");
            var warningsContent = string.Join(Environment.NewLine, warnings.Select(warning => $"- {warning}"));
            await _textFileStore.WriteAllTextAsync(warningsPath, warningsContent, cancellationToken);
        }

        return new SpecArtifactsGenerationResult(
            DataWindowCount: dataWindows.Count,
            ComponentCount: components.Count,
            JspInvocationCount: jspInvocations.Count,
            JspPrototypeCount: jspPrototypes.Count,
            WarningCount: warnings.Count,
            Warnings: warnings.AsReadOnly());
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

    private static bool IsSupportedSource(string extension) =>
        extension.Equals(".srd", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".sru", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".jsp", StringComparison.OrdinalIgnoreCase);

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
}
