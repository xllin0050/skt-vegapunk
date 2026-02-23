namespace SktVegapunk.Core.Pipeline;

public static class SourceFileHelper
{
    /// <summary>
    /// 判斷指定路徑是否為已知的 PowerBuilder 匯出檔副檔名。
    /// </summary>
    public static bool HasPbExtension(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".sru" or ".srw" or ".srd" or ".srx" or ".srf" or ".srs";
    }
}
