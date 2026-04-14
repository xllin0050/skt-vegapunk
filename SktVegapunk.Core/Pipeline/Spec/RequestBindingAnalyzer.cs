using System.Text;
using System.Text.RegularExpressions;

namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// 產生 JSP 到 PB component 與 outgoing request 的參數綁定摘要。
/// </summary>
public sealed class RequestBindingAnalyzer
{
    private static readonly Regex _componentCallRegex = new(
        @"(?<receiver>\w+)\.(?<method>(?:of|uf)_\w+)\s*\(",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _assignmentRegex = new(
        @"(?<variable>\w+)\s*=\s*(?<expression>[^;\r\n]+);",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _propertyAssignmentRegex = new(
        @"(?<variable>\w+)\s*\[\s*(?:""(?<double>[^""]+)""|'(?<single>[^']+)')\s*\]\s*=\s*(?<expression>[^;\r\n]+);",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _requestParameterRegex = new(
        @"request\.getParameter\s*\(\s*""(?<name>[^""]+)""\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _sessionAttributeRegex = new(
        @"session\.getAttribute\s*\(\s*""(?<name>[^""]+)""\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _applicationAttributeRegex = new(
        @"application\.getAttribute\s*\(\s*""(?<name>[^""]+)""\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _ajaxRegex = new(
        @"\$(?:\.ajax|\.\w+\.ajax)?\s*\(\s*\{(?<body>[\s\S]*?)\}\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _ajaxUrlRegex = new(
        @"url\s*:\s*(?<value>[^,\r\n]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _ajaxTypeRegex = new(
        @"type\s*:\s*(?<value>[^,\r\n]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _ajaxDataRegex = new(
        @"data\s*:\s*(?<value>\{[\s\S]*?\}|[^,\r\n]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _getBytesRegex = new(
        @"(?<variable>\w+)\.getBytes\s*\(\s*(?<encoding>[^)]*)\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _jqueryValueRegex = new(
        @"\$\s*\(\s*['""]#(?<id>[\w:-]+)['""]\s*\)\.val\s*\(\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _domValueRegex = new(
        @"document\.getElementById\s*\(\s*['""](?<id>[\w:-]+)['""]\s*\)\.value",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public IReadOnlyList<RequestBindingArtifact> Analyze(
        MigrationSpec migrationSpec,
        IReadOnlyList<JspSourceArtifact> jspSources)
    {
        ArgumentNullException.ThrowIfNull(migrationSpec);
        ArgumentNullException.ThrowIfNull(jspSources);

        var componentPrototypeMap = migrationSpec.Components
            .Where(component => !string.IsNullOrWhiteSpace(component.ClassName))
            .SelectMany(component => component.Prototypes.Select(prototype => new
            {
                Key = $"{component.ClassName}.{prototype.Name}",
                Prototype = prototype
            }))
            .ToDictionary(item => item.Key, item => item.Prototype, StringComparer.OrdinalIgnoreCase);

        var endpointMap = migrationSpec.EndpointCandidates
            .GroupBy(candidate => candidate.JspSource, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var artifacts = new List<RequestBindingArtifact>();
        foreach (var jspSource in jspSources.OrderBy(source => source.JspFileName, StringComparer.OrdinalIgnoreCase))
        {
            endpointMap.TryGetValue(jspSource.JspFileName, out var endpoint);

            var pbMethod = !string.IsNullOrWhiteSpace(jspSource.Invocation.ComponentName)
                && !string.IsNullOrWhiteSpace(jspSource.Invocation.MethodName)
                ? $"{jspSource.Invocation.ComponentName}.{jspSource.Invocation.MethodName}"
                : string.Empty;

            componentPrototypeMap.TryGetValue(pbMethod, out var prototype);

            artifacts.Add(new RequestBindingArtifact(
                JspSource: jspSource.JspFileName,
                PbMethod: pbMethod,
                SuggestedHttpMethod: endpoint?.SuggestedHttpMethod ?? "GET",
                SuggestedRoute: endpoint?.SuggestedRoute ?? string.Empty,
                Status: endpoint?.Status.ToString() ?? EndpointStatus.Unresolved.ToString(),
                Parameters: BuildParameters(jspSource.Content, jspSource.Invocation, prototype),
                OutgoingRequests: BuildOutgoingRequests(jspSource.Content, jspSource.Prototype)));
        }

        return artifacts;
    }

    public string GenerateMarkdown(IReadOnlyList<RequestBindingArtifact> artifacts)
    {
        ArgumentNullException.ThrowIfNull(artifacts);

        var builder = new StringBuilder();
        builder.AppendLine("# Request Bindings");
        builder.AppendLine();
        builder.AppendLine($"總計: {artifacts.Count} 個 JSP binding artifact");
        builder.AppendLine();

        foreach (var artifact in artifacts)
        {
            builder.AppendLine($"## {artifact.JspSource}");
            builder.AppendLine();
            builder.AppendLine($"- PB Method: {(string.IsNullOrWhiteSpace(artifact.PbMethod) ? "N/A" : artifact.PbMethod)}");
            builder.AppendLine($"- Route: {artifact.SuggestedHttpMethod} {artifact.SuggestedRoute}");
            builder.AppendLine($"- Status: {artifact.Status}");
            builder.AppendLine();

            if (artifact.Parameters.Count == 0)
            {
                builder.AppendLine("- Parameters: 無 component parameter bindings");
            }
            else
            {
                builder.AppendLine("| # | PB Parameter | Type | Source | Source Name | Confidence | Note |");
                builder.AppendLine("|---|--------------|------|--------|-------------|------------|------|");
                foreach (var parameter in artifact.Parameters)
                {
                    builder.AppendLine(
                        $"| {parameter.Position} | {parameter.Name} | {parameter.Type ?? string.Empty} | {parameter.SourceKind} | {parameter.SourceName ?? string.Empty} | {parameter.Confidence} | {parameter.Note ?? string.Empty} |");
                }
            }

            builder.AppendLine();

            if (artifact.OutgoingRequests.Count == 0)
            {
                builder.AppendLine("- Outgoing Requests: 無");
                builder.AppendLine();
                continue;
            }

            foreach (var request in artifact.OutgoingRequests)
            {
                builder.AppendLine($"- [{request.Kind}] {request.HttpMethod} {request.Target}");
                builder.AppendLine($"  Payload: {request.PayloadExpression}");
                if (request.PayloadFields.Count == 0)
                {
                    builder.AppendLine("  Fields: 無法解析");
                    continue;
                }

                builder.AppendLine($"  Fields: {string.Join(", ", request.PayloadFields.Select(field => $"{field.Name}<{field.SourceKind}>"))}");
            }

            builder.AppendLine();
        }

        return builder.ToString().Trim();
    }

    private static IReadOnlyList<RequestBindingParameter> BuildParameters(
        string jspText,
        JspInvocation invocation,
        SruPrototype? prototype)
    {
        if (string.IsNullOrWhiteSpace(invocation.ComponentName)
            || string.IsNullOrWhiteSpace(invocation.MethodName))
        {
            return [];
        }

        var callMatch = _componentCallRegex.Match(jspText);
        var callIndex = callMatch.Success ? callMatch.Index : jspText.Length;
        var assignments = ParseAssignments(jspText, callIndex);

        var prototypeParameters = prototype?.Parameters ?? [];
        var parameters = new List<RequestBindingParameter>();
        for (var i = 0; i < invocation.Parameters.Count; i++)
        {
            var expression = invocation.Parameters[i];
            var prototypeParameter = i < prototypeParameters.Count ? prototypeParameters[i] : null;
            var binding = ResolveBinding(assignments, expression);

            parameters.Add(new RequestBindingParameter(
                Position: i + 1,
                Name: prototypeParameter?.Name ?? $"arg{i + 1}",
                Type: prototypeParameter?.Type,
                SourceKind: binding.SourceKind,
                SourceName: binding.SourceName,
                Expression: binding.Expression,
                Confidence: binding.Confidence,
                Note: binding.Note));
        }

        return parameters;
    }

    private static IReadOnlyList<RequestBindingTransport> BuildOutgoingRequests(
        string jspText,
        JspPrototypeArtifact prototype)
    {
        var transports = new List<RequestBindingTransport>();
        var formControls = ParseFormControls(prototype.Controls);
        var formActions = new Dictionary<string, (string? Action, string? Method)>(StringComparer.OrdinalIgnoreCase);
        foreach (var form in prototype.Forms)
        {
            if (!string.IsNullOrWhiteSpace(form.Id))
            {
                formActions[form.Id] = (form.Action, form.Method);
            }

            if (!string.IsNullOrWhiteSpace(form.Name))
            {
                formActions[form.Name] = (form.Action, form.Method);
            }
        }

        foreach (var evt in prototype.Events.OrderBy(evt => evt.Order))
        {
            if (evt.Kind == "FormActionChange" && !string.IsNullOrWhiteSpace(evt.Target))
            {
                var current = formActions.TryGetValue(evt.Target, out var existing)
                    ? existing
                    : (Action: (string?)null, Method: (string?)null);
                formActions[evt.Target] = (NormalizeValue(evt.Value), current.Method);
                continue;
            }

            if (evt.Kind == "Submit" && !string.IsNullOrWhiteSpace(evt.Target))
            {
                formActions.TryGetValue(evt.Target, out var formState);
                var payloadFields = formControls.TryGetValue(evt.Target, out var controls)
                    ? controls
                    : [];

                transports.Add(new RequestBindingTransport(
                    Kind: "FormSubmit",
                    Target: NormalizeValue(formState.Action) ?? string.Empty,
                    HttpMethod: string.IsNullOrWhiteSpace(formState.Method) ? "POST" : formState.Method!.ToUpperInvariant(),
                    PayloadExpression: $"form:{evt.Target}",
                    PayloadFields: payloadFields));
                continue;
            }
        }

        foreach (Match match in _ajaxRegex.Matches(jspText))
        {
            var body = match.Groups["body"].Value;
            var target = NormalizeValue(_ajaxUrlRegex.Match(body).Groups["value"].Value) ?? string.Empty;
            var method = NormalizeValue(_ajaxTypeRegex.Match(body).Groups["value"].Value) ?? "POST";
            var payloadExpression = NormalizeValue(_ajaxDataRegex.Match(body).Groups["value"].Value) ?? string.Empty;
            var assignments = ParseAssignments(jspText, match.Index);

            transports.Add(new RequestBindingTransport(
                Kind: "Ajax",
                Target: target,
                HttpMethod: method.Trim('\'', '"').ToUpperInvariant(),
                PayloadExpression: payloadExpression,
                PayloadFields: ParseAjaxPayloadFields(payloadExpression, assignments, prototype.Controls)));
        }

        return transports;
    }

    private static Dictionary<string, List<RequestPayloadField>> ParseFormControls(IReadOnlyList<JspControlPrototype> controls)
    {
        var result = new Dictionary<string, List<RequestPayloadField>>(StringComparer.OrdinalIgnoreCase);
        foreach (var formGroup in controls
            .Where(control => !string.IsNullOrWhiteSpace(control.FormKey))
            .GroupBy(control => control.FormKey!, StringComparer.OrdinalIgnoreCase))
        {
            var fields = new List<RequestPayloadField>();
            foreach (var control in formGroup)
            {
                var name = control.Name ?? control.Id;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                fields.Add(new RequestPayloadField(
                    Name: name,
                    SourceKind: "FormControl",
                    SourceExpression: name,
                    SourceControl: control.Id ?? control.Name));
            }

            result[formGroup.Key] = fields;
        }

        return result;
    }

    private static IReadOnlyList<RequestPayloadField> ParseAjaxPayloadFields(
        string payloadExpression,
        IReadOnlyList<VariableAssignment> assignments,
        IReadOnlyList<JspControlPrototype> controls)
    {
        if (string.IsNullOrWhiteSpace(payloadExpression))
        {
            return [];
        }

        var trimmed = payloadExpression.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal) || !trimmed.EndsWith("}", StringComparison.Ordinal))
        {
            if (assignments.Any(assignment =>
                assignment.Variable.Equals(trimmed, StringComparison.OrdinalIgnoreCase)
                || assignment.Variable.StartsWith($"{trimmed}.", StringComparison.OrdinalIgnoreCase)))
            {
                return ParseObjectVariableFields(trimmed, assignments, controls);
            }

            return
            [
                new RequestPayloadField(
                    Name: "*payload*",
                    SourceKind: "Expression",
                    SourceExpression: trimmed,
                    SourceControl: TryResolveControlName(trimmed, controls))
            ];
        }

        var inner = trimmed[1..^1];
        var fields = new List<RequestPayloadField>();
        foreach (var part in inner.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                continue;
            }

            var colonIndex = part.IndexOf(':');
            if (colonIndex >= 0)
            {
                var key = part[..colonIndex].Trim().Trim('\'', '"');
                var expression = part[(colonIndex + 1)..].Trim();
                var (sourceKind, controlName) = ClassifyPayloadExpression(expression, controls);
                fields.Add(new RequestPayloadField(
                    Name: key,
                    SourceKind: sourceKind,
                    SourceExpression: expression,
                    SourceControl: controlName));
                continue;
            }

            var shorthandKey = part.Trim().Trim('\'', '"');
            fields.Add(new RequestPayloadField(
                Name: shorthandKey,
                SourceKind: "Shorthand",
                SourceExpression: shorthandKey,
                SourceControl: null));
        }

        return fields;
    }

    private static IReadOnlyList<VariableAssignment> ParseAssignments(string jspText, int callIndex)
    {
        var assignments = new List<VariableAssignment>();
        foreach (Match match in _assignmentRegex.Matches(jspText))
        {
            if (match.Index >= callIndex)
            {
                break;
            }

            var variable = match.Groups["variable"].Value;
            var expression = match.Groups["expression"].Value.Trim();
            if (string.IsNullOrWhiteSpace(variable) || string.IsNullOrWhiteSpace(expression))
            {
                continue;
            }

            assignments.Add(new VariableAssignment(
                Variable: variable,
                Index: match.Index,
                Expression: expression,
                Classification: ClassifyExpression(expression)));
        }

        foreach (Match match in _propertyAssignmentRegex.Matches(jspText))
        {
            if (match.Index >= callIndex)
            {
                break;
            }

            var variable = match.Groups["variable"].Value;
            var property = match.Groups["double"].Success
                ? match.Groups["double"].Value
                : match.Groups["single"].Value;
            var expression = match.Groups["expression"].Value.Trim();
            if (string.IsNullOrWhiteSpace(variable) || string.IsNullOrWhiteSpace(property) || string.IsNullOrWhiteSpace(expression))
            {
                continue;
            }

            assignments.Add(new VariableAssignment(
                Variable: $"{variable}.{property}",
                Index: match.Index,
                Expression: expression,
                Classification: ClassifyExpression(expression)));
        }

        return assignments;
    }

    private static BindingResolution ResolveBinding(
        IReadOnlyList<VariableAssignment> assignments,
        string expression,
        int depth = 0)
    {
        if (depth > 8)
        {
            return new BindingResolution(
                "Variable",
                expression,
                expression,
                "Low",
                "來源追蹤超過安全深度。");
        }

        var direct = ClassifyExpression(expression);
        if (direct.SourceKind != "Variable")
        {
            return new BindingResolution(
                direct.SourceKind,
                direct.SourceName,
                expression,
                "Exact",
                direct.Note);
        }

        var getBytesMatch = _getBytesRegex.Match(expression.Trim());
        if (getBytesMatch.Success)
        {
            var innerVariable = getBytesMatch.Groups["variable"].Value;
            var innerBinding = ResolveBinding(assignments, innerVariable, depth + 1);
            var encoding = NormalizeValue(getBytesMatch.Groups["encoding"].Value);
            var note = string.IsNullOrWhiteSpace(innerBinding.Note)
                ? $"以 {encoding} 編碼轉為 blob。"
                : $"{innerBinding.Note} 以 {encoding} 編碼轉為 blob。";

            return innerBinding with
            {
                Expression = expression,
                Confidence = "Heuristic",
                Note = note
            };
        }

        var assignmentCandidates = assignments
            .Where(assignment => assignment.Variable.Equals(expression, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var candidates = assignmentCandidates
            .Select(assignment => assignment.Classification)
            .ToList();

        var preferred = candidates.FirstOrDefault(candidate =>
            candidate.SourceKind is "RequestParameter" or "SessionAttribute" or "ApplicationAttribute");

        if (preferred is not null)
        {
            var note = candidates.Any(candidate => candidate.SourceKind == "Literal")
                ? "偵測到後續 default/fallback 賦值。"
                : null;

            return new BindingResolution(
                preferred.SourceKind,
                preferred.SourceName,
                expression,
                "Heuristic",
                note);
        }

        foreach (var assignment in assignmentCandidates.OrderByDescending(candidate => candidate.Index))
        {
            if (assignment.Expression.Equals(expression, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var resolved = ResolveBinding(assignments, assignment.Expression, depth + 1);
            if (resolved.SourceKind != "Variable")
            {
                return resolved with
                {
                    Expression = expression,
                    Confidence = "Heuristic"
                };
            }
        }

        var last = candidates.LastOrDefault();
        if (last is not null)
        {
            return new BindingResolution(
                last.SourceKind,
                last.SourceName,
                expression,
                "Heuristic",
                last.Note);
        }

        return new BindingResolution(
            "Variable",
            expression,
            expression,
            "Low",
            "找不到對應的外部來源賦值。");
    }

    private static ExpressionClassification ClassifyExpression(string expression)
    {
        var trimmed = expression.Trim();

        var requestMatch = _requestParameterRegex.Match(trimmed);
        if (requestMatch.Success)
        {
            return new ExpressionClassification("RequestParameter", requestMatch.Groups["name"].Value, null);
        }

        var sessionMatch = _sessionAttributeRegex.Match(trimmed);
        if (sessionMatch.Success)
        {
            return new ExpressionClassification("SessionAttribute", sessionMatch.Groups["name"].Value, null);
        }

        var applicationMatch = _applicationAttributeRegex.Match(trimmed);
        if (applicationMatch.Success)
        {
            return new ExpressionClassification("ApplicationAttribute", applicationMatch.Groups["name"].Value, null);
        }

        if ((trimmed.StartsWith("\"", StringComparison.Ordinal) && trimmed.EndsWith("\"", StringComparison.Ordinal))
            || (trimmed.StartsWith("'", StringComparison.Ordinal) && trimmed.EndsWith("'", StringComparison.Ordinal)))
        {
            return new ExpressionClassification("Literal", NormalizeValue(trimmed), null);
        }

        if (trimmed.Contains('+', StringComparison.Ordinal))
        {
            return new ExpressionClassification("CompositeExpression", trimmed, null);
        }

        return new ExpressionClassification("Variable", trimmed, null);
    }

    private static IReadOnlyList<RequestPayloadField> ParseObjectVariableFields(
        string variableName,
        IReadOnlyList<VariableAssignment> assignments,
        IReadOnlyList<JspControlPrototype> controls)
    {
        return assignments
            .Where(assignment => assignment.Variable.StartsWith($"{variableName}.", StringComparison.OrdinalIgnoreCase))
            .Select(assignment =>
            {
                var fieldName = assignment.Variable[(variableName.Length + 1)..];
                var (sourceKind, controlName) = ClassifyPayloadExpression(assignment.Expression, controls);
                return new RequestPayloadField(
                    Name: fieldName,
                    SourceKind: sourceKind,
                    SourceExpression: assignment.Expression,
                    SourceControl: controlName);
            })
            .ToList();
    }

    private static (string SourceKind, string? SourceControl) ClassifyPayloadExpression(
        string expression,
        IReadOnlyList<JspControlPrototype> controls)
    {
        var controlName = TryResolveControlName(expression, controls);
        if (!string.IsNullOrWhiteSpace(controlName))
        {
            return ("ControlValue", controlName);
        }

        return ("Expression", null);
    }

    private static string? TryResolveControlName(string expression, IReadOnlyList<JspControlPrototype> controls)
    {
        var jqueryMatch = _jqueryValueRegex.Match(expression);
        if (jqueryMatch.Success)
        {
            var id = jqueryMatch.Groups["id"].Value;
            return FindControlKey(id, controls);
        }

        var domMatch = _domValueRegex.Match(expression);
        if (domMatch.Success)
        {
            var id = domMatch.Groups["id"].Value;
            return FindControlKey(id, controls);
        }

        return null;
    }

    private static string? FindControlKey(string id, IReadOnlyList<JspControlPrototype> controls)
    {
        var control = controls.FirstOrDefault(control =>
            string.Equals(control.Id, id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(control.Name, id, StringComparison.OrdinalIgnoreCase));

        return control?.Id ?? control?.Name;
    }

    private static string? NormalizeValue(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        return rawValue.Trim().TrimEnd(',').Trim().Trim('\'', '"');
    }

    private sealed record VariableAssignment(
        string Variable,
        int Index,
        string Expression,
        ExpressionClassification Classification);

    private sealed record ExpressionClassification(
        string SourceKind,
        string? SourceName,
        string? Note);

    private sealed record BindingResolution(
        string SourceKind,
        string? SourceName,
        string Expression,
        string Confidence,
        string? Note);
}
