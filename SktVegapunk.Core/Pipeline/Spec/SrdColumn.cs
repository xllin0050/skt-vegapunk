namespace SktVegapunk.Core.Pipeline.Spec;

public sealed record SrdColumn(
    string Name,
    string DbName,
    string Type,
    int? MaxLength);
