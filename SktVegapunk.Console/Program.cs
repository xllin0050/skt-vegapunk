using Microsoft.Extensions.Configuration;
using SktVegapunk.Core;
using SktVegapunk.Core.Pipeline;

internal class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=== SktVegapunk SSG Go! ===");

        // 檢查參數解析
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

        string apiKey = config["OpenRouter:ApiKey"]
            ?? throw new InvalidOperationException("找不到 OpenRouter:ApiKey，請依照 README.md 設定 user-secrets。");
        string systemPrompt = config["Agent:SystemPrompt"]
            ?? throw new InvalidOperationException("找不到 Agent:SystemPrompt，請檢查 appsettings.json。");
        string modelName = config["Agent:ModelName"]
            ?? throw new InvalidOperationException("找不到 Agent:ModelName，請檢查 appsettings.json。");


        var maxRetries = ParseIntOrDefault(config["Pipeline:MaxRetries"], 3);
        if (maxRetries < 1)
        {
            throw new InvalidOperationException("Pipeline:MaxRetries 必須大於 0。");
        }

        var runTestsAfterBuild = ParseBoolOrDefault(config["Pipeline:RunTestsAfterBuild"], false);
        var buildConfiguration = config["Pipeline:BuildConfiguration"] ?? "Debug";

        using var httpClient = new HttpClient();

        // 與 OpenRouter API 進行溝通，發送請求並接收回應
        var openRouterClient = new OpenRouterClient(httpClient, apiKey);

        // 根據從 PbScriptExtractor 提取的資訊來生成 C# 代碼，並使用 OpenRouterClient 來輔助生成過程中的決策
        var codeGenerator = new OpenRouterCodeGenerator(openRouterClient, modelName);

        // 負責管理從提取、生成到驗證的整個過程，確保各個步驟按照正確的順序執行，並處理過程中的錯誤和重試邏輯
        var orchestrator = new MigrationOrchestrator(
            new FileTextStore(),
            new PbScriptExtractor(),
            new PromptBuilder(),
            codeGenerator,
            new DotnetBuildValidator(new ProcessRunner()));

        // 封裝了整個轉換流程所需的所有資訊，Orchestrator 會根據這些資訊來執行整個轉換和驗證流程
        var request = new MigrationRequest
        {
            SourceFilePath = options.SourceFilePath,
            OutputFilePath = options.OutputFilePath,
            TargetPath = options.TargetPath,
            SystemPrompt = systemPrompt,
            MaxRetries = maxRetries,
            RunTestsAfterBuild = runTestsAfterBuild,
            BuildConfiguration = buildConfiguration
        };

        try
        {
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

                default:
                    options = null;
                    error = $"未知參數: {arg}";
                    return false;
            }
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

        options = new ProgramOptions(sourcePath, outputPath, targetPath);
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

    private sealed record ProgramOptions(string SourceFilePath, string OutputFilePath, string TargetPath);
}
