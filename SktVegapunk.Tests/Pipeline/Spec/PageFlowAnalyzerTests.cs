using SktVegapunk.Core.Pipeline.Spec;

namespace SktVegapunk.Tests.Pipeline.Spec;

public sealed class PageFlowAnalyzerTests
{
    [Fact]
    public void Analyze_應從事件推導頁面流程邊()
    {
        var graph = new PageFlowAnalyzer().Analyze(
            [
                new JspPrototypeArtifact(
                    JspFileName: "sign/sign_00.jsp",
                    HtmlPrototype: string.Empty,
                    JavaScriptPrototype: string.Empty,
                    CssPrototype: string.Empty,
                    Forms:
                    [
                        new JspFormPrototype("thisform", "thisform", "post", "sign_00.jsp", null)
                    ],
                    Events:
                    [
                        new JspInteractionEvent(1, "FormActionChange", "script", "thisform", "\"sign_dtl.jsp\"", "thisform.action = \"sign_dtl.jsp\";"),
                        new JspInteractionEvent(2, "Submit", "script", "thisform", null, "thisform.submit();"),
                        new JspInteractionEvent(3, "Ajax", "script", "'sign_pick_api_3.jsp'", "url='sign_pick_api_3.jsp'; method='post'; dataType='text'", "$.ajax(...)"),
                        new JspInteractionEvent(4, "Navigate", "script", "top.location.href", "'../index.html'", "top.location.href='../index.html';")
                    ],
                    ScriptSources: [],
                    StyleSources: [],
                    HttpParameters: [],
                    ComponentName: "n_sign",
                    MethodName: "of_sign_00")
            ],
            new MigrationSpec(
                DataWindows: [],
                Components: [],
                JspInvocations: [],
                EndpointCandidates:
                [
                    new EndpointCandidate("sign/sign_00.jsp", "n_sign.of_sign_00", "GET", "/api/sign_00", EndpointStatus.Resolved)
                ],
                UnresolvedMethods: []));

        Assert.Contains(graph.Edges, edge => edge.Kind == "Submit" && edge.Target == "sign_dtl.jsp");
        Assert.Contains(graph.Edges, edge => edge.Kind == "Ajax" && edge.Target == "sign_pick_api_3.jsp");
        Assert.Contains(graph.Edges, edge => edge.Kind == "Navigate" && edge.Target == "../index.html");
        Assert.Contains(graph.Edges, edge => edge.Kind == "ComponentCall" && edge.Target == "n_sign.of_sign_00");
    }
}
