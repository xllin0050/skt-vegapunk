using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using SktVegapunk.Core;

namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// 當靜態分析因缺檔而產生 unresolved endpoint 時，
/// 改以 LLM 根據 JSP 原始碼與 DB schema 推導 endpoint 規格。
/// </summary>
public sealed class UnresolvedEndpointInferrer
{
    private static readonly Regex _ajaxUrlRegex = new(
        @"url\s*:\s*['""](?<url>[^'""]+\.jsp)['""]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _jsonBlockRegex = new(
        @"```(?:json)?\s*(\{[\s\S]*?\})\s*```",
        RegexOptions.Compiled);

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private const string SystemPrompt = """
        你是 PowerBuilder Jaguar EAServer 轉 ASP.NET Core Web API 的遷移助理。
        使用者會提供 JSP 原始碼與 Sybase DB schema 片段。
        請根據這些資訊推導出缺少的 PB component 方法規格，並以 JSON 格式回傳。

        JSON 格式（不得有額外文字，只回傳 JSON）：
        {
          "businessSummary": "一句話說明此 endpoint 的業務用途",
          "httpMethod": "GET 或 POST",
          "suggestedRoute": "/api/xxx/yyy",
          "inputParameters": ["param1:型別", "param2:型別"],
          "relatedTables": ["表名1", "表名2"],
          "responseType": "html|json|text"
        }
        """;

    private readonly GitHubCopilotClient _copilotClient;
    private readonly string _modelName;
    private readonly ITextFileStore _textFileStore;

