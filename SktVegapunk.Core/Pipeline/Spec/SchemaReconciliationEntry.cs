namespace SktVegapunk.Core.Pipeline.Spec;

/// <summary>
/// 單一資料表的 SrdSpec 與 Schema 比對結果。
/// SrdSpec 僅描述 DataWindow 讀取的欄位集合，無 PK 資訊，因此此處只記錄 Schema 側的 PK。
/// </summary>
public sealed record SchemaReconciliationEntry(
    string TableName,
    IReadOnlyList<string> ColumnsOnlyInSrd,
    IReadOnlyList<string> ColumnsOnlyInSchema,
    IReadOnlyList<SchemaColumnTypeMismatch> TypeMismatches,
    IReadOnlyList<string> SchemaPrimaryKey,
    bool TableExistsInSchema);

/// <summary>
/// 同一欄位在 SrdSpec 與 Schema 中型別不一致的記錄。
/// </summary>
public sealed record SchemaColumnTypeMismatch(
    string ColumnName,
    string SrdType,
    string SchemaType);
