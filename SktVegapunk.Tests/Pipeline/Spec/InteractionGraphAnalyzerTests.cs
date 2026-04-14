using SktVegapunk.Core.Pipeline.Spec;

namespace SktVegapunk.Tests.Pipeline.Spec;

public sealed class InteractionGraphAnalyzerTests
{
    [Fact]
    public void Analyze_應串出Click到Action的事件鏈()
    {
        var analyzer = new InteractionGraphAnalyzer();
        var prototype = new JspPrototypeArtifact(
            JspFileName: "sign/sign_dtl.jsp",
            HtmlPrototype: "<input type=\"button\" id=\"nextBtn\" onclick=\"goNext()\">",
            JavaScriptPrototype: """
function goNext(){
    thisform.action = "sign_ins.jsp";
    thisform.submit();
}
""",
            CssPrototype: string.Empty,
            Forms:
            [
                new JspFormPrototype("thisform", "thisform", "post", "sign_dtl.jsp", null)
            ],
            Controls:
            [
                new JspControlPrototype("input", "button", "nextBtn", null, null, null, null, "goNext()")
            ],
            Events:
            [
                new JspInteractionEvent(1, "Click", "goNext()", "nextBtn", null, "<input ... onclick=\"goNext()\">"),
                new JspInteractionEvent(2, "FormActionChange", "script", "thisform", "\"sign_ins.jsp\"", "thisform.action = \"sign_ins.jsp\";"),
                new JspInteractionEvent(3, "Submit", "script", "thisform", null, "thisform.submit();")
            ],
            ScriptSources: [],
            StyleSources: [],
            HttpParameters: [],
            ComponentName: "n_sign",
            MethodName: "of_sign_dtl");

        var graph = analyzer.Analyze([prototype]);

        Assert.Contains(graph.Edges, edge =>
            edge.JspFileName == "sign/sign_dtl.jsp"
            && edge.ClickTarget == "nextBtn"
            && edge.Handler == "goNext"
            && edge.ActionKind == "Submit"
            && edge.ActionTarget == "thisform");
    }
}
