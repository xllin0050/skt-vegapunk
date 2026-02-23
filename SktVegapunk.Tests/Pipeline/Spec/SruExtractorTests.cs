using SktVegapunk.Core.Pipeline;
using SktVegapunk.Core.Pipeline.Spec;

namespace SktVegapunk.Tests.Pipeline.Spec;

public sealed class SruExtractorTests
{
    private static string FixtureRoot => Path.Combine(AppContext.BaseDirectory, "../../../../source/sign");

    [Fact]
    public void Extract_應支援無回傳型別的SubroutinePrototype()
    {
        var text = ReadNormalizedPbText(Path.Combine(FixtureRoot, "sky_webbase/n_sky_webbase.sru"));
        var extractor = new SruExtractor(new PbScriptExtractor());

        var result = extractor.Extract(text);
        var prototype = Assert.Single(result.Prototypes, item => item.Name == "of_getyms");

        Assert.False(prototype.IsFunction);
        Assert.True(string.IsNullOrEmpty(prototype.ReturnType));
        Assert.Equal(3, prototype.Parameters.Count);
        Assert.Contains(result.Routines, routine => routine.Prototype.Name == "of_getyms");
    }

    [Fact]
    public void Extract_Routine不應誤抓ForwardPrototypes()
    {
        var text = ReadNormalizedPbText(Path.Combine(FixtureRoot, "sky_webbase/uo_ds.sru"));
        var extractor = new SruExtractor(new PbScriptExtractor());

        var result = extractor.Extract(text);
        var routines = result.Routines.Where(item => item.Prototype.Name == "of_geterror").ToList();

        Assert.Single(routines);
        Assert.Contains("ai_errorcode", routines[0].Body, StringComparison.Ordinal);
    }

    private static string ReadNormalizedPbText(string filePath)
    {
        var raw = File.ReadAllBytes(filePath);
        var normalizer = new PbSourceNormalizer();
        return normalizer.Normalize(raw, filePath).NormalizedText;
    }
}
