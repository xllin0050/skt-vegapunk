using SktVegapunk.Core.Pipeline.Spec;

namespace SktVegapunk.Tests.Pipeline.Spec;

public sealed class JspPrototypeExtractorTests
{
    [Fact]
    public void Extract_應提取HtmlJsCss原型與表單資訊()
    {
        const string jspText = """
<%@ page contentType="text/html; charset=UTF-8" %>
<html>
<head>
<link rel="stylesheet" href="../style/main.css">
<style>
body { color: red; }
</style>
<script src="../script/app.js"></script>
<script>
function goNext() { document.forms[0].submit(); }
</script>
</head>
<body>
<form id="thisform" name="thisform" method="post" action="sign_00.jsp">
<% out.print("<input type='hidden' name='agent'>"); %>
</form>
</body>
</html>
""";

        var extractor = new JspPrototypeExtractor(new JspExtractor());

        var result = extractor.Extract(jspText);

        Assert.Contains("<!-- JSP_SCRIPTLET -->", result.HtmlPrototype, StringComparison.Ordinal);
        Assert.Contains("function goNext()", result.JavaScriptPrototype, StringComparison.Ordinal);
        Assert.Contains("body { color: red; }", result.CssPrototype, StringComparison.Ordinal);
        Assert.Contains("../script/app.js", result.ScriptSources);
        Assert.Contains("../style/main.css", result.StyleSources);
        var form = Assert.Single(result.Forms);
        Assert.Equal("thisform", form.Id);
        Assert.Equal("post", form.Method);
        Assert.Equal("sign_00.jsp", form.Action);
        Assert.Contains(result.Controls, control => control.TagName == "input" && control.Name == "agent" && control.FormKey == "thisform");
    }

    [Fact]
    public void Extract_應提取互動事件結構()
    {
        const string jspText = """
<input type="button" id="search" onclick="find_person(1)">
<script>
thisform.action = "sign_ins.jsp";
thisform.submit();
$.ajax({
    url:'sign_pick_api_3.jsp',
    type:"post",
    dataType:"text"
});
window.open("sign_select.jsp?x=1","select");
top.location.href='../index.html';
</script>
""";

        var extractor = new JspPrototypeExtractor(new JspExtractor());

        var result = extractor.Extract(jspText);

        Assert.Contains(result.Events, evt => evt.Kind == "Click" && evt.Target == "search");
        Assert.Contains(result.Events, evt => evt.Kind == "FormActionChange" && evt.Value!.Contains("sign_ins.jsp", StringComparison.Ordinal));
        Assert.Contains(result.Events, evt => evt.Kind == "Submit" && evt.Target == "thisform");
        Assert.Contains(result.Events, evt => evt.Kind == "Ajax" && evt.Target!.Contains("sign_pick_api_3.jsp", StringComparison.Ordinal));
        Assert.Contains(result.Events, evt => evt.Kind == "OpenWindow" && evt.Value!.Contains("sign_select.jsp", StringComparison.Ordinal));
        Assert.Contains(result.Events, evt => evt.Kind == "Navigate" && evt.Target == "top.location.href");
    }
}
