using SktVegapunk.Core.Pipeline.Spec;

namespace SktVegapunk.Tests.Pipeline.Spec;

public sealed class UnresolvedEndpointAnalyzerTests
{
    [Fact]
    public void Analyze_應區分缺Source與缺Prototype()
    {
        var sourceRoot = Path.Combine(Path.GetTempPath(), $"skt-vegapunk-unresolved-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceRoot);

        try
        {
            File.WriteAllText(Path.Combine(sourceRoot, "n_sign.sru"), "dummy");

            var spec = new MigrationSpec(
                DataWindows: [],
                Components:
                [
                    new SruSpec(
                        FileName: "n_sign.sru",
                        ClassName: "n_sign",
                        ParentClass: "nonvisualobject",
                        InstanceVariables: [],
                        Prototypes:
                        [
                            new SruPrototype("public", "string", "of_sign_00", [], true)
                        ],
                        Routines: [],
                        EventBlocks: [])
                ],
                JspInvocations: [],
                EndpointCandidates:
                [
                    new EndpointCandidate("sign_history_00.jsp", "n_sign_history.of_sign_history_00", "GET", "/api/x", EndpointStatus.Unresolved),
                    new EndpointCandidate("sign_select.jsp", "n_sign.of_sign_select", "GET", "/api/y", EndpointStatus.Unresolved)
                ],
                UnresolvedMethods:
                [
                    "n_sign_history.of_sign_history_00",
                    "n_sign.of_sign_select"
                ]);

            var analyzer = new UnresolvedEndpointAnalyzer();

            var findings = analyzer.Analyze(spec, sourceRoot);

            Assert.Collection(
                findings,
                finding =>
                {
                    Assert.Equal("MissingComponentSource", finding.RootCause);
                    Assert.Contains("n_sign_history.sru", finding.Detail, StringComparison.Ordinal);
                },
                finding =>
                {
                    Assert.Equal("MissingPrototype", finding.RootCause);
                    Assert.Contains("of_sign_select", finding.Detail, StringComparison.Ordinal);
                });
        }
        finally
        {
            if (Directory.Exists(sourceRoot))
            {
                Directory.Delete(sourceRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void GenerateMarkdown_應標示DeferredPlaceholder策略()
    {
        var analyzer = new UnresolvedEndpointAnalyzer();

        var markdown = analyzer.GenerateMarkdown(
        [
            new UnresolvedEndpointFinding(
                "sign/sign_select.jsp",
                "n_sign.of_sign_select",
                "MissingPrototype",
                "n_sign.sru 的 forward prototypes 中找不到 of_sign_select")
        ]);

        Assert.Contains("Unresolved Endpoint Placeholders", markdown, StringComparison.Ordinal);
        Assert.Contains("不阻塞 generation phase", markdown, StringComparison.Ordinal);
        Assert.Contains("| Stub | MissingPrototype |", markdown, StringComparison.Ordinal);
    }
}
