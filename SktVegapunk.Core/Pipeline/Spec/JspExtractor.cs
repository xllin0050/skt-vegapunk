using System.Text.RegularExpressions;

namespace SktVegapunk.Core.Pipeline.Spec;

public sealed class JspExtractor : IJspExtractor
{
    private static readonly Regex _componentVariableRegex = new(
        @"^\s*(?<type>\w+)\s+(?<variable>\w+)\s*=\s*[^;\r\n]*;",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    private static readonly Regex _componentCallRegex = new(
        @"(?<receiver>\w+)\.(?<method>(?:of|uf)_\w+)\s*\(",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex _getParameterRegex = new(
        @"request\.getParameter\s*\(\s*""(?<param>[^""]+)""\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public JspInvocation Extract(string jspText)
    {
        var receiverTypeMap = BuildReceiverTypeMap(jspText);
        var componentMatches = _componentCallRegex.Matches(jspText);
        string componentName = string.Empty;
        string methodName = string.Empty;
        var parameters = new List<string>();

        if (componentMatches.Count > 0)
        {
            var firstMatch = componentMatches[0];
            var receiver = firstMatch.Groups["receiver"].Value;
            componentName = receiverTypeMap.TryGetValue(receiver, out var componentType)
                ? componentType
                : receiver;
            methodName = firstMatch.Groups["method"].Value;

            // 提取目標 component 方法呼叫的參數
            var parenStart = firstMatch.Index + firstMatch.Length - 1;
            var parenEnd = FindMatchingParenthesis(jspText, parenStart);

            if (parenEnd > parenStart)
            {
                var paramsText = jspText.Substring(parenStart + 1, parenEnd - parenStart - 1).Trim();
                var parts = paramsText.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var trimmed = part.Trim();
                    if (!string.IsNullOrEmpty(trimmed) && trimmed != "null")
                    {
                        // 移除引號
                        if (trimmed.StartsWith("\"") && trimmed.EndsWith("\""))
                        {
                            trimmed = trimmed.Substring(1, trimmed.Length - 2);
                        }
                        parameters.Add(trimmed);
                    }
                }
            }
        }

        // 提取 HTTP 參數
        var httpParameters = new List<string>();
        foreach (Match match in _getParameterRegex.Matches(jspText))
        {
            httpParameters.Add(match.Groups["param"].Value);
        }

        return new JspInvocation(
            JspFileName: "", // 由外部設定
            componentName,
            methodName,
            parameters,
            httpParameters);
    }

    private static Dictionary<string, string> BuildReceiverTypeMap(string jspText)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in _componentVariableRegex.Matches(jspText))
        {
            var variable = match.Groups["variable"].Value;
            var type = match.Groups["type"].Value;
            if (string.IsNullOrWhiteSpace(variable) || string.IsNullOrWhiteSpace(type))
            {
                continue;
            }

            map[variable] = type;
        }

        return map;
    }

    private static int FindMatchingParenthesis(string text, int startIndex)
    {
        var depth = 1;
        for (var i = startIndex + 1; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '(') depth++;
            else if (c == ')' && --depth == 0) return i;
        }
        return -1;
    }
}
