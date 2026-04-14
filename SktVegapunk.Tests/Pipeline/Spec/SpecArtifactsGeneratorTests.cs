using System.Text;
using SktVegapunk.Core.Pipeline;
using SktVegapunk.Core.Pipeline.Spec;

namespace SktVegapunk.Tests.Pipeline.Spec;

public sealed class SpecArtifactsGeneratorTests
{
    [Fact]
    public async Task GenerateAsync_應輸出報告與中介資料()
    {
        var rootPath = Path.Combine(Path.GetTempPath(), $"skt-vegapunk-spec-{Guid.NewGuid():N}");
        var sourcePath = Path.Combine(rootPath, "source");
        var outputPath = Path.Combine(rootPath, "output");

        Directory.CreateDirectory(Path.Combine(sourcePath, "sign", "dw_sign"));
        Directory.CreateDirectory(Path.Combine(sourcePath, "sign", "sign"));

        var utf16LeWithBom = Encoding.Unicode.GetPreamble()
            .Concat(Encoding.Unicode.GetBytes("""
datawindow(units=0 color=1073741824 processing=1 HTMLDW=no print.documentname="test")
column=(type=long name=sign_kind dbname="s99_sign_kind.sign_kind" )
retrieve="SELECT sign_kind FROM s99_sign_kind"
arguments=(("sign_kind", number))
"""))
            .ToArray();

        await File.WriteAllBytesAsync(
            Path.Combine(sourcePath, "sign", "dw_sign", "d_signkind.srd"),
            utf16LeWithBom);

        await File.WriteAllBytesAsync(
            Path.Combine(sourcePath, "sign", "sign", "n_sign.sru"),
            Encoding.Unicode.GetPreamble()
                .Concat(Encoding.Unicode.GetBytes("""
global type n_sign from nonvisualobject
end type
forward prototypes
public function string of_sign_00 ()
end prototypes
public function string of_sign_00 ();
return "ok"
end function
"""))
                .ToArray());

        await File.WriteAllTextAsync(
            Path.Combine(sourcePath, "sign", "createSign.jsp"),
            """
<%
n_sign inv_sign = create n_sign
string ls_result
ls_result = inv_sign.of_sign_00(request.getParameter("sign_kind"))
%>
""");

        try
        {
            var fileStore = new FileTextStore();
            var generator = new SpecArtifactsGenerator(
                fileStore,
                new PbSourceNormalizer(),
                new SrdExtractor(),
                new SruExtractor(new PbScriptExtractor()),
                new JspExtractor(),
                new JspPrototypeExtractor(new JspExtractor()),
                new SpecReportBuilder(fileStore),
                new UnresolvedEndpointAnalyzer(),
                new PageFlowAnalyzer(),
                new GenerationPhasePlanner(),
                new RequestBindingAnalyzer(),
                new ResponseClassificationAnalyzer(),
                new InteractionGraphAnalyzer());

            var result = await generator.GenerateAsync(sourcePath, outputPath);

            Assert.Equal(1, result.DataWindowCount);
            Assert.Equal(1, result.ComponentCount);
            Assert.Equal(1, result.JspInvocationCount);
            Assert.Equal(1, result.JspPrototypeCount);
            Assert.True(File.Exists(Path.Combine(outputPath, "spec", "report.md")));
            Assert.True(File.Exists(Path.Combine(outputPath, "spec", "datawindows", "sign", "dw_sign", "d_signkind.json")));
            Assert.True(File.Exists(Path.Combine(outputPath, "spec", "components", "sign", "sign", "n_sign.json")));
            Assert.True(File.Exists(Path.Combine(outputPath, "spec", "jsp", "sign", "createSign.html")));
            Assert.True(File.Exists(Path.Combine(outputPath, "spec", "unresolved-causes.md")));
            Assert.True(File.Exists(Path.Combine(outputPath, "spec", "generation-phase-plan.md")));
            Assert.True(File.Exists(Path.Combine(outputPath, "spec", "request-bindings.md")));
            Assert.True(File.Exists(Path.Combine(outputPath, "spec", "request-bindings.json")));
            Assert.True(File.Exists(Path.Combine(outputPath, "spec", "response-classifications.md")));
            Assert.True(File.Exists(Path.Combine(outputPath, "spec", "response-classifications.json")));
            Assert.True(File.Exists(Path.Combine(outputPath, "spec", "control-inventory.md")));
            Assert.True(File.Exists(Path.Combine(outputPath, "spec", "control-inventory.json")));
            Assert.True(File.Exists(Path.Combine(outputPath, "spec", "payload-mappings.md")));
            Assert.True(File.Exists(Path.Combine(outputPath, "spec", "payload-mappings.json")));
            Assert.True(File.Exists(Path.Combine(outputPath, "spec", "interaction-graph.md")));
            Assert.True(File.Exists(Path.Combine(outputPath, "spec", "interaction-graph.json")));
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
