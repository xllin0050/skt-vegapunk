using System.Text;
using System.Text.RegularExpressions;

namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// 從 JSP 提取可供前端搬遷參考的 HTML、JavaScript、CSS 原型。
/// </summary>
public sealed class JspPrototypeExtractor
{
    private static readonly Regex _onclickRegex = new(
        @"<(?<tag>\w+)\b(?<attrs>[^>]*)\bonclick\s*=\s*(?:""(?<double>[^""]*)""|'(?<single>[^']*)')",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _scriptBlockRegex = new(
        @"<script\b(?<attrs>[^>]*)>(?<content>[\s\S]*?)</script>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _styleBlockRegex = new(
        @"<style\b[^>]*>(?<content>[\s\S]*?)</style>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _stylesheetLinkRegex = new(
        @"<link\b(?<attrs>[^>]*)>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _formRegex = new(
        @"<form\b(?<attrs>[^>]*)>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _formBlockRegex = new(
        @"<form\b(?<attrs>[^>]*)>(?<content>[\s\S]*?)</form>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _controlRegex = new(
        @"<(?<tag>input|select|textarea|button|a)\b(?<attrs>[^>]*)>(?<content>[\s\S]*?)(?:</\k<tag>>)?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _directiveRegex = new(
        @"<%@[\s\S]*?%>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _declarationRegex = new(
        @"<%![\s\S]*?%>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _expressionRegex = new(
        @"<%=[\s\S]*?%>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _scriptletRegex = new(
        @"<%(?!@|=|!)[\s\S]*?%>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _attributeRegex = new(
        @"(?<name>[\w:-]+)\s*=\s*(?:""(?<double>[^""]*)""|'(?<single>[^']*)')",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _formActionAssignmentRegex = new(
        @"(?<target>\w+)\.action\s*=\s*(?<value>[^;]+);",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _submitRegex = new(
        @"(?<target>\w+)\.submit\s*\(\s*\)\s*;",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _windowOpenRegex = new(
        @"window\.open\s*\(\s*(?<value>[^,]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _navigateRegex = new(
        @"(?<target>(?:top\.)?location\.href)\s*=\s*(?<value>[^;]+);",
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

    private static readonly Regex _ajaxDataTypeRegex = new(
        @"dataType\s*:\s*(?<value>[^,\r\n]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IJspExtractor _jspExtractor;

    public JspPrototypeExtractor(IJspExtractor jspExtractor)
    {
        ArgumentNullException.ThrowIfNull(jspExtractor);
        _jspExtractor = jspExtractor;
    }

    public JspPrototypeArtifact Extract(string jspText)
    {
        ArgumentNullException.ThrowIfNull(jspText);

        var invocation = _jspExtractor.Extract(jspText);
        var forms = ExtractForms(jspText);
        var controls = ExtractControls(jspText);
        var events = ExtractEvents(jspText);
        var scriptSources = new List<string>();
        var scriptBodies = new List<string>();
        foreach (Match match in _scriptBlockRegex.Matches(jspText))
        {
            var attrs = match.Groups["attrs"].Value;
            var src = GetAttributeValue(attrs, "src");
            if (!string.IsNullOrWhiteSpace(src))
            {
                scriptSources.Add(src);
            }

            var content = match.Groups["content"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(content))
            {
                scriptBodies.Add(content);
            }
        }

        var styleSources = new List<string>();
        foreach (Match match in _stylesheetLinkRegex.Matches(jspText))
        {
            var attrs = match.Groups["attrs"].Value;
            var rel = GetAttributeValue(attrs, "rel");
            if (string.IsNullOrWhiteSpace(rel) || !rel.Contains("stylesheet", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var href = GetAttributeValue(attrs, "href");
            if (!string.IsNullOrWhiteSpace(href))
            {
                styleSources.Add(href);
            }
        }

        var styleBodies = _styleBlockRegex.Matches(jspText)
            .Select(match => match.Groups["content"].Value.Trim())
            .Where(content => !string.IsNullOrWhiteSpace(content))
            .ToList();

        return new JspPrototypeArtifact(
            JspFileName: string.Empty,
            HtmlPrototype: BuildHtmlPrototype(jspText),
            JavaScriptPrototype: JoinBlocks(scriptBodies, "// ---- inline script ----"),
            CssPrototype: JoinBlocks(styleBodies, "/* ---- inline style ---- */"),
            Forms: forms,
            Controls: controls,
            Events: events,
            ScriptSources: scriptSources,
            StyleSources: styleSources,
            HttpParameters: invocation.HttpParameters,
            ComponentName: invocation.ComponentName,
            MethodName: invocation.MethodName);
    }

    private static IReadOnlyList<JspFormPrototype> ExtractForms(string jspText)
    {
        var forms = new List<JspFormPrototype>();
        foreach (Match match in _formRegex.Matches(jspText))
        {
            var attrs = match.Groups["attrs"].Value;
            forms.Add(new JspFormPrototype(
                Id: GetAttributeValue(attrs, "id"),
                Name: GetAttributeValue(attrs, "name"),
                Method: GetAttributeValue(attrs, "method"),
                Action: GetAttributeValue(attrs, "action"),
                Target: GetAttributeValue(attrs, "target")));
        }

        return forms;
    }

    private static IReadOnlyList<JspInteractionEvent> ExtractEvents(string jspText)
    {
        var indexedEvents = new List<(int Index, JspInteractionEvent Event)>();

        foreach (Match match in _onclickRegex.Matches(jspText))
        {
            var attrs = match.Groups["attrs"].Value;
            var handler = match.Groups["double"].Success
                ? match.Groups["double"].Value
                : match.Groups["single"].Value;

            indexedEvents.Add((match.Index, new JspInteractionEvent(
                Order: 0,
                Kind: "Click",
                Trigger: handler,
                Target: GetAttributeValue(attrs, "id") ?? GetAttributeValue(attrs, "name") ?? match.Groups["tag"].Value,
                Value: null,
                SourceSnippet: match.Value.Trim())));
        }

        foreach (Match match in _formActionAssignmentRegex.Matches(jspText))
        {
            indexedEvents.Add((match.Index, new JspInteractionEvent(
                Order: 0,
                Kind: "FormActionChange",
                Trigger: "script",
                Target: match.Groups["target"].Value,
                Value: match.Groups["value"].Value.Trim(),
                SourceSnippet: match.Value.Trim())));
        }

        foreach (Match match in _submitRegex.Matches(jspText))
        {
            indexedEvents.Add((match.Index, new JspInteractionEvent(
                Order: 0,
                Kind: "Submit",
                Trigger: "script",
                Target: match.Groups["target"].Value,
                Value: null,
                SourceSnippet: match.Value.Trim())));
        }

        foreach (Match match in _windowOpenRegex.Matches(jspText))
        {
            indexedEvents.Add((match.Index, new JspInteractionEvent(
                Order: 0,
                Kind: "OpenWindow",
                Trigger: "script",
                Target: "window",
                Value: match.Groups["value"].Value.Trim(),
                SourceSnippet: match.Value.Trim())));
        }

        foreach (Match match in _navigateRegex.Matches(jspText))
        {
            indexedEvents.Add((match.Index, new JspInteractionEvent(
                Order: 0,
                Kind: "Navigate",
                Trigger: "script",
                Target: match.Groups["target"].Value,
                Value: match.Groups["value"].Value.Trim(),
                SourceSnippet: match.Value.Trim())));
        }

        foreach (Match match in _ajaxRegex.Matches(jspText))
        {
            var body = match.Groups["body"].Value;
            var url = NormalizeJsValue(_ajaxUrlRegex.Match(body).Groups["value"].Value);
            var method = NormalizeJsValue(_ajaxTypeRegex.Match(body).Groups["value"].Value);
            var dataType = NormalizeJsValue(_ajaxDataTypeRegex.Match(body).Groups["value"].Value);
            var value = $"url={url}; method={method}; dataType={dataType}";

            indexedEvents.Add((match.Index, new JspInteractionEvent(
                Order: 0,
                Kind: "Ajax",
                Trigger: "script",
                Target: url,
                Value: value,
                SourceSnippet: match.Value.Trim())));
        }

        return indexedEvents
            .OrderBy(item => item.Index)
            .Select((item, index) => item.Event with { Order = index + 1 })
            .ToList();
    }

    private static IReadOnlyList<JspControlPrototype> ExtractControls(string jspText)
    {
        var controls = new List<JspControlPrototype>();
        var assignedForms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match formMatch in _formBlockRegex.Matches(jspText))
        {
            var attrs = formMatch.Groups["attrs"].Value;
            var content = formMatch.Groups["content"].Value;
            var formKey = GetAttributeValue(attrs, "id") ?? GetAttributeValue(attrs, "name");
            controls.AddRange(ParseControls(content, formKey));
            if (!string.IsNullOrWhiteSpace(formKey))
            {
                assignedForms.Add(formKey);
            }
        }

        foreach (var control in ParseControls(jspText, null))
        {
            if (!string.IsNullOrWhiteSpace(control.FormKey) && assignedForms.Contains(control.FormKey))
            {
                continue;
            }

            if (controls.Any(existing =>
                existing.TagName == control.TagName
                && string.Equals(existing.Id, control.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Name, control.Name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Text, control.Text, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            controls.Add(control);
        }

        return controls;
    }

    private static IReadOnlyList<JspControlPrototype> ParseControls(string markup, string? formKey)
    {
        var controls = new List<JspControlPrototype>();
        foreach (Match match in _controlRegex.Matches(markup))
        {
            var tag = match.Groups["tag"].Value.ToLowerInvariant();
            var attrs = match.Groups["attrs"].Value;
            var content = match.Groups["content"].Value.Trim();
            var controlFormKey = GetAttributeValue(attrs, "form") ?? formKey;
            var onClickHandler = GetAttributeValue(attrs, "onclick");
            var text = tag is "button" or "a"
                ? StripTags(content)
                : null;

            controls.Add(new JspControlPrototype(
                TagName: tag,
                Type: GetAttributeValue(attrs, "type"),
                Id: GetAttributeValue(attrs, "id"),
                Name: GetAttributeValue(attrs, "name"),
                Value: GetAttributeValue(attrs, "value"),
                Text: string.IsNullOrWhiteSpace(text) ? null : text,
                FormKey: controlFormKey,
                OnClickHandler: onClickHandler));
        }

        return controls;
    }

    private static string BuildHtmlPrototype(string jspText)
    {
        var sanitized = _directiveRegex.Replace(jspText, string.Empty);
        sanitized = _declarationRegex.Replace(sanitized, "<!-- JSP_DECLARATION -->");
        sanitized = _expressionRegex.Replace(sanitized, "{{ JSP_EXPRESSION }}");
        sanitized = _scriptletRegex.Replace(sanitized, "<!-- JSP_SCRIPTLET -->");

        return sanitized.Trim();
    }

    private static string JoinBlocks(IReadOnlyList<string> blocks, string separator)
    {
        if (blocks.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < blocks.Count; i++)
        {
            if (i > 0)
            {
                builder.AppendLine();
                builder.AppendLine(separator);
                builder.AppendLine();
            }

            builder.AppendLine(blocks[i]);
        }

        return builder.ToString().Trim();
    }

    private static string? NormalizeJsValue(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        return rawValue.Trim().TrimEnd(',').Trim();
    }

    private static string? GetAttributeValue(string attrs, string attributeName)
    {
        foreach (Match match in _attributeRegex.Matches(attrs))
        {
            var name = match.Groups["name"].Value;
            if (!name.Equals(attributeName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = match.Groups["double"].Success
                ? match.Groups["double"].Value
                : match.Groups["single"].Value;

            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }

    private static string StripTags(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return string.Empty;
        }

        return Regex.Replace(rawText, "<[^>]+>", string.Empty).Trim();
    }
}
