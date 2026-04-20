using SktVegapunk.Core.Pipeline;
using SktVegapunk.Core.Pipeline.Spec;

namespace SktVegapunk.Tests.Pipeline.Spec;

public sealed class SpecReportBuilderTests
{
    [Fact]
    public void Build_遇到重複ClassName時不應拋例外且可依方法對齊()
    {
        var fileStore = new InMemoryTextFileStore();
        var builder = new SpecReportBuilder(fileStore);
        var components = new List<SruSpec>
        {
            CreateComponent("uo_ds_a.sru", "uo_ds", "of_geterror"),
            CreateComponent("uo_ds_b.sru", "uo_ds", "of_geterrortext")
        };
        var jspInvocations = new List<JspInvocation>
        {
            new("createSign.jsp", "uo_ds", "of_geterrortext", [], [])
        };

        var spec = builder.Build([], components, jspInvocations);
        var endpoint = Assert.Single(spec.EndpointCandidates);

        Assert.Equal(EndpointStatus.Resolved, endpoint.Status);
        Assert.Equal("uo_ds.of_geterrortext", endpoint.PbMethod);
        Assert.Empty(spec.UnresolvedMethods);
    }

    [Fact]
    public async Task WriteReportAsync_應透過ITextFileStore輸出並使用注入時間()
    {
        var fileStore = new InMemoryTextFileStore();
        var fixedUtcNow = new DateTimeOffset(2026, 2, 23, 15, 30, 0, TimeSpan.Zero);
        var builder = new SpecReportBuilder(fileStore, new FixedTimeProvider(fixedUtcNow));
        var spec = new MigrationSpec(
            DataWindows:
            [
                new SrdSpec(
                    FileName: "d_signkind.srd",
                    Columns: [new SrdColumn("sign_kind", "s99_sign_kind.sign_kind", "long", null)],
                    RetrieveSql: "SELECT sign_kind FROM s99_sign_kind",
                    Arguments: [],
                    Tables: ["s99_sign_kind"])
            ],
            Components:
            [
                CreateComponent("n_sign.sru", "n_sign", "of_sign_00")
            ],
            JspInvocations:
            [
                new JspInvocation("sign_00.jsp", "n_sign", "of_sign_00", ["ls_pblpath"], ["pblpath"])
            ],
            EndpointCandidates:
            [
                new EndpointCandidate("sign_00.jsp", "n_sign.of_sign_00", "GET", "/api/sign_00", EndpointStatus.Resolved)
            ],
            UnresolvedMethods: []);

        await builder.WriteReportAsync(spec, "/tmp/output/spec");

        Assert.Contains("/tmp/output/spec/datawindows/d_signkind.json", fileStore.WrittenPaths);
        Assert.Contains("/tmp/output/spec/components/n_sign.json", fileStore.WrittenPaths);
        Assert.Contains("/tmp/output/spec/report.md", fileStore.WrittenPaths);
        Assert.Contains("2026-02-23 15:30:00 UTC", fileStore.ContentByPath["/tmp/output/spec/report.md"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteReportAsync_應保留相對路徑避免同名檔案互相覆蓋()
    {
        var fileStore = new InMemoryTextFileStore();
        var builder = new SpecReportBuilder(fileStore);
        var spec = new MigrationSpec(
            DataWindows:
            [
                new SrdSpec("sign/dw_sign/d_list.srd", [], string.Empty, [], []),
                new SrdSpec("other/dw_sign/d_list.srd", [], string.Empty, [], [])
            ],
            Components:
            [
                CreateComponent("sign/webap/n_webap.sru", "n_webap", "of_a"),
                CreateComponent("legacy/webap/n_webap.sru", "n_webap", "of_b")
            ],
            JspInvocations: [],
            EndpointCandidates: [],
            UnresolvedMethods: []);

        await builder.WriteReportAsync(spec, "/tmp/output/spec");

        Assert.Contains("/tmp/output/spec/datawindows/sign/dw_sign/d_list.json", fileStore.WrittenPaths);
        Assert.Contains("/tmp/output/spec/datawindows/other/dw_sign/d_list.json", fileStore.WrittenPaths);
        Assert.Contains("/tmp/output/spec/components/sign/webap/n_webap.json", fileStore.WrittenPaths);
        Assert.Contains("/tmp/output/spec/components/legacy/webap/n_webap.json", fileStore.WrittenPaths);
    }

    private static SruSpec CreateComponent(string fileName, string className, string methodName)
    {
        return new SruSpec(
            FileName: fileName,
            ClassName: className,
            ParentClass: "nonvisualobject",
            InstanceVariables: [],
            Prototypes:
            [
                new SruPrototype("public", "string", methodName, [], true)
            ],
            Routines:
            [
                new SruRoutine(
                    Prototype: new SruPrototype("public", "string", methodName, [], true),
                    Body: "return \"ok\"",
                    ReferencedDataWindows: [],
                    ReferencedSql: [])
            ],
            EventBlocks: []);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class InMemoryTextFileStore : ITextFileStore
    {
        public Dictionary<string, string> ContentByPath { get; } = new(StringComparer.Ordinal);

        public IReadOnlyList<string> WrittenPaths => ContentByPath.Keys.ToList();

        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
        {
            if (!ContentByPath.TryGetValue(path, out var content))
            {
                throw new FileNotFoundException(path);
            }

            return Task.FromResult(content);
        }

        public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
        {
            ContentByPath[path] = content;
            return Task.CompletedTask;
        }

        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
        {
            if (!ContentByPath.TryGetValue(path, out var content))
            {
                throw new FileNotFoundException(path);
            }

            return Task.FromResult(System.Text.Encoding.UTF8.GetBytes(content));
        }
    }
}
