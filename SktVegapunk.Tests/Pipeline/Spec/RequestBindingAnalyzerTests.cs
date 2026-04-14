using SktVegapunk.Core.Pipeline.Spec;

namespace SktVegapunk.Tests.Pipeline.Spec;

public sealed class RequestBindingAnalyzerTests
{
    [Fact]
    public void Analyze_應對齊Component參數與Request來源()
    {
        var analyzer = new RequestBindingAnalyzer();
        var migrationSpec = new MigrationSpec(
            DataWindows: [],
            Components:
            [
                new SruSpec(
                    "sign/sign/n_sign.sru",
                    "n_sign",
                    "nonvisualobject",
                    [],
                    [
                        new SruPrototype(
                            "public",
                            "string",
                            "of_sign_00",
                            [
                                new SruParameter("as_pblpath", "string"),
                                new SruParameter("as_year", "string"),
                                new SruParameter("as_sms", "string"),
                                new SruParameter("as_loginid", "string"),
                                new SruParameter("as_agent", "string"),
                                new SruParameter("as_sign_kind", "string"),
                                new SruParameter("as_card_type", "string")
                            ],
                            true)
                    ],
                    [],
                    [])
            ],
            JspInvocations: [],
            EndpointCandidates:
            [
                new EndpointCandidate("sign/sign_00.jsp", "n_sign.of_sign_00", "POST", "/api/sign/00", EndpointStatus.Resolved)
            ],
            UnresolvedMethods: []);

        const string jspText = """
<form id="thisform" name="thisform" method="post" action="sign_00.jsp">
  <input type="hidden" name="agent" id="agent" />
  <input type="hidden" name="sign_kind" id="sign_kind" />
</form>
<%
ls_year = (String)session.getAttribute( "sysyear" ) ;
ls_sms = (String)session.getAttribute( "syssms" ) ;
ls_loginid = (String)session.getAttribute( "loginid" ) ;
ls_agent = (String)request.getParameter( "agent" ) ;
ls_sign_kind = (String)request.getParameter( "sign_kind" ) ;
ls_card_type = (String)request.getParameter( "card_type" ) ;
ls_pblpath = (String)application.getAttribute( "pblpath" ) ;
if((ls_card_type == null) || (ls_card_type.equals("")))
{
    ls_card_type = "1";
}
ls_getrtn = iJagComponent.of_sign_00(ls_pblpath,ls_year,ls_sms,ls_loginid,ls_agent,ls_sign_kind,ls_card_type);
%>
<script>
$.ajax({
  url:'sign_pick_api_1.jsp',
  type:'post',
  data:{agent:$("#agent").val(), sign_kind, dt:_dt}
})
var myForm = {}
myForm["dt"] = new Date().getTime();
myForm["emp"] = $('#agent').val();
$.ajax({
  url:'sign_pick_api_2.jsp',
  type:'post',
  data:myForm
})
thisform.action = "sign_ins.jsp";
thisform.submit();
</script>
""";

        var invocation = new JspInvocation(
            "sign/sign_00.jsp",
            "n_sign",
            "of_sign_00",
            ["ls_pblpath", "ls_year", "ls_sms", "ls_loginid", "ls_agent", "ls_sign_kind", "ls_card_type"],
            ["agent", "sign_kind", "card_type"]);
        var prototype = new JspPrototypeArtifact(
            "sign/sign_00.jsp",
            string.Empty,
            string.Empty,
            string.Empty,
            [
                new JspFormPrototype("thisform", "thisform", "post", "sign_00.jsp", null)
            ],
            [
                new JspControlPrototype("input", "hidden", "agent", "agent", null, null, "thisform", null),
                new JspControlPrototype("input", "hidden", "sign_kind", "sign_kind", null, null, "thisform", null)
            ],
            [
                new JspInteractionEvent(1, "FormActionChange", "script", "thisform", "\"sign_ins.jsp\"", "thisform.action = \"sign_ins.jsp\";"),
                new JspInteractionEvent(2, "Submit", "script", "thisform", null, "thisform.submit();")
            ],
            [],
            [],
            invocation.HttpParameters,
            invocation.ComponentName,
            invocation.MethodName);

        var artifacts = analyzer.Analyze(migrationSpec, [new JspSourceArtifact("sign/sign_00.jsp", jspText, invocation, prototype)]);
        var artifact = Assert.Single(artifacts);

        Assert.Equal("n_sign.of_sign_00", artifact.PbMethod);
        Assert.Equal(7, artifact.Parameters.Count);
        Assert.Equal("ApplicationAttribute", artifact.Parameters[0].SourceKind);
        Assert.Equal("pblpath", artifact.Parameters[0].SourceName);
        Assert.Equal("RequestParameter", artifact.Parameters[4].SourceKind);
        Assert.Equal("agent", artifact.Parameters[4].SourceName);
        Assert.Equal("RequestParameter", artifact.Parameters[6].SourceKind);
        Assert.Contains("fallback", artifact.Parameters[6].Note!, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(3, artifact.OutgoingRequests.Count);
        Assert.Contains(artifact.OutgoingRequests, request => request.Kind == "FormSubmit" && request.Target == "sign_ins.jsp");
        Assert.Contains(artifact.OutgoingRequests, request => request.Kind == "Ajax" && request.PayloadFields.Any(field => field.Name == "agent" && field.SourceControl == "agent"));
        Assert.Contains(artifact.OutgoingRequests, request => request.Kind == "Ajax" && request.Target == "sign_pick_api_2.jsp" && request.PayloadFields.Any(field => field.Name == "emp" && field.SourceControl == "agent"));
    }

    [Fact]
    public void Analyze_應追蹤GetBytes來源到Request參數()
    {
        var analyzer = new RequestBindingAnalyzer();
        var migrationSpec = new MigrationSpec(
            DataWindows: [],
            Components:
            [
                new SruSpec(
                    "sign/uo_sign_record.sru",
                    "uo_sign_record",
                    "nonvisualobject",
                    [],
                    [
                        new SruPrototype(
                            "public",
                            "string",
                            "uf_create_sign",
                            [
                                new SruParameter("flow_id", "string"),
                                new SruParameter("vou_subject", "blob")
                            ],
                            true)
                    ],
                    [],
                    [])
            ],
            JspInvocations: [],
            EndpointCandidates:
            [
                new EndpointCandidate("sign/createSign.jsp", "uo_sign_record.uf_create_sign", "POST", "/api/createsign", EndpointStatus.Resolved)
            ],
            UnresolvedMethods: []);

        const string jspText = """
<%
ls_flow_id = (String)request.getParameter("flow");
ls_vou_subject = (String)request.getParameter("subject");
byte [] lb_vou_subject = ls_vou_subject.getBytes("UTF-8");
ls_getrtn = iJagComponent.uf_create_sign(ls_flow_id, lb_vou_subject);
%>
""";

        var invocation = new JspInvocation(
            "sign/createSign.jsp",
            "uo_sign_record",
            "uf_create_sign",
            ["ls_flow_id", "lb_vou_subject"],
            ["flow", "subject"]);
        var prototype = new JspPrototypeArtifact(
            "sign/createSign.jsp",
            string.Empty,
            string.Empty,
            string.Empty,
            [],
            [],
            [],
            [],
            [],
            invocation.HttpParameters,
            invocation.ComponentName,
            invocation.MethodName);

        var artifact = Assert.Single(analyzer.Analyze(migrationSpec, [new JspSourceArtifact("sign/createSign.jsp", jspText, invocation, prototype)]));

        Assert.Equal("RequestParameter", artifact.Parameters[1].SourceKind);
        Assert.Equal("subject", artifact.Parameters[1].SourceName);
        Assert.Contains("blob", artifact.Parameters[1].Note!, StringComparison.OrdinalIgnoreCase);
    }
}
