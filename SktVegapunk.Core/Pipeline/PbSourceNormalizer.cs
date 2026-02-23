using System.Text;

namespace SktVegapunk.Core.Pipeline;

public sealed class PbSourceNormalizer : ISourceNormalizer
{
    private static readonly byte[] _misencodedBom = [(byte)0xC3, (byte)0xBF, (byte)0xC3, (byte)0xBE];
    private static readonly byte[] _utf16LeBom = [(byte)0xFF, (byte)0xFE];
    private static readonly Encoding _utf16LeEncoding = new UnicodeEncoding(false, true, true);
    private static readonly Encoding _utf8Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public SourceArtifact Normalize(byte[] rawBytes, string originalPath)
    {
        ArgumentNullException.ThrowIfNull(rawBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);

        var warnings = new List<string>();

        // 偵測編碼並取得需跳過的 BOM byte 數，再一次 slice 解碼，避免重複判斷
        var (encoding, skipBytes) = DetectEncoding(rawBytes);
        var text = Decode(rawBytes, encoding, skipBytes, warnings);

        return new SourceArtifact(
            OriginalPath: originalPath,
            NormalizedText: text,
            SourceEncoding: encoding.WebName,
            Warnings: warnings);
    }

    private static (Encoding Encoding, int SkipBytes) DetectEncoding(ReadOnlySpan<byte> rawBytes)
    {
        if (rawBytes.StartsWith(_misencodedBom))
        {
            // C3 BF C3 BE = UTF-16LE BOM（FF FE）被某工具以 ISO-8859-1 → UTF-8 誤轉碼後的結果
            return (_utf16LeEncoding, _misencodedBom.Length);
        }

        if (rawBytes.StartsWith(_utf16LeBom))
        {
            return (_utf16LeEncoding, _utf16LeBom.Length);
        }

        // 無已知 BOM，fallback 以 UTF-8 解碼
        return (_utf8Encoding, 0);
    }

    private static string Decode(
        ReadOnlySpan<byte> rawBytes,
        Encoding encoding,
        int skipBytes,
        ICollection<string> warnings)
    {
        try
        {
            // 直接以 span slice 傳入 GetString，避免 Skip().ToArray() 的多餘配置
            return encoding.GetString(rawBytes[skipBytes..]);
        }
        catch (DecoderFallbackException ex)
        {
            warnings.Add($"解碼失敗: {ex.Message}");
            return string.Empty;
        }
    }
}