    public UnresolvedEndpointInferrer(
        GitHubCopilotClient copilotClient,
        string modelName,
        ITextFileStore textFileStore)
    {
        ArgumentNullException.ThrowIfNull(copilotClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        ArgumentNullException.ThrowIfNull(textFileStore);

        _copilotClient = copilotClient;
        _modelName = modelName;
        _textFileStore = textFileStore;
    }

    /// <summary>
    /// 對每個 unresolved finding 呼叫 LLM 推導規格，並輸出 artifact。
    /// </summary>
    public async Task<IReadOnlyList<InferredEndpointSpec>> InferAndWriteAsync(
        IReadOnlyList<UnresolvedEndpointFinding> findings,
        IReadOnlyList<JspSourceArtifact> jspSources,
        SchemaArtifacts? schemaArtifacts,
        string specDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(jspSources);
        ArgumentException.ThrowIfNullOrWhiteSpace(specDirectory);

        if (findings.Count == 0)
        {
            return [];
        }

        var jspByName = jspSources.ToDictionary(
            s => Path.GetFileName(s.JspFileName),
            s => s,
            StringComparer.OrdinalIgnoreCase);

        var results = new List<InferredEndpointSpec>();

        foreach (var finding in findings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var spec = await InferSingleAsync(finding, jspByName, schemaArtifacts, cancellationToken);
            results.Add(spec);
        }

        await WriteArtifactsAsync(specDirectory, results, cancellationToken);

        return results;
    }

    private async Task<InferredEndpointSpec> InferSingleAsync(
        UnresolvedEndpointFinding finding,
        Dictionary<string, JspSourceArtifact> jspByName,
        SchemaArtifacts? schemaArtifacts,
        CancellationToken cancellationToken)
    {
        var jspFileName = Path.GetFileName(finding.JspSource);

        if (!jspByName.TryGetValue(jspFileName, out var primaryJsp))
        {
            return Stub(finding, "找不到對應的 JSP 原始碼");
        }

        var userPrompt = BuildUserPrompt(finding, primaryJsp, jspByName, schemaArtifacts);

        string? llmResponse;
        try
        {
            llmResponse = await _copilotClient.SendMessageAsync(
                _modelName,
                SystemPrompt,
                userPrompt,
                timeout: TimeSpan.FromSeconds(60),
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            return Stub(finding, $"LLM 呼叫失敗：{ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(llmResponse))
        {
            return Stub(finding, "LLM 回傳空內容");
        }

        return ParseLlmResponse(finding, llmResponse);
    }

    private static string BuildUserPrompt(
        UnresolvedEndpointFinding finding,
        JspSourceArtifact primaryJsp,
        Dictionary<string, JspSourceArtifact> jspByName,
        SchemaArtifacts? schemaArtifacts)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"## 目標：推導 `{finding.PbMethod}` 的 API 規格");
        sb.AppendLine($"根因：{finding.RootCause} — {finding.Detail}");
        sb.AppendLine();

        sb.AppendLine($"## 主要 JSP：{finding.JspSource}");
        sb.AppendLine("```jsp");
        sb.AppendLine(primaryJsp.Content);
        sb.AppendLine("```");

        // 找出主 JSP 中 AJAX 呼叫的相關 JSP 並附上原始碼
        var referencedJsps = ExtractAjaxTargets(primaryJsp.Content);
        foreach (var refJsp in referencedJsps)
        {
            if (jspByName.TryGetValue(refJsp, out var relatedJsp))
            {
                sb.AppendLine();
                sb.AppendLine($"## 相關 JSP（AJAX 目標）：{refJsp}");
                sb.AppendLine("```jsp");
                sb.AppendLine(relatedJsp.Content);
                sb.AppendLine("```");
            }
        }

        // 附上可能相關的 schema 表（以方法名稱關鍵字比對）
        if (schemaArtifacts?.Tables.Count > 0)
        {
            var keywords = ExtractKeywords(finding.PbMethod);
            var relevantTables = schemaArtifacts.Tables
                .Where(t => keywords.Any(kw => t.TableName.Contains(kw, StringComparison.OrdinalIgnoreCase)))
                .Take(10)
                .ToList();

            if (relevantTables.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## 相關 DB 資料表（DDL 摘要）");
                foreach (var table in relevantTables)
                {
                    sb.AppendLine($"### {table.TableName}");
                    var cols = string.Join(", ", table.Columns.Take(20).Select(c => c.Name));
                    sb.AppendLine($"欄位：{cols}");
                    if (table.PrimaryKey.Count > 0)
                    {
                        sb.AppendLine($"PK：{string.Join(", ", table.PrimaryKey)}");
                    }
                }
            }
        }

        return sb.ToString();
    }

    private static IReadOnlyList<string> ExtractAjaxTargets(string jspContent)
    {
        return _ajaxUrlRegex.Matches(jspContent)
            .Select(m => m.Groups["url"].Value)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> ExtractKeywords(string pbMethod)
    {
        // n_sign_history.of_sign_history_00 → ["sign", "history"]
        return pbMethod
            .Split(['.', '_'])
            .Where(p => p.Length > 2 && !p.Equals("of", StringComparison.OrdinalIgnoreCase)
                                     && !p.Equals("uf", StringComparison.OrdinalIgnoreCase)
                                     && !p.All(char.IsDigit))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static InferredEndpointSpec ParseLlmResponse(UnresolvedEndpointFinding finding, string llmResponse)
    {
        // 先嘗試從 markdown 程式碼區塊取出 JSON
        var jsonText = llmResponse;
        var match = _jsonBlockRegex.Match(llmResponse);
        if (match.Success)
        {
            jsonText = match.Groups[1].Value;
        }
        else
        {
            // 找第一個 { 到最後一個 }
            var start = llmResponse.IndexOf('{');
            var end = llmResponse.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                jsonText = llmResponse[start..(end + 1)];
            }
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            return new InferredEndpointSpec(
                JspSource: finding.JspSource,
                PbMethod: finding.PbMethod,
                SuggestedHttpMethod: GetString(root, "httpMethod", "POST"),
                SuggestedRoute: GetString(root, "suggestedRoute", string.Empty),
                BusinessSummary: GetString(root, "businessSummary", string.Empty),
                InputParameters: GetStringArray(root, "inputParameters"),
                RelatedTables: GetStringArray(root, "relatedTables"),
                ResponseType: GetString(root, "responseType", "html"),
                InferenceSucceeded: true);
        }
        catch
        {
            return Stub(finding, "LLM 回傳內容無法解析為 JSON");
        }
    }

    private static InferredEndpointSpec Stub(UnresolvedEndpointFinding finding, string reason)
    {
        return new InferredEndpointSpec(
            JspSource: finding.JspSource,
            PbMethod: finding.PbMethod,
            SuggestedHttpMethod: "POST",
            SuggestedRoute: string.Empty,
            BusinessSummary: $"[推導失敗] {reason}",
            InputParameters: [],
            RelatedTables: [],
            ResponseType: "html",
            InferenceSucceeded: false);
    }

    private async Task WriteArtifactsAsync(
        string specDirectory,
        IReadOnlyList<InferredEndpointSpec> specs,
        CancellationToken cancellationToken)
    {
        var specPath = Path.Combine(specDirectory, "inferred-endpoints");

        var jsonPath = $"{specPath}.json";
        var jsonContent = JsonSerializer.Serialize(specs, _jsonOptions);
        await _textFileStore.WriteAllTextAsync(jsonPath, jsonContent, cancellationToken);

        var mdPath = $"{specPath}.md";
        await _textFileStore.WriteAllTextAsync(mdPath, BuildMarkdown(specs), cancellationToken);
    }

    private static string BuildMarkdown(IReadOnlyList<InferredEndpointSpec> specs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# LLM 推導的 Endpoint 規格");
        sb.AppendLine();

        var succeeded = specs.Count(s => s.InferenceSucceeded);
        sb.AppendLine($"總計：{specs.Count} 個 unresolved endpoint，推導成功 {succeeded} 個。");
        sb.AppendLine();

        foreach (var spec in specs)
        {
            var status = spec.InferenceSucceeded ? "✓" : "✗";
            sb.AppendLine($"## {status} `{spec.PbMethod}`");
            sb.AppendLine($"- **JSP**：{spec.JspSource}");
            sb.AppendLine($"- **業務說明**：{spec.BusinessSummary}");
            sb.AppendLine($"- **HTTP 方法**：{spec.SuggestedHttpMethod}");
            sb.AppendLine($"- **建議路由**：{spec.SuggestedRoute}");
            sb.AppendLine($"- **回應類型**：{spec.ResponseType}");

            if (spec.InputParameters.Count > 0)
            {
                sb.AppendLine($"- **輸入參數**：{string.Join("、", spec.InputParameters)}");
            }

            if (spec.RelatedTables.Count > 0)
            {
                sb.AppendLine($"- **相關資料表**：{string.Join("、", spec.RelatedTables)}");
            }

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string GetString(JsonElement root, string propertyName, string defaultValue)
    {
        return root.TryGetProperty(propertyName, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString() ?? defaultValue
            : defaultValue;
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var el) || el.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return el.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!)
            .ToList();
    }
}
