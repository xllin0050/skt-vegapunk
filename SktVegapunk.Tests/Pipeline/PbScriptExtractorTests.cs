using SktVegapunk.Core.Pipeline;

namespace SktVegapunk.Tests.Pipeline;

public sealed class PbScriptExtractorTests
{
    [Fact]
    public void Extract_從混合內容中提取事件區塊()
    {
        var source = """
            global type w_main from window
            integer width = 1200
            end type

            event clicked;
            string ls_name
            ls_name = sle_name.text
            end event

            on rowfocuschanged;
            long ll_id
            ll_id = dw_1.GetRow()
            end on
            """;

        var extractor = new PbScriptExtractor();

        var blocks = extractor.Extract(source);

        Assert.Equal(2, blocks.Count);
        Assert.Equal("clicked", blocks[0].EventName);
        Assert.Contains("ls_name = sle_name.text", blocks[0].ScriptBody);
        Assert.Equal("rowfocuschanged", blocks[1].EventName);
        Assert.Contains("ll_id = dw_1.GetRow()", blocks[1].ScriptBody);
    }

    [Fact]
    public void Extract_沒有事件時回傳空集合()
    {
        var source = """
            type cb_submit from commandbutton within w_main
            integer x = 100
            integer y = 200
            end type
            """;

        var extractor = new PbScriptExtractor();

        var blocks = extractor.Extract(source);

        Assert.Empty(blocks);
    }

    [Fact]
    public void Extract_保留事件順序()
    {
        var source = """
            event first;
            return
            end event
            event second;
            return
            end event
            """;

        var extractor = new PbScriptExtractor();

        var blocks = extractor.Extract(source);

        Assert.Collection(
            blocks,
            first => Assert.Equal("first", first.EventName),
            second => Assert.Equal("second", second.EventName));
    }
}
