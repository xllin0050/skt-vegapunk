using SktVegapunk.Core.Pipeline;
using SktVegapunk.Core.Pipeline.Spec;

namespace SktVegapunk.Tests.Pipeline.Spec;

public sealed class SruExtractorTests
{
    private static string FixtureRoot => Path.Combine(AppContext.BaseDirectory, "../../../../source/sign");

    [Fact]
    public void Extract_應支援無回傳型別的SubroutinePrototype()
    {
        var text = ReadNormalizedPbText(Path.Combine(FixtureRoot, "sky_webbase/n_sky_webbase.sru"));
        var extractor = new SruExtractor(new PbScriptExtractor());

        var result = extractor.Extract(text);
        var prototype = Assert.Single(result.Prototypes, item => item.Name == "of_getyms");

        Assert.False(prototype.IsFunction);
        Assert.True(string.IsNullOrEmpty(prototype.ReturnType));
        Assert.Equal(3, prototype.Parameters.Count);
        Assert.Contains(result.Routines, routine => routine.Prototype.Name == "of_getyms");
    }

    [Fact]
    public void Extract_Routine不應誤抓ForwardPrototypes()
    {
        var text = ReadNormalizedPbText(Path.Combine(FixtureRoot, "sky_webbase/uo_ds.sru"));
        var extractor = new SruExtractor(new PbScriptExtractor());

        var result = extractor.Extract(text);
        var routines = result.Routines.Where(item => item.Prototype.Name == "of_geterror").ToList();

        Assert.Single(routines);
        Assert.Contains("ai_errorcode", routines[0].Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Extract_應以libraryexport與dataobject辨識DataWindow名稱()
    {
        // retrieve(arg) 的引數是檢索值（如 as_empid），不應被誤判為 DataWindow。
        var pbText = """
            forward
            global type n_dummy from nonvisualobject
            end type
            end forward

            global type n_dummy from nonvisualobject
            end type
            global n_dummy n_dummy

            forward prototypes
            public function integer of_run (string as_empid)
            end prototypes

            public function integer of_run (string as_empid);
            datastore lds_data
            string ls_pblpath, ls_dwsyntax, ls_error
            lds_data = create datastore
            ls_dwsyntax = libraryexport(ls_pblpath, "d_sign_list", exportdatawindow!)
            lds_data.create(ls_dwsyntax, ls_error)
            lds_data.dataobject = 'd_sign_detail'
            lds_data.SetTransObject(SQLCA)
            lds_data.Retrieve(as_empid)
            return 1
            end function
            """;

        var extractor = new SruExtractor(new PbScriptExtractor());
        var result = extractor.Extract(pbText);
        var routine = Assert.Single(result.Routines, r => r.Prototype.Name == "of_run");

        Assert.Contains("d_sign_list", routine.ReferencedDataWindows);
        Assert.Contains("d_sign_detail", routine.ReferencedDataWindows);
        Assert.DoesNotContain("as_empid", routine.ReferencedDataWindows);
    }

    private static string ReadNormalizedPbText(string filePath)
    {
        var raw = File.ReadAllBytes(filePath);
        var normalizer = new PbSourceNormalizer();
        return normalizer.Normalize(raw, filePath).NormalizedText;
    }
}
