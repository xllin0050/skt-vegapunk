using System.Text;

namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// 分析未解析 endpoint 的主要根因。
/// </summary>
public sealed class UnresolvedEndpointAnalyzer
{
    public IReadOnlyList<UnresolvedEndpointFinding> Analyze(MigrationSpec spec, string sourceDirectory)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);

        var componentsByName = spec.Components
            .Where(component => !string.IsNullOrWhiteSpace(component.ClassName))
            .GroupBy(component => component.ClassName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var sruNames = Directory.Exists(sourceDirectory)
            ? Directory.EnumerateFiles(sourceDirectory, "*.sru", SearchOption.AllDirectories)
                .Select(path => Path.GetFileNameWithoutExtension(path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var findings = new List<UnresolvedEndpointFinding>();
        foreach (var endpoint in spec.EndpointCandidates.Where(candidate => candidate.Status == EndpointStatus.Unresolved))
        {
            var separatorIndex = endpoint.PbMethod.LastIndexOf('.');
            if (separatorIndex <= 0 || separatorIndex >= endpoint.PbMethod.Length - 1)
            {
                findings.Add(new UnresolvedEndpointFinding(
                    endpoint.JspSource,
                    endpoint.PbMethod,
                    "Unknown",
                    endpoint.StatusReason ?? "無法解析 PbMethod 格式。"));
                continue;
            }

            var componentName = endpoint.PbMethod[..separatorIndex];
            var methodName = endpoint.PbMethod[(separatorIndex + 1)..];

            if (!componentsByName.TryGetValue(componentName, out var components))
            {
                var detail = sruNames.Contains(componentName)
                    ? $"找到同名 .sru，但未進入 component 分析鏈：{componentName}.sru"
                    : $"source/ 內找不到 {componentName}.sru";

                findings.Add(new UnresolvedEndpointFinding(
                    endpoint.JspSource,
                    endpoint.PbMethod,
                    "MissingComponentSource",
                    detail));
                continue;
            }

            if (!components.Any(component => component.Prototypes.Any(prototype => prototype.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase))))
            {
                findings.Add(new UnresolvedEndpointFinding(
                    endpoint.JspSource,
                    endpoint.PbMethod,
                    "MissingPrototype",
                    $"{componentName}.sru 的 forward prototypes 中找不到 {methodName}"));
                continue;
            }

            findings.Add(new UnresolvedEndpointFinding(
                endpoint.JspSource,
                endpoint.PbMethod,
                "Unknown",
                endpoint.StatusReason ?? "未能判定根因。"));
        }

        return findings;
    }

    public string GenerateMarkdown(IReadOnlyList<UnresolvedEndpointFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        var builder = new StringBuilder();
        builder.AppendLine("# Unresolved Endpoint Placeholders");
        builder.AppendLine();
        builder.AppendLine($"總計: {findings.Count} 個 unresolved endpoint");
        builder.AppendLine();
        builder.AppendLine("這些 endpoint 目前保留佔位，不阻塞 generation phase。");
        builder.AppendLine("建議先產生 stub controller/service 或 mock client，後續再補齊真實實作。");
        builder.AppendLine();

        if (findings.Count == 0)
        {
            builder.AppendLine("目前沒有 unresolved endpoint。");
            return builder.ToString().Trim();
        }

        builder.AppendLine("| # | JSP | PB Method | Placeholder | Deferred Cause | Detail |");
        builder.AppendLine("|---|-----|-----------|-------------|----------------|--------|");
        for (var i = 0; i < findings.Count; i++)
        {
            var finding = findings[i];
            builder.AppendLine($"| {i + 1} | {finding.JspSource} | {finding.PbMethod} | Stub | {finding.RootCause} | {finding.Detail} |");
        }

        return builder.ToString().Trim();
    }
}
