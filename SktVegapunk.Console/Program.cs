using Microsoft.Extensions.Configuration;
using SktVegapunk.Core;
using SktVegapunk.Core.Pipeline;
using SktVegapunk.Core.Pipeline.Spec;

internal class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=== SktVegapunk SSG Go! ===");

        if (!TryParseOptions(args, out var options, out var parseError) || options is null)
        {
            Console.WriteLine(parseError);
            PrintUsage();
            return 1;
        }

        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false)
            .AddUserSecrets<Program>()
            .AddEnvironmentVariables()
            .Build();

        try
        {
            if (options.Mode == ProgramMode.SpecArtifacts)
            {
                return await RunSpecArtifactsModeAsync(options);
            }

            string systemPrompt = config["Agent:SystemPrompt"]
                ?? throw new InvalidOperationException("找不到 Agent:SystemPrompt，請檢查 appsettings.json。");
            string modelName = config["Agent:ModelName"]
                ?? throw new InvalidOperationException("找不到 Agent:ModelName，請檢查 appsettings.json。");
            string? githubToken = config["GitHubCopilot:GitHubToken"];
            string? cliPath = config["GitHubCopilot:CliPath"];
            string? workingDirectory = config["GitHubCopilot:WorkingDirectory"];

            var maxRetries = ParseIntOrDefault(config["Pipeline:MaxRetries"], 3);
            if (maxRetries < 1)
            {
                throw new InvalidOperationException("Pipeline:MaxRetries 必須大於 0。");
            }

            var runTestsAfterBuild = ParseBoolOrDefault(config["Pipeline:RunTestsAfterBuild"], false);
            var buildConfiguration = config["Pipeline:BuildConfiguration"] ?? "Debug";

            await using var copilotClient = new GitHubCopilotClient(
                githubToken,
                cliPath,
                workingDirectory);

            var codeGenerator = new CopilotCodeGenerator(copilotClient, modelName);
            var orchestrator = new MigrationOrchestrator(
                new FileTextStore(),
                new PbScriptExtractor(),
                new PromptBuilder(),
                codeGenerator,
                new DotnetBuildValidator(new ProcessRunner()));

            var request = new MigrationRequest
            {
                SourceFilePath = options.SourceFilePath!,
                OutputFilePath = options.OutputFilePath!,
                TargetPath = options.TargetPath!,
                SystemPrompt = systemPrompt,
                MaxRetries = maxRetries,
                RunTestsAfterBuild = runTestsAfterBuild,
                BuildConfiguration = buildConfiguration
            };

            Console.WriteLine($"模型: {modelName}");
            Console.WriteLine($"來源檔案: {options.SourceFilePath}");
            Console.WriteLine($"輸出檔案: {options.OutputFilePath}");
            Console.WriteLine($"驗證目標: {options.TargetPath}");
            Console.WriteLine();

            var result = await orchestrator.RunAsync(request);

            if (result.FinalState == MigrationState.Completed)
            {
                Console.WriteLine("轉換成功並通過驗證。");
                Console.WriteLine($"嘗試次數: {result.Attempts}");
                return 0;
            }

            Console.WriteLine("轉換失敗。");
            Console.WriteLine($"最終狀態: {result.FinalState}");
            Console.WriteLine($"嘗試次數: {result.Attempts}");

            if (!string.IsNullOrWhiteSpace(result.FailureReason))
            {
                Console.WriteLine($"失敗原因: {result.FailureReason}");
            }

            if (!string.IsNullOrWhiteSpace(result.LastValidationOutput))
            {
                Console.WriteLine("最後一次驗證輸出：");
                Console.WriteLine(result.LastValidationOutput);
            }

            return 2;
        }
        catch (HttpRequestException httpEx)
        {
            Console.WriteLine($"[網路/API請求錯誤]: {httpEx.Message}");
            return 3;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[系統錯誤]: {ex.Message}");
            return 4;
        }
    }

    private static bool TryParseOptions(string[] args, out ProgramOptions? options, out string error)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? sourcePath = null;
        string? outputPath = null;
        string? targetPath = null;
        string? specSourcePath = null;
        string? specOutputPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--source":
                    if (!TryReadNextValue(args, ref i, out sourcePath))
                    {
                        options = null;
                        error = "參數 --source 缺少值。";
                        return false;
                    }

                    break;

                case "--output":
                    if (!TryReadNextValue(args, ref i, out outputPath))
                    {
                        options = null;
                        error = "參數 --output 缺少值。";
                        return false;
                    }

                    break;

                case "--target-project":
                    if (!TryReadNextValue(args, ref i, out targetPath))
                    {
                        options = null;
                        error = "參數 --target-project 缺少值。";
                        return false;
                    }

                    break;

                case "--spec-source":
                    if (!TryReadNextValue(args, ref i, out specSourcePath))
                    {
                        options = null;
                        error = "參數 --spec-source 缺少值。";
                        return false;
                    }

                    break;

                case "--spec-output":
                    if (!TryReadNextValue(args, ref i, out specOutputPath))
                    {
                        options = null;
                        error = "參數 --spec-output 缺少值。";
                        return false;
                    }

                    break;

                default:
                    options = null;
                    error = $"未知參數: {arg}";
                    return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(specSourcePath) || !string.IsNullOrWhiteSpace(specOutputPath))
        {
            if (string.IsNullOrWhiteSpace(specSourcePath))
            {
                options = null;
                error = "缺少必要參數 --spec-source。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(specOutputPath))
            {
                options = null;
                error = "缺少必要參數 --spec-output。";
                return false;
            }

            options = new ProgramOptions(
                Mode: ProgramMode.SpecArtifacts,
                SourceFilePath: null,
                OutputFilePath: null,
                TargetPath: null,
                SpecSourceDirectory: specSourcePath,
                SpecOutputDirectory: specOutputPath);
            error = string.Empty;
            return true;
        }

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            options = null;
            error = "缺少必要參數 --source。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            options = null;
            error = "缺少必要參數 --output。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            options = null;
            error = "缺少必要參數 --target-project。";
            return false;
        }

        options = new ProgramOptions(
            Mode: ProgramMode.Migration,
            SourceFilePath: sourcePath,
            OutputFilePath: outputPath,
            TargetPath: targetPath,
            SpecSourceDirectory: null,
            SpecOutputDirectory: null);
        error = string.Empty;
        return true;
    }

    private static bool TryReadNextValue(string[] args, ref int index, out string? value)
    {
        var nextIndex = index + 1;
        if (nextIndex >= args.Length)
        {
            value = null;
            return false;
        }

        value = args[nextIndex];
        index = nextIndex;
        return true;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("用法：");
        Console.WriteLine("dotnet run --project SktVegapunk.Console -- --source <pb-file> --output <generated-cs-file> --target-project <project-or-sln>");
        Console.WriteLine("dotnet run --project SktVegapunk.Console -- --spec-source <source-dir> --spec-output <output-dir>");
    }

    private static int ParseIntOrDefault(string? rawValue, int defaultValue)
    {
        if (int.TryParse(rawValue, out var parsed))
        {
            return parsed;
        }

        return defaultValue;
    }

    private static bool ParseBoolOrDefault(string? rawValue, bool defaultValue)
    {
        if (bool.TryParse(rawValue, out var parsed))
        {
            return parsed;
        }

        return defaultValue;
    }

    private static async Task<int> RunSpecArtifactsModeAsync(ProgramOptions options)
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

        Console.WriteLine($"規格來源目錄: {options.SpecSourceDirectory}");
        Console.WriteLine($"規格輸出目錄: {options.SpecOutputDirectory}");
        Console.WriteLine();

        var result = await generator.GenerateAsync(
            options.SpecSourceDirectory!,
            options.SpecOutputDirectory!);

        Console.WriteLine("規格提取完成。");
        Console.WriteLine($"DataWindow: {result.DataWindowCount}");
        Console.WriteLine($"Component: {result.ComponentCount}");
        Console.WriteLine($"JSP Invocation: {result.JspInvocationCount}");
        Console.WriteLine($"JSP Prototype: {result.JspPrototypeCount}");
        Console.WriteLine($"Warnings: {result.WarningCount}");

        return 0;
    }

    private sealed record ProgramOptions(
        ProgramMode Mode,
        string? SourceFilePath,
        string? OutputFilePath,
        string? TargetPath,
        string? SpecSourceDirectory,
        string? SpecOutputDirectory);

    private enum ProgramMode
    {
        Migration,
        SpecArtifacts
    }
}
