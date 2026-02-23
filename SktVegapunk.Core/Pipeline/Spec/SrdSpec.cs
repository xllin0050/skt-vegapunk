namespace SktVegapunk.Core.Pipeline.Spec;

public sealed record SrdSpec(
    string FileName,
    IReadOnlyList<SrdColumn> Columns,
    string RetrieveSql,
    IReadOnlyList<SrdArgument> Arguments,
    IReadOnlyList<string> Tables);
