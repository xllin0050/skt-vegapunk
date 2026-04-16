using SktVegapunk.Core.Pipeline.Spec;

namespace SktVegapunk.Tests.Pipeline.Spec;

public sealed class SchemaExtractorTests
{
    private static readonly SchemaExtractor _extractor = new();

    private const string _simpleDdl = """
        -- DDL for Table 'tpec.dbo.s99_sign_kind'
        create table s99_sign_kind (
            sign_kind                       varchar(5)                       not null  ,
            sign_kind_name                  varchar(60)                          null  ,
            sort_seq                        int                                  null  ,
            upd_name                        varchar(18)                      DEFAULT  '' null ,
                CONSTRAINT PK_s99_sign_kind PRIMARY KEY CLUSTERED ( sign_kind )  on 'default'
        )
        lock allpages
        go

        -- DDL for Index 'idx_sign_kind_name'
        print 'creating index'
        create nonclustered index idx_sign_kind_name
        on tpec.dbo.s99_sign_kind(sign_kind_name)
        go

        -- DDL for Trigger 'tpec.dbo.tg_s99_sign_kind_upd'
        setuser 'dbo'
        create trigger dbo.tg_s99_sign_kind_upd
        on dbo.s99_sign_kind
        for UPDATE
        AS BEGIN
            INSERT INTO audit_log SELECT * FROM inserted
        END
        setuser
        """;

    [Fact]
    public void Extract_應解析資料表欄位與PK()
    {
        var result = _extractor.Extract(_simpleDdl);

        var table = Assert.Single(result.Tables);
        Assert.Equal("s99_sign_kind", table.TableName);
        Assert.Equal(4, table.Columns.Count);
        Assert.Contains(table.Columns, c => c.Name == "sign_kind" && !c.Nullable);
        Assert.Contains(table.Columns, c => c.Name == "sort_seq" && c.Nullable);
        Assert.Single(table.PrimaryKey);
        Assert.Equal("sign_kind", table.PrimaryKey[0]);
    }

    [Fact]
    public void Extract_應解析欄位的DefaultValue()
    {
        var result = _extractor.Extract(_simpleDdl);
        var table = Assert.Single(result.Tables);
        var updNameCol = table.Columns.FirstOrDefault(c => c.Name == "upd_name");
        Assert.NotNull(updNameCol);
        Assert.Equal(string.Empty, updNameCol.DefaultValue);
    }

    [Fact]
    public void Extract_應解析Trigger並包含事件與body()
    {
        var result = _extractor.Extract(_simpleDdl);

        var trigger = Assert.Single(result.Triggers);
        Assert.Equal("tg_s99_sign_kind_upd", trigger.TriggerName);
        Assert.Equal("s99_sign_kind", trigger.TableName);
        Assert.Contains("UPDATE", trigger.Events);
        Assert.Contains("INSERT INTO audit_log", trigger.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Extract_應解析StandaloneIndex並合併進資料表()
    {
        var result = _extractor.Extract(_simpleDdl);

        // standalone indexes list
        var idx = Assert.Single(result.StandaloneIndexes);
        Assert.Equal("idx_sign_kind_name", idx.Name);
        Assert.Contains("sign_kind_name", idx.Columns);
        Assert.False(idx.Clustered);

        // 同時合併進 table
        var table = Assert.Single(result.Tables);
        Assert.Contains(table.Indexes, i => i.Name == "idx_sign_kind_name");
    }

    [Fact]
    public void Extract_多張資料表均應各自解析()
    {
        var ddl = """
            -- DDL for Table 'tpec.dbo.t_a'
            create table t_a (
                id varchar(10) not null ,
                CONSTRAINT PK_t_a PRIMARY KEY CLUSTERED ( id ) on 'default'
            )
            go

            -- DDL for Table 'tpec.dbo.t_b'
            create table t_b (
                code varchar(5) not null ,
                name varchar(50) null
            )
            go
            """;

        var result = _extractor.Extract(ddl);
        Assert.Equal(2, result.Tables.Count);
        Assert.Contains(result.Tables, t => t.TableName == "t_a");
        Assert.Contains(result.Tables, t => t.TableName == "t_b");
    }

    [Fact]
    public void Extract_表格帶有CHECK約束時應解析()
    {
        var ddl = """
            -- DDL for Table 'tpec.dbo.s99_status'
            create table s99_status (
                status varchar(1) not null ,
                CONSTRAINT PK_s99_status PRIMARY KEY CLUSTERED ( status ) on 'default' ,
                CONSTRAINT chk_status CHECK ( status IN ('0','1') )
            )
            go
            """;

        var result = _extractor.Extract(ddl);
        var table = Assert.Single(result.Tables);
        var chk = Assert.Single(table.CheckConstraints);
        Assert.Equal("chk_status", chk.Name);
    }

    [Fact]
    public void Extract_觸發多個事件的Trigger應解析全部事件()
    {
        var ddl = """
            -- DDL for Trigger 'tpec.dbo.tg_multi'
            setuser 'dbo'
            create trigger dbo.tg_multi
            on dbo.some_table
            for INSERT, UPDATE, DELETE
            AS BEGIN
                SELECT 1
            END
            setuser
            """;

        var result = _extractor.Extract(ddl);
        var trigger = Assert.Single(result.Triggers);
        Assert.Contains("INSERT", trigger.Events);
        Assert.Contains("UPDATE", trigger.Events);
        Assert.Contains("DELETE", trigger.Events);
    }
}
