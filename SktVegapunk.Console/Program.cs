using Microsoft.Extensions.Configuration;
using SktVegapunk.Core;

Console.WriteLine("=== SktVegapunk AI Agents Start ===");

// 從 user-secrets 或環境變數讀取金鑰（優先順序：環境變數 > user-secrets）
// 設定方式請參閱 README.md
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: false)
    .AddUserSecrets<Program>()
    .AddEnvironmentVariables()
    .Build();

string apiKey = config["OpenRouter:ApiKey"]
    ?? throw new InvalidOperationException("找不到 OpenRouter:ApiKey，請依照 README.md 設定 user-secrets。");

// 初始化 Client
var client = new OpenRouterClient(apiKey);

string systemPrompt = config["Agent:SystemPrompt"]
    ?? throw new InvalidOperationException("找不到 Agent:SystemPrompt，請檢查 appsettings.json。");
string modelName = config["Agent:ModelName"]
    ?? throw new InvalidOperationException("找不到 Agent:ModelName，請檢查 appsettings.json。");

// 設定測試用的假資料
string userPrompt = "ls_username = sle_name.text \n li_status = 1";

Console.WriteLine($"\n準備呼叫模型: {modelName}");
Console.WriteLine("思考中...\n");

try
{
    // 呼叫 API
    string? result = await client.SendMessageAsync(modelName, systemPrompt, userPrompt);

    Console.WriteLine("=== AI 轉換結果 ===");
    Console.WriteLine(result);
    Console.WriteLine("===================");
}
catch (HttpRequestException httpEx)
{
    Console.WriteLine($"[網路/API請求錯誤]: {httpEx.Message}");
}
catch (Exception ex)
{
    Console.WriteLine($"[系統錯誤]: {ex.Message}");
}
