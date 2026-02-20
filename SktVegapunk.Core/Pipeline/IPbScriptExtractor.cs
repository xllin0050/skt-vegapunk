namespace SktVegapunk.Core.Pipeline;

public interface IPbScriptExtractor
{
    IReadOnlyList<PbEventBlock> Extract(string source);
}
