using System.Text;

namespace SktVegapunk.Core.Pipeline;

public sealed class PromptBuilder : IPromptBuilder
{
    public string BuildInitialPrompt(IReadOnlyList<PbEventBlock> eventBlocks)
    {
        ArgumentNullException.ThrowIfNull(eventBlocks);
        if (eventBlocks.Count == 0)
        {
            throw new ArgumentException("至少要包含一個可轉換的事件區塊。", nameof(eventBlocks));
        }

        var builder = new StringBuilder();
        builder.AppendLine("以下是從 PowerBuilder 提取的事件邏輯，請轉換為可編譯的 C# 後端程式碼。");
        builder.AppendLine("請保留業務邏輯語意，並輸出完整程式碼。");
        builder.AppendLine();

        foreach (var block in eventBlocks)
        {
            builder.AppendLine($"[Event:{block.EventName}]");
            builder.AppendLine(block.ScriptBody);
            builder.AppendLine("[/Event]");
            builder.AppendLine();
        }

        return builder.ToString().Trim();
    }

    public string BuildRepairPrompt(string initialPrompt, string previousGeneratedCode, string validationOutput)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(initialPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(previousGeneratedCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(validationOutput);

        var builder = new StringBuilder();
        builder.AppendLine("前一次輸出的 C# 程式碼驗證失敗，請根據錯誤訊息修正並重新輸出完整程式碼。");
        builder.AppendLine();
        builder.AppendLine("=== 原始轉換需求 ===");
        builder.AppendLine(initialPrompt);
        builder.AppendLine();
        builder.AppendLine("=== 前一次輸出程式碼 ===");
        builder.AppendLine(previousGeneratedCode);
        builder.AppendLine();
        builder.AppendLine("=== 驗證輸出 ===");
        builder.AppendLine(validationOutput);

        return builder.ToString().Trim();
    }
}
