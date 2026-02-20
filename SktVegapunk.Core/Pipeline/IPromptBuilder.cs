namespace SktVegapunk.Core.Pipeline;

public interface IPromptBuilder
{
    string BuildInitialPrompt(IReadOnlyList<PbEventBlock> eventBlocks);

    string BuildRepairPrompt(string initialPrompt, string previousGeneratedCode, string validationOutput);
}
