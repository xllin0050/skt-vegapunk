using System.Text;
using System.Text.RegularExpressions;

namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// 從 JSP prototype 推導 click handler 到實際動作的互動圖。
/// </summary>
public sealed class InteractionGraphAnalyzer
{
    private static readonly Regex _functionSignatureRegex = new(
        @"function\s+(?<name>\w+)\s*\((?<args>[^)]*)\)\s*\{",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public InteractionGraph Analyze(IReadOnlyList<JspPrototypeArtifact> jspPrototypes)
    {
        ArgumentNullException.ThrowIfNull(jspPrototypes);

        var edges = new List<InteractionGraphEdge>();
        foreach (var prototype in jspPrototypes)
        {
            var pageActions = prototype.Events
                .Where(evt => evt.Kind is "Submit" or "Ajax" or "OpenWindow" or "Navigate")
                .ToList();
            var functionMap = BuildFunctionMap(prototype.JavaScriptPrototype);

            foreach (var clickEvent in prototype.Events.Where(evt => evt.Kind == "Click"))
            {
                var handler = ExtractHandlerName(clickEvent.Trigger);
                if (string.IsNullOrWhiteSpace(handler) || !functionMap.TryGetValue(handler, out var body))
                {
                    continue;
                }

                foreach (var action in pageActions)
                {
                    if (!body.Contains(action.SourceSnippet, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    edges.Add(new InteractionGraphEdge(
                        JspFileName: prototype.JspFileName,
                        ClickTarget: clickEvent.Target ?? string.Empty,
                        Handler: handler,
                        ActionKind: action.Kind,
                        ActionTarget: action.Target ?? action.Value ?? string.Empty,
                        Detail: action.Value ?? action.SourceSnippet));
                }
            }
        }

        return new InteractionGraph(edges);
    }

    public string GenerateMarkdown(InteractionGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new StringBuilder();
        builder.AppendLine("# Interaction Graph");
        builder.AppendLine();
        builder.AppendLine($"總計: {graph.Edges.Count} 條互動邊");
        builder.AppendLine();
        builder.AppendLine("| JSP | Click Target | Handler | Action | Target | Detail |");
        builder.AppendLine("|-----|--------------|---------|--------|--------|--------|");

        foreach (var edge in graph.Edges
            .OrderBy(edge => edge.JspFileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(edge => edge.ClickTarget, StringComparer.OrdinalIgnoreCase)
            .ThenBy(edge => edge.Handler, StringComparer.OrdinalIgnoreCase))
        {
            builder.AppendLine($"| {edge.JspFileName} | {edge.ClickTarget} | {edge.Handler} | {edge.ActionKind} | {edge.ActionTarget} | {SanitizeMarkdown(edge.Detail)} |");
        }

        return builder.ToString().Trim();
    }

    private static Dictionary<string, string> BuildFunctionMap(string javaScriptPrototype)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in _functionSignatureRegex.Matches(javaScriptPrototype))
        {
            var name = match.Groups["name"].Value;
            var openingBraceIndex = match.Index + match.Length - 1;
            var closingBraceIndex = FindMatchingBrace(javaScriptPrototype, openingBraceIndex);
            if (string.IsNullOrWhiteSpace(name) || closingBraceIndex <= openingBraceIndex)
            {
                continue;
            }

            var body = javaScriptPrototype[(openingBraceIndex + 1)..closingBraceIndex];
            map[name] = body;
        }

        return map;
    }

    private static string? ExtractHandlerName(string? rawHandler)
    {
        if (string.IsNullOrWhiteSpace(rawHandler))
        {
            return null;
        }

        var trimmed = rawHandler.Trim();
        var parenIndex = trimmed.IndexOf('(');
        if (parenIndex >= 0)
        {
            trimmed = trimmed[..parenIndex];
        }

        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string SanitizeMarkdown(string value)
    {
        return value.Replace("|", "\\|", StringComparison.Ordinal).Replace(Environment.NewLine, "<br>", StringComparison.Ordinal);
    }

    private static int FindMatchingBrace(string text, int openingBraceIndex)
    {
        var depth = 0;
        var inSingleQuote = false;
        var inDoubleQuote = false;

        for (var i = openingBraceIndex; i < text.Length; i++)
        {
            var current = text[i];
            var previous = i > 0 ? text[i - 1] : '\0';

            if (current == '\'' && !inDoubleQuote && previous != '\\')
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (current == '"' && !inSingleQuote && previous != '\\')
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (inSingleQuote || inDoubleQuote)
            {
                continue;
            }

            if (current == '{')
            {
                depth++;
                continue;
            }

            if (current != '}')
            {
                continue;
            }

            depth--;
            if (depth == 0)
            {
                return i;
            }
        }

        return -1;
    }
}
