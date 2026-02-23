using System.Text;
using SktVegapunk.Core.Pipeline;

namespace SktVegapunk.Tests.Pipeline;

public sealed class PbSourceNormalizerTests
{
    private static string FixtureRoot => Path.Combine(AppContext.BaseDirectory, "../../../../source/sign");

    [Fact]
    public void Normalize_ShouldDecode_MisencodedBom_Utf16Le()
    {
        var raw = File.ReadAllBytes(Path.Combine(FixtureRoot, "dw_sign/d_signkind.srd"));
        var normalizer = new PbSourceNormalizer();

        var artifact = normalizer.Normalize(raw, "d_signkind.srd");

        Assert.Equal("utf-16", artifact.SourceEncoding);
        Assert.Contains("PBExportHeader", artifact.NormalizedText);
        Assert.Contains("sign_kind_name", artifact.NormalizedText);
        Assert.Empty(artifact.Warnings);
    }

    [Fact]
    public void Normalize_ShouldDecode_MisencodedBom_Sru()
    {
        var raw = File.ReadAllBytes(Path.Combine(FixtureRoot, "sign/n_sign.sru"));
        var normalizer = new PbSourceNormalizer();

        var artifact = normalizer.Normalize(raw, "n_sign.sru");

        Assert.Equal("utf-16", artifact.SourceEncoding);
        Assert.Contains("forward", artifact.NormalizedText);
        Assert.Contains("of_sign_00", artifact.NormalizedText);
        Assert.Empty(artifact.Warnings);
    }

    [Fact]
    public void Normalize_ShouldDecode_StandardUtf16LeBom()
    {
        var text = "hello PB";
        var bom = new byte[] { 0xFF, 0xFE };
        var content = Encoding.Unicode.GetBytes(text);
        var raw = bom.Concat(content).ToArray();
        var normalizer = new PbSourceNormalizer();

        var artifact = normalizer.Normalize(raw, "standard_bom.srd");

        Assert.Equal("utf-16", artifact.SourceEncoding);
        Assert.Equal(text, artifact.NormalizedText);
        Assert.Empty(artifact.Warnings);
    }

    [Fact]
    public void Normalize_ShouldFallback_ToUtf8_WhenNoBom()
    {
        var text = "hello";
        var raw = Encoding.UTF8.GetBytes(text);
        var normalizer = new PbSourceNormalizer();

        var artifact = normalizer.Normalize(raw, "plain.txt");

        Assert.Equal("utf-8", artifact.SourceEncoding);
        Assert.Equal(text, artifact.NormalizedText);
        Assert.Empty(artifact.Warnings);
    }

    [Fact]
    public void Normalize_ShouldRecordWarning_WhenDecodeFails()
    {
        // 0xFF 開頭不匹配 C3 BF C3 BE（mis-encoded BOM），也不匹配 FF FE（標準 UTF-16LE BOM），
        // 因此走 UTF-8 fallback 路徑；0xFF 不是合法 UTF-8 起始 byte，觸發解碼失敗 warning。
        var raw = new byte[] { 0xFF, 0xFF, 0xFF };
        var normalizer = new PbSourceNormalizer();

        var artifact = normalizer.Normalize(raw, "invalid.bin");

        Assert.NotEmpty(artifact.Warnings);
        Assert.Equal(string.Empty, artifact.NormalizedText);
    }
}
