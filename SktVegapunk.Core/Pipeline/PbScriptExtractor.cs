using System.Text;

namespace SktVegapunk.Core.Pipeline;

public sealed class PbScriptExtractor : IPbScriptExtractor
{
    public IReadOnlyList<PbEventBlock> Extract(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var blocks = new List<PbEventBlock>();
        using var reader = new StringReader(source);

        var state = ParserState.Scanning;
        var currentEventName = string.Empty;
        var scriptBuffer = new StringBuilder();

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmedLine = line.Trim();

            switch (state)
            {
                case ParserState.Scanning:
                    if (TryParseEventStart(trimmedLine, out var eventName))
                    {
                        currentEventName = eventName;
                        scriptBuffer.Clear();
                        state = ParserState.InEventScript;
                    }

                    break;

                case ParserState.InEventScript:
                    if (IsEventEnd(trimmedLine))
                    {
                        var scriptBody = scriptBuffer.ToString().Trim();
                        if (!string.IsNullOrWhiteSpace(scriptBody))
                        {
                            blocks.Add(new PbEventBlock(currentEventName, scriptBody));
                        }

                        currentEventName = string.Empty;
                        scriptBuffer.Clear();
                        state = ParserState.Scanning;
                    }
                    else
                    {
                        scriptBuffer.AppendLine(line);
                    }

                    break;
            }
        }

        return blocks;
    }

    private static bool TryParseEventStart(string line, out string eventName)
    {
        eventName = string.Empty;

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        const string eventPrefix = "event ";
        const string onPrefix = "on ";

        string? rawName = null;
        if (line.StartsWith(eventPrefix, StringComparison.OrdinalIgnoreCase))
        {
            rawName = line[eventPrefix.Length..];
        }
        else if (line.StartsWith(onPrefix, StringComparison.OrdinalIgnoreCase))
        {
            rawName = line[onPrefix.Length..];
        }

        if (rawName is null)
        {
            return false;
        }

        var normalizedName = rawName.Trim().TrimEnd(';').Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return false;
        }

        eventName = normalizedName;
        return true;
    }

    private static bool IsEventEnd(string line) =>
        line.Equals("end event", StringComparison.OrdinalIgnoreCase)
        || line.Equals("end on", StringComparison.OrdinalIgnoreCase);

    private enum ParserState
    {
        Scanning,
        InEventScript
    }
}
