using SktVegapunk.Core.Pipeline;
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
        PageFlowAnalyzer pageFlowAnalyzer)
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

        _textFileStore = textFileStore;
        _sourceNormalizer = sourceNormalizer;
        _srdExtractor = srdExtractor;
        _sruExtractor = sruExtractor;
        _jspExtractor = jspExtractor;
        _jspPrototypeExtractor = jspPrototypeExtractor;
        _specReportBuilder = specReportBuilder;
        _unresolvedEndpointAnalyzer = unresolvedEndpointAnalyzer;
        _pageFlowAnalyzer = pageFlowAnalyzer;
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
        await WriteUnresolvedEndpointCausesAsync(outputDirectory, migrationSpec, rootPath, cancellationToken);
        await WritePageFlowArtifactsAsync(outputDirectory, jspPrototypes, migrationSpec, cancellationToken);

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

    private async Task WriteUnresolvedEndpointCausesAsync(
        string outputDirectory,
        MigrationSpec migrationSpec,
        string sourceDirectory,
        CancellationToken cancellationToken)
    {
        var findings = _unresolvedEndpointAnalyzer.Analyze(migrationSpec, sourceDirectory);
        var markdown = _unresolvedEndpointAnalyzer.GenerateMarkdown(findings);
        var path = Path.Combine(outputDirectory, "spec", "unresolved-causes.md");
        await _textFileStore.WriteAllTextAsync(path, markdown, cancellationToken);
    }

    private async Task WritePageFlowArtifactsAsync(
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
