using System.Text;
using System.Text.RegularExpressions;

namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// 從 JSP prototype 與 endpoint 規格推導頁面流程圖。
/// </summary>
public sealed class PageFlowAnalyzer
{
    private static readonly Regex _pathRegex = new(
        @"(?<path>[^""']+\.(?:jsp|html)(?:\?[^""']*)?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public PageFlowGraph Analyze(
        IReadOnlyList<JspPrototypeArtifact> jspPrototypes,
        MigrationSpec migrationSpec)
    {
        ArgumentNullException.ThrowIfNull(jspPrototypes);
        ArgumentNullException.ThrowIfNull(migrationSpec);

        var pages = jspPrototypes
            .Select(prototype => prototype.JspFileName)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var endpointMap = migrationSpec.EndpointCandidates
            .GroupBy(endpoint => endpoint.JspSource, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var edges = new List<PageFlowEdge>();
        foreach (var prototype in jspPrototypes)
        {
            var formActions = BuildInitialFormActions(prototype.Forms);
            foreach (var evt in prototype.Events.OrderBy(evt => evt.Order))
            {
                switch (evt.Kind)
                {
                    case "FormActionChange":
                        if (!string.IsNullOrWhiteSpace(evt.Target))
                        {
                            formActions[evt.Target] = evt.Value;
                        }
                        break;

                    case "Submit":
                        if (!string.IsNullOrWhiteSpace(evt.Target)
                            && formActions.TryGetValue(evt.Target, out var actionValue)
                            && TryResolvePath(actionValue, out var submitTarget))
                        {
                            edges.Add(new PageFlowEdge(
                                Source: prototype.JspFileName,
                                Kind: "Submit",
                                Trigger: evt.Target,
                                Target: submitTarget,
                                Detail: actionValue ?? string.Empty));
                        }
                        break;

                    case "Ajax":
                        if (TryResolvePath(evt.Target, out var ajaxTarget))
                        {
                            edges.Add(new PageFlowEdge(
                                Source: prototype.JspFileName,
                                Kind: "Ajax",
                                Trigger: evt.Trigger,
                                Target: ajaxTarget,
                                Detail: evt.Value ?? string.Empty));
                        }
                        break;

                    case "OpenWindow":
                        if (TryResolvePath(evt.Value, out var popupTarget))
                        {
                            edges.Add(new PageFlowEdge(
                                Source: prototype.JspFileName,
                                Kind: "OpenWindow",
                                Trigger: evt.Trigger,
                                Target: popupTarget,
                                Detail: evt.Value ?? string.Empty));
                        }
                        break;

                    case "Navigate":
                        if (TryResolvePath(evt.Value, out var navigateTarget))
                        {
                            edges.Add(new PageFlowEdge(
                                Source: prototype.JspFileName,
                                Kind: "Navigate",
                                Trigger: evt.Target ?? evt.Trigger,
                                Target: navigateTarget,
                                Detail: evt.Value ?? string.Empty));
                        }
                        break;
                }
            }

            if (endpointMap.TryGetValue(prototype.JspFileName, out var endpoints))
            {
                foreach (var endpoint in endpoints)
                {
                    edges.Add(new PageFlowEdge(
                        Source: prototype.JspFileName,
                        Kind: endpoint.Status == EndpointStatus.Resolved ? "ComponentCall" : "ComponentCallUnresolved",
                        Trigger: $"{endpoint.SuggestedHttpMethod} {endpoint.SuggestedRoute}",
                        Target: endpoint.PbMethod,
                        Detail: endpoint.StatusReason ?? string.Empty));
                }
            }
        }

        return new PageFlowGraph(
            Pages: pages,
            Edges: edges);
    }

    public string GenerateMarkdown(PageFlowGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new StringBuilder();
        builder.AppendLine("# JSP Page Flow");
        builder.AppendLine();
        builder.AppendLine($"頁面數: {graph.Pages.Count}");
        builder.AppendLine($"邊數: {graph.Edges.Count}");
        builder.AppendLine();

        foreach (var page in graph.Pages)
        {
            builder.AppendLine($"## {page}");
            builder.AppendLine();

            var pageEdges = graph.Edges
                .Where(edge => edge.Source.Equals(page, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (pageEdges.Count == 0)
            {
                builder.AppendLine("- 無可推導的流程邊");
                builder.AppendLine();
                continue;
            }

            foreach (var edge in pageEdges)
            {
                builder.AppendLine($"- [{edge.Kind}] {edge.Source} -> {edge.Target}");
                builder.AppendLine($"  Trigger: {edge.Trigger}");
                if (!string.IsNullOrWhiteSpace(edge.Detail))
                {
                    builder.AppendLine($"  Detail: {edge.Detail}");
                }
            }

            builder.AppendLine();
        }

        return builder.ToString().Trim();
    }

    private static Dictionary<string, string?> BuildInitialFormActions(IReadOnlyList<JspFormPrototype> forms)
    {
        var actions = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var form in forms)
        {
            if (!string.IsNullOrWhiteSpace(form.Id))
            {
                actions[form.Id] = form.Action;
            }

            if (!string.IsNullOrWhiteSpace(form.Name))
            {
                actions[form.Name] = form.Action;
            }
        }

        return actions;
    }

    private static bool TryResolvePath(string? rawValue, out string path)
    {
        path = string.Empty;
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var match = _pathRegex.Match(rawValue);
        if (!match.Success)
        {
            return false;
        }

        path = match.Groups["path"].Value;
        return !string.IsNullOrWhiteSpace(path);
    }
}
