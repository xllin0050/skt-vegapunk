using SktVegapunk.Core.Pipeline.Spec;

namespace SktVegapunk.Tests.Pipeline.Spec;

public sealed class EndpointDataWindowAnalyzerTests
{
    private static readonly EndpointDataWindowAnalyzer _analyzer = new();

    [Fact]
    public void Analyze_應從routine建立endpoint對DataWindow的對應()
    {
        var components = new List<SruSpec>
        {
            new SruSpec(
                FileName: "sign/n_sign.sru",
                ClassName: "n_sign",
                ParentClass: "nonvisualobject",
                InstanceVariables: [],
                Prototypes: [],
                Routines:
                [
                    new SruRoutine(
                        new SruPrototype("public", "string", "of_sign_00", [], true),
                        Body: "datawindow = dw_sign_list",
                        ReferencedDataWindows: ["dw_sign_list"],
                        ReferencedSql: [])
                ],
                EventBlocks: [])
        };

        var migrationSpec = new MigrationSpec(
            DataWindows: [],
            Components: components,
            JspInvocations: [],
            EndpointCandidates:
            [
                new EndpointCandidate(
                    JspSource: "createSign.jsp",
                    PbMethod: "n_sign.of_sign_00",
                    SuggestedHttpMethod: "GET",
                    SuggestedRoute: "/api/sign",
                    Status: EndpointStatus.Resolved)
            ],
            UnresolvedMethods: []);

        var result = _analyzer.Analyze(migrationSpec, components);

        var entry = Assert.Single(result);
        Assert.Equal("/api/sign", entry.SuggestedRoute);
        Assert.Equal("n_sign.of_sign_00", entry.PbMethod);
        Assert.Contains("dw_sign_list", entry.DataWindowNames);
    }

    [Fact]
    public void Analyze_Unresolved端點不應出現在結果中()
    {
        var components = new List<SruSpec>
        {
            new SruSpec(
                FileName: "sign/n_sign.sru",
                ClassName: "n_sign",
                ParentClass: "nonvisualobject",
                InstanceVariables: [],
                Prototypes: [],
                Routines:
                [
                    new SruRoutine(
                        new SruPrototype("public", "string", "of_sign_00", [], true),
                        Body: string.Empty,
                        ReferencedDataWindows: ["dw_x"],
                        ReferencedSql: [])
                ],
                EventBlocks: [])
        };

        var migrationSpec = new MigrationSpec(
            DataWindows: [],
            Components: components,
            JspInvocations: [],
            EndpointCandidates:
            [
                new EndpointCandidate(
                    JspSource: "sign.jsp",
                    PbMethod: "n_sign.of_sign_00",
                    SuggestedHttpMethod: "GET",
                    SuggestedRoute: "/api/sign",
                    Status: EndpointStatus.Unresolved)
            ],
            UnresolvedMethods: []);

        var result = _analyzer.Analyze(migrationSpec, components);
        Assert.Empty(result);
    }

    [Fact]
    public void Analyze_無DataWindow引用的routine不應出現在結果中()
    {
        var components = new List<SruSpec>
        {
            new SruSpec(
                FileName: "sign/n_sign.sru",
                ClassName: "n_sign",
                ParentClass: "nonvisualobject",
                InstanceVariables: [],
                Prototypes: [],
                Routines:
                [
                    new SruRoutine(
                        new SruPrototype("public", "string", "of_sign_00", [], true),
                        Body: "return 'ok'",
                        ReferencedDataWindows: [],
                        ReferencedSql: [])
                ],
                EventBlocks: [])
        };

        var migrationSpec = new MigrationSpec(
            DataWindows: [],
            Components: components,
            JspInvocations: [],
            EndpointCandidates:
            [
                new EndpointCandidate(
                    JspSource: "sign.jsp",
                    PbMethod: "n_sign.of_sign_00",
                    SuggestedHttpMethod: "GET",
                    SuggestedRoute: "/api/sign",
                    Status: EndpointStatus.Resolved)
            ],
            UnresolvedMethods: []);

        var result = _analyzer.Analyze(migrationSpec, components);
        Assert.Empty(result);
    }

    [Fact]
    public void GenerateMarkdown_應產生包含route與datawindow的表格()
    {
        var entries = new List<EndpointDataWindowMapEntry>
        {
            new EndpointDataWindowMapEntry(
                SuggestedRoute: "/api/sign",
                PbMethod: "n_sign.of_sign_00",
                DataWindowNames: ["dw_sign_list", "dw_sign_detail"])
        };

        var markdown = _analyzer.GenerateMarkdown(entries);
        Assert.Contains("/api/sign", markdown);
        Assert.Contains("dw_sign_list", markdown);
        Assert.Contains("dw_sign_detail", markdown);
    }
}
