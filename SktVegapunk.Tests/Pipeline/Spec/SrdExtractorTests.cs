using SktVegapunk.Core.Pipeline;
using SktVegapunk.Core.Pipeline.Spec;

namespace SktVegapunk.Tests.Pipeline.Spec;

public sealed class SrdExtractorTests
{
    private static string FixtureRoot => Path.Combine(AppContext.BaseDirectory, "../../../../source/sign");

    [Fact]
    public void Extract_應支援無Key屬性的欄位與PbselectRetrieve()
    {
        var text = ReadNormalizedPbText(Path.Combine(FixtureRoot, "dw_sign/d_signkind.srd"));
        var extractor = new SrdExtractor();

        var result = extractor.Extract(text);

        Assert.Contains(result.Columns, column => column.Name == "sign_kind_name");
        Assert.Contains("PBSELECT(", result.RetrieveSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TABLE(NAME=~\"s99_sign_kind~\" )", result.RetrieveSql, StringComparison.Ordinal);
        Assert.Contains("s99_sign_kind", result.Tables);
    }

    [Fact]
    public void Extract_應完整提取多個Arguments()
    {
        var text = ReadNormalizedPbText(Path.Combine(FixtureRoot, "dw_sign/d_count.srd"));
        var extractor = new SrdExtractor();

        var result = extractor.Extract(text);

        Assert.Equal(2, result.Arguments.Count);
        Assert.Contains(result.Arguments, argument => argument.Name == "as_empid" && argument.Type == "string");
        Assert.Contains(result.Arguments, argument => argument.Name == "as_titid" && argument.Type == "string");
    }

    private static string ReadNormalizedPbText(string filePath)
    {
        var raw = File.ReadAllBytes(filePath);
        var normalizer = new PbSourceNormalizer();
        return normalizer.Normalize(raw, filePath).NormalizedText;
    }
}
