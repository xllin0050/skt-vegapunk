using SktVegapunk.Core.Pipeline.Spec;

namespace SktVegapunk.Tests.Pipeline.Spec;

public sealed class GenerationPhasePlannerTests
{
    [Fact]
    public void GenerateMarkdown_應輸出進入GenerationPhase的補件計畫()
    {
        var planner = new GenerationPhasePlanner();
        var migrationSpec = new MigrationSpec(
            DataWindows:
            [
                new SrdSpec("sign/dw/d_demo.srd", [], "select * from demo_table", [], ["demo_table"])
            ],
            Components:
            [
                new SruSpec("sign/sign/n_demo.sru", "n_demo", "nonvisualobject", [], [], [], [])
            ],
            JspInvocations: [],
            EndpointCandidates:
            [
                new EndpointCandidate("sign/demo.jsp", "n_demo.of_demo", "GET", "/api/demo", EndpointStatus.Resolved),
                new EndpointCandidate("sign/missing.jsp", "n_missing.of_missing", "GET", "/api/missing", EndpointStatus.Unresolved)
            ],
            UnresolvedMethods:
            [
                "n_missing.of_missing"
            ]);
        IReadOnlyList<JspPrototypeArtifact> jspPrototypes =
        [
            new JspPrototypeArtifact(
                "sign/demo.jsp",
                "<form id=\"demo\"></form>",
                "function submitDemo() {}",
                string.Empty,
                [
                    new JspFormPrototype("demo", "demo", "post", "demo.jsp", null)
                ],
                [
                    new JspControlPrototype("input", "text", "demo_id", "demo_id", null, null, "demo", null)
                ],
                [
                    new JspInteractionEvent(1, "Submit", "submitDemo", "demo", "demo.jsp", "document.demo.submit();")
                ],
                [],
                [],
                [
                    "demo_id"
                ],
                "n_demo",
                "of_demo")
        ];
        var pageFlowGraph = new PageFlowGraph(
            ["sign/demo.jsp"],
            [
                new PageFlowEdge("sign/demo.jsp", "Submit", "demo", "demo.jsp", "demo.jsp")
            ]);
        IReadOnlyList<UnresolvedEndpointFinding> unresolvedFindings =
        [
            new UnresolvedEndpointFinding(
                "sign/missing.jsp",
                "n_missing.of_missing",
                "MissingComponentSource",
                "source/ 內找不到 n_missing.sru")
        ];
        IReadOnlyList<RequestBindingArtifact> requestBindings =
        [
            new RequestBindingArtifact(
                "sign/demo.jsp",
                "n_demo.of_demo",
                "GET",
                "/api/demo",
                "Resolved",
                [
                    new RequestBindingParameter(1, "as_demo", "string", "RequestParameter", "demo_id", "ls_demo_id", "Heuristic", null)
                ],
                [])
        ];
        IReadOnlyList<ResponseClassificationArtifact> responseClassifications =
        [
            new ResponseClassificationArtifact("sign/demo.jsp", "n_demo.of_demo", "GET", "/api/demo", "html", "Heuristic", "PB routine 內含 HTML 組裝線索")
        ];

        var markdown = planner.GenerateMarkdown(migrationSpec, jspPrototypes, pageFlowGraph, unresolvedFindings, requestBindings, responseClassifications);

        Assert.Contains("Generation Phase Plan", markdown, StringComparison.Ordinal);
        Assert.Contains("resolved endpoints + unresolved placeholders + frontend skeleton", markdown, StringComparison.Ordinal);
        Assert.Contains("已覆蓋 1 個 JSP component call", markdown, StringComparison.Ordinal);
        Assert.Contains("已覆蓋 1 個 endpoint", markdown, StringComparison.Ordinal);
        Assert.Contains("control inventory", markdown, StringComparison.Ordinal);
        Assert.Contains("payload mapping", markdown, StringComparison.Ordinal);
        Assert.Contains("`n_missing.of_missing`：先保留 stub", markdown, StringComparison.Ordinal);
    }
}
