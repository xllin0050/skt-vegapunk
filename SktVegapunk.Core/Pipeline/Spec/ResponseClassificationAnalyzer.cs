using System.Text;

namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// 根據 JSP 與 PB routine 內容推斷 endpoint 的回應型態。
/// </summary>
public sealed class ResponseClassificationAnalyzer
{
    public IReadOnlyList<ResponseClassificationArtifact> Analyze(
        MigrationSpec migrationSpec,
        IReadOnlyList<JspSourceArtifact> jspSources)
    {
        ArgumentNullException.ThrowIfNull(migrationSpec);
        ArgumentNullException.ThrowIfNull(jspSources);

        var routineMap = migrationSpec.Components
            .Where(component => !string.IsNullOrWhiteSpace(component.ClassName))
            .SelectMany(component => component.Routines.Select(routine => new
            {
                Key = $"{component.ClassName}.{routine.Prototype.Name}",
                Routine = routine
            }))
            .ToDictionary(item => item.Key, item => item.Routine, StringComparer.OrdinalIgnoreCase);

        var endpointMap = migrationSpec.EndpointCandidates
            .GroupBy(candidate => candidate.JspSource, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var artifacts = new List<ResponseClassificationArtifact>();
        foreach (var jspSource in jspSources.OrderBy(source => source.JspFileName, StringComparer.OrdinalIgnoreCase))
        {
            endpointMap.TryGetValue(jspSource.JspFileName, out var endpoint);

            var pbMethod = !string.IsNullOrWhiteSpace(jspSource.Invocation.ComponentName)
                && !string.IsNullOrWhiteSpace(jspSource.Invocation.MethodName)
                ? $"{jspSource.Invocation.ComponentName}.{jspSource.Invocation.MethodName}"
                : string.Empty;

            routineMap.TryGetValue(pbMethod, out var routine);
            var classification = Classify(routine?.Body, jspSource.Content);

            artifacts.Add(new ResponseClassificationArtifact(
                JspSource: jspSource.JspFileName,
                PbMethod: pbMethod,
                SuggestedHttpMethod: endpoint?.SuggestedHttpMethod ?? "GET",
                SuggestedRoute: endpoint?.SuggestedRoute ?? string.Empty,
                ResponseKind: classification.ResponseKind,
                Confidence: classification.Confidence,
                Evidence: classification.Evidence));
        }

        return artifacts;
    }

    public string GenerateMarkdown(IReadOnlyList<ResponseClassificationArtifact> artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);

        var builder = new StringBuilder();
        builder.AppendLine("# Response Classifications");
        builder.AppendLine();
        builder.AppendLine($"總計: {artifacts.Count} 個 endpoint classification");
        builder.AppendLine();
        builder.AppendLine("| JSP | PB Method | Route | Response | Confidence | Evidence |");
        builder.AppendLine("|-----|-----------|-------|----------|------------|----------|");

        foreach (var artifact in artifacts)
        {
            builder.AppendLine(
                $"| {artifact.JspSource} | {artifact.PbMethod} | {artifact.SuggestedHttpMethod} {artifact.SuggestedRoute} | {artifact.ResponseKind} | {artifact.Confidence} | {artifact.Evidence} |");
        }

        return builder.ToString().Trim();
    }

    private static ClassificationResult Classify(string? routineBody, string jspText)
    {
        if (!string.IsNullOrWhiteSpace(routineBody))
        {
            var body = routineBody!;

            if (ContainsAny(body, "<script>", "<script ", "location.href", "history.back()", "thisform.submit()"))
            {
                return new ClassificationResult("script-redirect", "Heuristic", "PB routine 內含 script redirect / submit 片段");
            }

            if (ContainsAny(body, "FileOpen(", "FileWriteEx(", "selectblob ", "uf_view_sign_doc(", "download"))
            {
                return new ClassificationResult("file", "Heuristic", "PB routine 內含檔案輸出或下載線索");
            }

            if (ContainsAny(body, "[{", "\"ShowText\"", "\"status\"", "\"data\"", "json"))
            {
                return new ClassificationResult("json", "Heuristic", "PB routine 內含 JSON 組裝線索");
            }

            if (ContainsAny(body, "<html", "<table", "<input", "<select", "<object", "<div", "<p ", "<center>", "<a href"))
            {
                return new ClassificationResult("html", "Heuristic", "PB routine 內含 HTML 組裝線索");
            }

            return new ClassificationResult("text", "Low", "PB routine 未出現明確 HTML / JSON / file 線索");
        }

        if (ContainsAny(jspText, "out.print(\"<script", "top.location.href", "alert('Please Logon')"))
        {
            return new ClassificationResult("script-redirect", "Low", "JSP fallback 內含 script redirect");
        }

        return new ClassificationResult("text", "Low", "找不到對應 PB routine，僅保留文字型佔位");
    }

    private static bool ContainsAny(string text, params string[] patterns) =>
        patterns.Any(pattern => text.Contains(pattern, StringComparison.OrdinalIgnoreCase));

    private sealed record ClassificationResult(
        string ResponseKind,
        string Confidence,
        string Evidence);
}
