namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// 描述單一 PB 參數的來源綁定。
/// </summary>
public sealed record RequestBindingParameter(
    int Position,
    string Name,
    string? Type,
    string SourceKind,
    string? SourceName,
    string Expression,
    string Confidence,
    string? Note);
