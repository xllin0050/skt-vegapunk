namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// 從 Sybase ASE DDL SQL 文字提取 Schema 結構。
/// </summary>
public interface ISchemaExtractor
{
    SchemaArtifacts Extract(string ddlText);
}
