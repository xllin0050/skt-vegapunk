namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// 單一 endpoint 對應的 DataWindow 清單。
/// </summary>
public sealed record EndpointDataWindowMapEntry(
    string SuggestedRoute,
    string PbMethod,
    IReadOnlyList<string> DataWindowNames);
