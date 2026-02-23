using System.Text;
using SktVegapunk.Core.Pipeline;

namespace SktVegapunk.Tests.Pipeline;

public sealed class MigrationOrchestratorTests
{
    [Fact]
    public async Task RunAsync_首次驗證成功時直接完成()
    {
        var fileStore = new InMemoryTextFileStore(new Dictionary<string, string>
        {
            ["/tmp/source.srw"] = "event clicked;\nreturn\nend event"
        });
        var extractor = new StubPbScriptExtractor
        {
            Blocks = [new PbEventBlock("clicked", "return")]
        };
        var promptBuilder = new StubPromptBuilder("initial-prompt");
        var codeGenerator = new QueueCodeGenerator(["generated-v1"]);
        var buildValidator = new QueueBuildValidator(
            [new BuildValidationResult(true, "build ok")]);

        var orchestrator = new MigrationOrchestrator(
            fileStore,
            extractor,
            promptBuilder,
            codeGenerator,
            buildValidator);

        var result = await orchestrator.RunAsync(new MigrationRequest
        {
            SourceFilePath = "/tmp/source.srw",
            OutputFilePath = "/tmp/output.cs",
            TargetPath = "SktVegapunk.slnx",
            SystemPrompt = "system",
            MaxRetries = 3,
            BuildConfiguration = "Debug",
            RunTestsAfterBuild = false
        });

        Assert.Equal(MigrationState.Completed, result.FinalState);
        Assert.Equal(1, result.Attempts);
        Assert.Equal("generated-v1", fileStore.LastWrittenContent);
        Assert.Single(codeGenerator.ReceivedUserPrompts);
    }

    [Fact]
    public async Task RunAsync_先失敗再成功時會進行修復重試()
    {
        var fileStore = new InMemoryTextFileStore(new Dictionary<string, string>
        {
            ["/tmp/source.srw"] = "event clicked;\nreturn\nend event"
        });
        var extractor = new StubPbScriptExtractor
        {
            Blocks = [new PbEventBlock("clicked", "return")]
        };
        var promptBuilder = new StubPromptBuilder("initial-prompt");
        var codeGenerator = new QueueCodeGenerator(["bad-code", "good-code"]);
        var buildValidator = new QueueBuildValidator(
            [
                new BuildValidationResult(false, "build failed"),
                new BuildValidationResult(true, "build ok")
            ]);

        var orchestrator = new MigrationOrchestrator(
            fileStore,
            extractor,
            promptBuilder,
            codeGenerator,
            buildValidator);

        var result = await orchestrator.RunAsync(new MigrationRequest
        {
            SourceFilePath = "/tmp/source.srw",
            OutputFilePath = "/tmp/output.cs",
            TargetPath = "SktVegapunk.slnx",
            SystemPrompt = "system",
            MaxRetries = 3,
            BuildConfiguration = "Debug",
            RunTestsAfterBuild = false
        });

        Assert.Equal(MigrationState.Completed, result.FinalState);
        Assert.Equal(2, result.Attempts);
        Assert.Equal(["initial-prompt", "repair::build failed"], codeGenerator.ReceivedUserPrompts);
    }

    [Fact]
    public async Task RunAsync_達到最大重試次數後失敗()
    {
        var fileStore = new InMemoryTextFileStore(new Dictionary<string, string>
        {
            ["/tmp/source.srw"] = "event clicked;\nreturn\nend event"
        });
        var extractor = new StubPbScriptExtractor
        {
            Blocks = [new PbEventBlock("clicked", "return")]
        };
        var promptBuilder = new StubPromptBuilder("initial-prompt");
        var codeGenerator = new QueueCodeGenerator(["bad-1", "bad-2", "bad-3"]);
        var buildValidator = new QueueBuildValidator(
            [
                new BuildValidationResult(false, "err-1"),
                new BuildValidationResult(false, "err-2"),
                new BuildValidationResult(false, "err-3")
            ]);

        var orchestrator = new MigrationOrchestrator(
            fileStore,
            extractor,
            promptBuilder,
            codeGenerator,
            buildValidator);

        var result = await orchestrator.RunAsync(new MigrationRequest
        {
            SourceFilePath = "/tmp/source.srw",
            OutputFilePath = "/tmp/output.cs",
            TargetPath = "SktVegapunk.slnx",
            SystemPrompt = "system",
            MaxRetries = 3,
            BuildConfiguration = "Debug",
            RunTestsAfterBuild = false
        });

        Assert.Equal(MigrationState.Failed, result.FinalState);
        Assert.Equal(3, result.Attempts);
        Assert.Equal("已達最大重試次數 3 次。", result.FailureReason);
    }

    private sealed class InMemoryTextFileStore : ITextFileStore
    {
        private readonly Dictionary<string, string> _files;

        public InMemoryTextFileStore(Dictionary<string, string> files)
        {
            _files = files;
        }

        public string? LastWrittenPath { get; private set; }

        public string? LastWrittenContent { get; private set; }

        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
        {
            if (!_files.TryGetValue(path, out var content))
            {
                throw new FileNotFoundException(path);
            }

            return Task.FromResult(content);
        }

        public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
        {
            LastWrittenPath = path;
            LastWrittenContent = content;
            _files[path] = content;
            return Task.CompletedTask;
        }

        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
        {
            if (!_files.TryGetValue(path, out var content))
            {
                throw new FileNotFoundException(path);
            }

            return Task.FromResult(Encoding.UTF8.GetBytes(content));
        }
    }

    private sealed class StubPbScriptExtractor : IPbScriptExtractor
    {
        public IReadOnlyList<PbEventBlock> Blocks { get; init; } = [];

        public IReadOnlyList<PbEventBlock> Extract(string source) => Blocks;
    }

    private sealed class StubPromptBuilder : IPromptBuilder
    {
        private readonly string _initialPrompt;

        public StubPromptBuilder(string initialPrompt)
        {
            _initialPrompt = initialPrompt;
        }

        public string BuildInitialPrompt(IReadOnlyList<PbEventBlock> eventBlocks) => _initialPrompt;

        public string BuildRepairPrompt(string initialPrompt, string previousGeneratedCode, string validationOutput)
            => $"repair::{validationOutput}";
    }

    private sealed class QueueCodeGenerator : ICodeGenerator
    {
        private readonly Queue<string> _responses;

        public QueueCodeGenerator(IEnumerable<string> responses)
        {
            _responses = new Queue<string>(responses);
        }

        public List<string> ReceivedUserPrompts { get; } = [];

        public Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
        {
            ReceivedUserPrompts.Add(userPrompt);

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("沒有可用的 mock 回應。");
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class QueueBuildValidator : IBuildValidator
    {
        private readonly Queue<BuildValidationResult> _results;

        public QueueBuildValidator(IEnumerable<BuildValidationResult> results)
        {
            _results = new Queue<BuildValidationResult>(results);
        }

        public Task<BuildValidationResult> ValidateAsync(
            BuildValidationRequest request,
            CancellationToken cancellationToken = default)
        {
            if (_results.Count == 0)
            {
                throw new InvalidOperationException("沒有可用的 mock 驗證結果。");
            }

            return Task.FromResult(_results.Dequeue());
        }
    }
}
