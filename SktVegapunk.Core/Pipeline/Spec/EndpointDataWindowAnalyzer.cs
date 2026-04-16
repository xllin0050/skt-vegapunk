using System.Text;

namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// 掃描 SruSpec routine body，建立 Endpoint → DataWindow 的交叉索引。
/// </summary>
public sealed class EndpointDataWindowAnalyzer
{
    public IReadOnlyList<EndpointDataWindowMapEntry> Analyze(
        MigrationSpec migrationSpec,
        IReadOnlyList<SruSpec> components)
    {
        ArgumentNullException.ThrowIfNull(migrationSpec);
        ArgumentNullException.ThrowIfNull(components);

        // 建立 (componentFileName, methodName) → ReferencedDataWindows 的快速查表
        // PbMethod 格式為 "n_sign.of_sign_00"，componentFileName 如 "sign/n_sign.sru"
        var routineMap = BuildRoutineMap(components);

        var results = new List<EndpointDataWindowMapEntry>();

        foreach (var endpoint in migrationSpec.EndpointCandidates)
        {
            if (endpoint.Status != EndpointStatus.Resolved)
            {
                continue;
            }

            // PbMethod 格式：<componentName>.<methodName>（都小寫）
            var pbMethod = endpoint.PbMethod ?? string.Empty;
            var dotIndex = pbMethod.LastIndexOf('.');
            if (dotIndex < 0)
            {
                continue;
            }

            var componentName = pbMethod[..dotIndex].ToLowerInvariant();
            var methodName = pbMethod[(dotIndex + 1)..].ToLowerInvariant();
            var key = (componentName, methodName);

            if (!routineMap.TryGetValue(key, out var dataWindowNames) || dataWindowNames.Count == 0)
            {
                continue;
            }

            results.Add(new EndpointDataWindowMapEntry(
                SuggestedRoute: endpoint.SuggestedRoute,
                PbMethod: endpoint.PbMethod ?? string.Empty,
                DataWindowNames: dataWindowNames));
        }

        return results.AsReadOnly();
    }

    public string GenerateMarkdown(IReadOnlyList<EndpointDataWindowMapEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var builder = new StringBuilder();
        builder.AppendLine("# Endpoint → DataWindow Map");
        builder.AppendLine();
        builder.AppendLine($"總計 {entries.Count} 個 endpoint 有明確引用 DataWindow。");
        builder.AppendLine();

        if (entries.Count == 0)
        {
            builder.AppendLine("無可識別的 DataWindow 引用。");
            return builder.ToString().Trim();
        }

        builder.AppendLine("| Route | PbMethod | DataWindows |");
        builder.AppendLine("|-------|----------|-------------|");
        foreach (var entry in entries)
        {
            builder.AppendLine($"| {entry.SuggestedRoute} | {entry.PbMethod} | {string.Join(", ", entry.DataWindowNames)} |");
        }

        return builder.ToString().Trim();
    }

    private static Dictionary<(string Component, string Method), IReadOnlyList<string>> BuildRoutineMap(
        IReadOnlyList<SruSpec> components)
    {
        var map = new Dictionary<(string, string), IReadOnlyList<string>>();

        foreach (var sru in components)
        {
            // 從檔名取得 component name（去副檔名、取最後一段）
            var componentName = Path.GetFileNameWithoutExtension(sru.FileName)
                .ToLowerInvariant();

            foreach (var routine in sru.Routines)
            {
                var key = (componentName, routine.Prototype.Name.ToLowerInvariant());
                if (routine.ReferencedDataWindows.Count > 0)
                {
                    map[key] = routine.ReferencedDataWindows;
                }
            }
        }

        return map;
    }
}
