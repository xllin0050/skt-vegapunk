using SktVegapunk.Core.Pipeline.Spec;

namespace SktVegapunk.Tests.Pipeline.Spec;

public sealed class ResponseClassificationAnalyzerTests
{
    [Fact]
    public void Analyze_應依PBRoutine推斷回應型態()
    {
        var analyzer = new ResponseClassificationAnalyzer();
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
                        new SruPrototype("public", "string", "of_sign_api", [new SruParameter("as_empid", "string")], true),
                        new SruPrototype("public", "string", "of_sign_doc", [new SruParameter("as_serno", "string")], true),
                        new SruPrototype("public", "string", "of_sign_ins", [new SruParameter("as_value", "string")], true)
                    ],
                    [
                        new SruRoutine(
                            new SruPrototype("public", "string", "of_sign_api", [new SruParameter("as_empid", "string")], true),
                            "shtml += '[{\"ShowText\":\"demo\"}]'~r~n return shtml",
                            [],
                            []),
                        new SruRoutine(
                            new SruPrototype("public", "string", "of_sign_doc", [new SruParameter("as_serno", "string")], true),
                            "FileOpen(ls_path,StreamMode!,Write!,LockWrite!,Replace!)~r~nreturn shtml",
                            [],
                            []),
                        new SruRoutine(
                            new SruPrototype("public", "string", "of_sign_ins", [new SruParameter("as_value", "string")], true),
                            "return '<script>alert(\"ok\");history.back();</script>'",
                            [],
                            [])
                    ],
                    [])
            ],
            JspInvocations: [],
            EndpointCandidates:
            [
                new EndpointCandidate("sign/sign_api.jsp", "n_sign.of_sign_api", "GET", "/api/sign_api", EndpointStatus.Resolved),
                new EndpointCandidate("sign/sign_doc.jsp", "n_sign.of_sign_doc", "GET", "/api/sign_doc", EndpointStatus.Resolved),
                new EndpointCandidate("sign/sign_ins.jsp", "n_sign.of_sign_ins", "POST", "/api/sign_ins", EndpointStatus.Resolved)
            ],
            UnresolvedMethods: []);
        IReadOnlyList<JspSourceArtifact> jspSources =
        [
            new JspSourceArtifact("sign/sign_api.jsp", "<% out.print(ls_getrtn); %>", new JspInvocation("sign/sign_api.jsp", "n_sign", "of_sign_api", ["ls_empid"], ["id"]), new JspPrototypeArtifact("sign/sign_api.jsp", "", "", "", [], [], [], [], [], ["id"], "n_sign", "of_sign_api")),
            new JspSourceArtifact("sign/sign_doc.jsp", "<% out.print(ls_getrtn); %>", new JspInvocation("sign/sign_doc.jsp", "n_sign", "of_sign_doc", ["ls_serno"], ["serno"]), new JspPrototypeArtifact("sign/sign_doc.jsp", "", "", "", [], [], [], [], [], ["serno"], "n_sign", "of_sign_doc")),
            new JspSourceArtifact("sign/sign_ins.jsp", "<% out.print(ls_getrtn); %>", new JspInvocation("sign/sign_ins.jsp", "n_sign", "of_sign_ins", ["ls_value"], ["content"]), new JspPrototypeArtifact("sign/sign_ins.jsp", "", "", "", [], [], [], [], [], ["content"], "n_sign", "of_sign_ins"))
        ];

        var artifacts = analyzer.Analyze(migrationSpec, jspSources);

        Assert.Contains(artifacts, artifact => artifact.JspSource == "sign/sign_api.jsp" && artifact.ResponseKind == "json");
        Assert.Contains(artifacts, artifact => artifact.JspSource == "sign/sign_doc.jsp" && artifact.ResponseKind == "file");
        Assert.Contains(artifacts, artifact => artifact.JspSource == "sign/sign_ins.jsp" && artifact.ResponseKind == "script-redirect");
    }
}
