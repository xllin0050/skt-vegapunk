using SktVegapunk.Core.Pipeline.Spec;

namespace SktVegapunk.Tests.Pipeline.Spec;

public sealed class JspExtractorTests
{
    private static string FixtureRoot => Path.Combine(AppContext.BaseDirectory, "../../../../source/sign");

    [Fact]
    public void Extract_應優先解析CorbaComponent呼叫而非ServletApi()
    {
        var text = File.ReadAllText(Path.Combine(FixtureRoot, "sign_00.jsp"));
        var extractor = new JspExtractor();

        var result = extractor.Extract(text);

        Assert.Equal("n_sign", result.ComponentName);
        Assert.Equal("of_sign_00", result.MethodName);
        Assert.Contains("ls_pblpath", result.Parameters);
        Assert.Contains("agent", result.HttpParameters);
    }

    [Fact]
    public void Extract_應回傳元件型別名稱而非變數名稱()
    {
        var text = File.ReadAllText(Path.Combine(FixtureRoot, "createSign.jsp"));
        var extractor = new JspExtractor();

        var result = extractor.Extract(text);

        Assert.Equal("uo_sign_record", result.ComponentName);
        Assert.Equal("uf_create_sign", result.MethodName);
        Assert.Contains("flow", result.HttpParameters);
        Assert.Contains("lb_vou_subject", result.Parameters);
    }
}
