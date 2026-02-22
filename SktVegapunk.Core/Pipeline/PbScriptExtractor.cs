using System.Text;

namespace SktVegapunk.Core.Pipeline;

public sealed class PbScriptExtractor : IPbScriptExtractor
{
    public IReadOnlyList<PbEventBlock> Extract(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        // 用於收集所有成功解析的事件區塊
        var blocks = new List<PbEventBlock>();
        using var reader = new StringReader(source);

        // 初始化狀態機：Scanning（尋找事件開頭）、InEventScript（收集腳本內容）
        var state = ParserState.Scanning;
        var currentEventName = string.Empty;
        var scriptBuffer = new StringBuilder();

        // 逐行解析，以狀態機驅動處理邏輯
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmedLine = line.Trim();

            switch (state)
            {
                case ParserState.Scanning:
                    // 尋找事件開頭（event XXX 或 on XXX），找到後切換至收集模式
                    if (TryParseEventStart(trimmedLine, out var eventName))
                    {
                        currentEventName = eventName;
                        scriptBuffer.Clear();
                        state = ParserState.InEventScript;
                    }

                    break;

                case ParserState.InEventScript:
                    // 遇到結束標記（end event 或 end on）時，將累積內容封裝為區塊
                    if (IsEventEnd(trimmedLine))
                    {
                        var scriptBody = scriptBuffer.ToString().Trim();
                        if (!string.IsNullOrWhiteSpace(scriptBody))
                        {
                            blocks.Add(new PbEventBlock(currentEventName, scriptBody));
                        }

                        // 重置狀態，繼續尋找下一個事件區塊
                        currentEventName = string.Empty;
                        scriptBuffer.Clear();
                        state = ParserState.Scanning;
                    }
                    else
                    {
                        // 保留原始縮排與格式，將非結束標記的行累積至緩衝區
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
