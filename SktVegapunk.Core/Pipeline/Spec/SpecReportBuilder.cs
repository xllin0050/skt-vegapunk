using System.Text;
using System.Text.Json;
using SktVegapunk.Core.Pipeline;

namespace SktVegapunk.Core.Pipeline.Spec;

public sealed class SpecReportBuilder : ISpecReportBuilder
{
    private readonly ITextFileStore _textFileStore;
    private readonly TimeProvider _timeProvider;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SpecReportBuilder(ITextFileStore textFileStore, TimeProvider? timeProvider = null)
    {
        _textFileStore = textFileStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public MigrationSpec Build(
        IReadOnlyList<SrdSpec> dataWindows,
        IReadOnlyList<SruSpec> components,
        IReadOnlyList<JspInvocation> jspInvocations)
    {
        // 建立 component 名稱到 spec 的映射
        var componentMap = components
            .Where(c => !string.IsNullOrWhiteSpace(c.ClassName))
            .GroupBy(c => c.ClassName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        // 產生 endpoint candidates
        var endpointCandidates = new List<EndpointCandidate>();
        var unresolvedMethods = new HashSet<string>();

        foreach (var jsp in jspInvocations)
        {
            var componentKey = jsp.ComponentName;
            var methodKey = jsp.MethodName;

            if (componentMap.TryGetValue(componentKey, out var componentCandidates))
            {
                var component = componentCandidates
                    .FirstOrDefault(candidate => candidate.Prototypes.Any(p => p.Name == methodKey))
                    ?? componentCandidates[0];

                // 尋找對應的 prototype
                var prototype = component.Prototypes.FirstOrDefault(p => p.Name == methodKey);
                if (prototype != null)
                {
                    var httpMethod = DetermineHttpMethod(methodName: methodKey);
                    var route = DetermineRoute(jsp.JspFileName);

                    endpointCandidates.Add(new EndpointCandidate(
                        JspSource: jsp.JspFileName,
                        PbMethod: $"{componentKey}.{methodKey}",
                        SuggestedHttpMethod: httpMethod,
                        SuggestedRoute: route,
                        Status: EndpointStatus.Resolved));
                }
                else
                {
                    // 未找到 prototype，可能是繼承自父類
                    var duplicateReason = componentCandidates.Count > 1
                        ? $"（同名 component {componentCandidates.Count} 份）"
                        : string.Empty;
                    var statusReason = $"JSP 呼叫但不在 {componentKey}.sru forward prototypes 中，可能定義在父類 {component.ParentClass}{duplicateReason}";
                    endpointCandidates.Add(new EndpointCandidate(
                        JspSource: jsp.JspFileName,
                        PbMethod: $"{componentKey}.{methodKey}",
                        SuggestedHttpMethod: "GET",
                        SuggestedRoute: $"/api/{componentKey.ToLower()}/{methodKey.ToLower()}",
                        Status: EndpointStatus.Unresolved,
                        StatusReason: statusReason));

                    unresolvedMethods.Add($"{componentKey}.{methodKey}");
                }
            }
            else
            {
                // 未找到 component
                var statusReason = $"未找到 component {componentKey}";
                endpointCandidates.Add(new EndpointCandidate(
                    JspSource: jsp.JspFileName,
                    PbMethod: $"{componentKey}.{methodKey}",
                    SuggestedHttpMethod: "GET",
                    SuggestedRoute: $"/api/{componentKey.ToLower()}/{methodKey.ToLower()}",
                    Status: EndpointStatus.Unresolved,
                    StatusReason: statusReason));

                unresolvedMethods.Add($"{componentKey}.{methodKey}");
            }
        }

        return new MigrationSpec(
            DataWindows: dataWindows,
            Components: components,
            JspInvocations: jspInvocations,
            EndpointCandidates: endpointCandidates,
            UnresolvedMethods: unresolvedMethods.ToList().AsReadOnly());
    }

    public async Task WriteReportAsync(MigrationSpec spec, string outputDirectory, CancellationToken cancellationToken = default)
    {
        var specDir = Path.Combine(outputDirectory, "spec");
        var dataWindowsDir = Path.Combine(specDir, "datawindows");
        var componentsDir = Path.Combine(specDir, "components");

        var dataWindowTasks = spec.DataWindows.Select(dw =>
            WriteJsonAsync(
                Path.Combine(dataWindowsDir, BuildArtifactRelativePath(dw.FileName, ".json")),
                dw,
                cancellationToken));

        var componentTasks = spec.Components.Select(comp =>
            WriteJsonAsync(
                Path.Combine(componentsDir, BuildArtifactRelativePath(comp.FileName, ".json")),
                comp,
                cancellationToken));

        await Task.WhenAll(dataWindowTasks.Concat(componentTasks));

        var reportPath = Path.Combine(specDir, "report.md");
        var reportContent = GenerateMarkdownReport(spec, _timeProvider.GetUtcNow());
        await _textFileStore.WriteAllTextAsync(reportPath, reportContent, cancellationToken);
    }

    private static string DetermineHttpMethod(string methodName)
    {
        var lowerName = methodName.ToLower();
        if (lowerName.Contains("ins") || lowerName.Contains("create") || lowerName.Contains("add"))
            return "POST";
        if (lowerName.Contains("upd") || lowerName.Contains("update") || lowerName.Contains("modify"))
            return "PUT";
        if (lowerName.Contains("del") || lowerName.Contains("delete") || lowerName.Contains("remove"))
            return "DELETE";
        return "GET";
    }

    private static string DetermineRoute(string jspFileName)
    {
        var jspNameWithoutExt = Path.GetFileNameWithoutExtension(jspFileName);
        return $"/api/{jspNameWithoutExt.ToLower()}";
    }

    private static string GenerateMarkdownReport(MigrationSpec spec, DateTimeOffset generatedAtUtc)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Migration Spec Report");
        sb.AppendLine();
        sb.AppendLine($"生成時間: {generatedAtUtc:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();

        // Endpoint Candidates
        sb.AppendLine("## Endpoint Candidates");
        sb.AppendLine();
        sb.AppendLine("| # | JSP | PB Method | HTTP | Route | Status |");
        sb.AppendLine("|---|-----|-----------|------|-------|--------|");

        var resolvedCount = 0;
        var unresolvedCount = 0;

        for (var i = 0; i < spec.EndpointCandidates.Count; i++)
        {
            var endpoint = spec.EndpointCandidates[i];
            var statusIcon = endpoint.Status == EndpointStatus.Resolved ? "✅" : "⚠️";
            if (endpoint.Status == EndpointStatus.Resolved) resolvedCount++;
            else unresolvedCount++;

            sb.AppendLine($"| {i + 1} | {endpoint.JspSource} | {endpoint.PbMethod} | {endpoint.SuggestedHttpMethod} | {endpoint.SuggestedRoute} | {statusIcon} {endpoint.Status} |");
        }

        sb.AppendLine();
        sb.AppendLine($"總計: {spec.EndpointCandidates.Count} 個 endpoint（已解析: {resolvedCount}, 未解析: {unresolvedCount}）");
        sb.AppendLine();

        // Unresolved Methods
        if (spec.UnresolvedMethods.Count > 0)
        {
            sb.AppendLine("## Unresolved Methods");
            sb.AppendLine();
            foreach (var method in spec.UnresolvedMethods)
            {
                sb.AppendLine($"- `{method}`");
            }
            sb.AppendLine();
        }

        // DataWindow Summary
        sb.AppendLine("## DataWindow Summary");
        sb.AppendLine();
        sb.AppendLine($"總計: {spec.DataWindows.Count} 個 DataWindow");
        sb.AppendLine();

        foreach (var dw in spec.DataWindows)
        {
            sb.AppendLine($"### {dw.FileName}");
            sb.AppendLine($"- 資料表: {string.Join(", ", dw.Tables)}");
            sb.AppendLine($"- 欄位數: {dw.Columns.Count}");
            sb.AppendLine($"- 參數數: {dw.Arguments.Count}");
            sb.AppendLine();
        }

        // Component Summary
        sb.AppendLine("## Component Summary");
        sb.AppendLine();
        sb.AppendLine($"總計: {spec.Components.Count} 個 Component");
        sb.AppendLine();

        foreach (var comp in spec.Components)
        {
            sb.AppendLine($"### {comp.FileName}");
            sb.AppendLine($"- 類別名稱: {comp.ClassName}");
            sb.AppendLine($"- 父類別: {comp.ParentClass}");
            sb.AppendLine($"- Prototypes: {comp.Prototypes.Count}");
            sb.AppendLine($"- Routines: {comp.Routines.Count}");
            sb.AppendLine($"- Event Blocks: {comp.EventBlocks.Count}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value, _jsonOptions);
        await _textFileStore.WriteAllTextAsync(path, json, cancellationToken);
    }

    private static string BuildArtifactRelativePath(string fileName, string targetExtension)
    {
        var normalizedPath = fileName
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            throw new ArgumentException("FileName 不可為空白。", nameof(fileName));
        }

        return Path.ChangeExtension(normalizedPath, targetExtension);
    }
}
