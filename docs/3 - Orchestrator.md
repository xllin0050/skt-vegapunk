## 預處理（Preprocessing）千萬不要用 AI

將 PowerBuilder 原始碼（.srd, .srw）交給 AI 預處理有三個致命缺點：

太貴： PB 檔案裡充滿了 UI 座標（x=100 y=200）、字型設定等對業務邏輯毫無意義的廢話，這會大量消耗 AI 的 Token 費用。

會漏詞（幻覺 / Hallucination）： AI 在讀取幾千行結構化文本時，非常容易自作主張省略中間的程式碼。

沒效率： PB 的檔案結構是有固定規律的文字檔。

最低成本的做法：用 C# 寫正則表達式（Regular Expression, Regex）。
寫一個簡單的 C# 方法，把 PB 檔案讀進來，用 Regex 把 SQL 語法區塊、變數宣告區塊、PowerScript 事件區塊（如 Clicked!）精準地切割成字串陣列或 JSON。這部分 100% 準確，而且執行成本是零。

## 極簡版 C# AI 代理工作流（PoC 階段）

Step 1: 讀取與切割（純 C#）
程式讀取 PB 原始檔，用 Regex 切出核心邏輯（剔除 UI 座標）。

Step 2: 呼叫 API（呼叫 LLM）
C# 程式使用 HttpClient 直接呼叫 OpenAI 或 Anthropic 的 API。

System Prompt: 「你是一個資深 .NET 開發者。請將以下的 PowerScript 邏輯轉換為 C# Web API 的 Controller 程式碼，只輸出程式碼，不要廢話。」

User Prompt: 帶入 Step 1 切出來的 PB 程式碼。

Step 3: 存檔與自動編譯（Tool Use 的雛形）
C# 程式接收到 AI 回傳的 C# 程式碼後，自動將它存成 .cs 檔案。接著，利用 C# 的 System.Diagnostics.Process 在背景執行終端機指令：dotnet build。

Step 4: 錯誤回饋迴圈（Error Feedback Loop）
這就是「代理」與「單純對話」的最大差異。

如果 dotnet build 成功：程式進入下一個檔案。

如果 dotnet build 失敗：C# 程式會抓取終端機的錯誤訊息（Error Logs），自動再發一個 API 請求給 AI：「剛剛的程式碼編譯失敗，錯誤訊息是 [Error Log]，請修正並重新提供完整的程式碼。」

設定一個上限（例如最多來回 3 次），避免無限迴圈燒錢。

這個 C# Console App 的精髓在於**回饋迴圈（Feedback Loop）**。它不再只是單向的「發送與接收」，而是具備了初步的「反思與修正（Reflection and Correction）」能力。

## 極簡版 AI 代理協調者（Orchestrator）的概念範例程式碼

你可以直接把它貼到一個新的 .NET Console 專案中測試：

```c#
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace LegacyModernizationAgent
{
    class Program
    {
        // 設定最大重試次數，避免 AI 無限鬼打牆燒 API 費用
        const int MaxRetries = 3; 
        
        static async Task Main(string[] args)
        {
            Console.WriteLine("啟動現代化轉換代理...");

            // 1. 預處理：從 PowerBuilder 原始檔提取核心邏輯 (這裡用假資料示意)
            string pbScript = ExtractLogicWithRegex("C:\\LegacyApp\\Window1.srw");
            
            // 初始化 Prompt
            string systemPrompt = "你是一個資深 .NET 開發者。請將以下的 PowerScript 邏輯轉換為 C# 代碼。只輸出程式碼本身，不要有 markdown 標記，也不要任何解釋。";
            string currentPrompt = pbScript;

            bool isBuildSuccessful = false;
            int attempt = 1;

            // 核心的 Agent 迴圈 (The Agentic Loop)
            while (attempt <= MaxRetries && !isBuildSuccessful)
            {
                Console.WriteLine($"\n--- 第 {attempt} 次嘗試生成與編譯 ---");

                // 2. 呼叫 LLM API 生成 C# 程式碼
                Console.WriteLine("呼叫 AI 模型生成程式碼中...");
                string generatedCSharpCode = await CallLlmApiAsync(systemPrompt, currentPrompt);

                // 3. 將 AI 生成的程式碼存檔
                string targetFilePath = "C:\\ModernApp\\Controllers\\GeneratedController.cs";
                File.WriteAllText(targetFilePath, generatedCSharpCode);
                Console.WriteLine($"程式碼已儲存至: {targetFilePath}");

                // 4. 執行本地編譯 (Tool Use)
                Console.WriteLine("正在執行 dotnet build...");
                string buildOutput = RunDotnetBuild("C:\\ModernApp");

                if (buildOutput.Contains("Build succeeded") || buildOutput.Contains("編譯成功"))
                {
                    Console.WriteLine("✅ 編譯成功！這個模組轉換完成。");
                    isBuildSuccessful = true;
                }
                else
                {
                    Console.WriteLine("❌ 編譯失敗，正在將錯誤訊息回饋給 AI...");
                    
                    // 關鍵步驟：將錯誤日誌 (Error Logs) 塞回 Prompt 中，要求 AI 修正
                    currentPrompt = $@"我剛才使用你提供的程式碼進行編譯，但是失敗了。
                    以下是原本的 PowerBuilder 邏輯：{pbScript}
                    以下是編譯器吐出的錯誤訊息：{buildOutput}請根據錯誤訊息修正程式碼，並重新提供完整的 C# 程式碼。
                    同樣只輸出程式碼本身。";
                    
                    attempt++;
                }
            }

            if (!isBuildSuccessful)
            {
                Console.WriteLine("\n⚠️ 達到最大重試次數，請由人類工程師（Human-in-the-Loop）接手處理。");
            }
        }

        // --- 以下為輔助方法 (Helper Methods) ---

        static string ExtractLogicWithRegex(string filePath)
        {
            // TODO: 這裡寫你的 Regex 邏輯，把 .srw 裡面的 UI 座標清掉，只保留 SQL 和 Clicked Event
            return "/* 假設這是用 Regex 切出來的 PowerScript 邏輯 */\n string ls_name\n ls_name = 'Test'"; 
        }

        static async Task<string> CallLlmApiAsync(string systemPrompt, string userPrompt)
        {
            // TODO: 這裡實作 HttpClient 呼叫 OpenAI 或 Anthropic API 的邏輯
            // 為了展示迴圈，這裡用模擬的回應：
            // 第一次回傳會報錯的 Code，第二次回傳正確的 Code
            await Task.Delay(1500); // 模擬網路延遲 (Network Latency)
            
            // 這裡你可以設計一個靜態變數來模擬 AI 第一次寫錯，第二次寫對的情況
            return "public class GeneratedController { \n // 假設這是 AI 寫出來的 Code \n }";
        }

        static string RunDotnetBuild(string projectDirectory)
        {
            // 啟動子處理程序 (Subprocess) 來執行終端機指令
            ProcessStartInfo processInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "build",
                WorkingDirectory = projectDirectory,
                RedirectStandardOutput = true, // 攔截標準輸出 (Standard Output)
                RedirectStandardError = true,  // 攔截標準錯誤 (Standard Error)
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = Process.Start(processInfo))
            {
                // 讀取編譯結果的文字
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return output;
            }
        }
    }
}

```

### 程式碼亮點解析

1. **回饋迴圈（Feedback Loop）：** 整個程式的靈魂在 `while` 迴圈。當編譯失敗時，程式不會崩潰，而是把 `buildOutput`（編譯器的錯誤訊息）當作新的 Prompt 餵給 AI。這就是代理（Agent）能夠「自我修正（Self-Correction）」的關鍵。
2. **子處理程序（Subprocess）：** `RunDotnetBuild` 方法利用 `System.Diagnostics.Process` 在背景偷偷執行 `dotnet build`。AI 產生的程式碼不再只是「看起來很棒的字串」，而是真正經過編譯器（Compiler）檢驗的產物。
3. **防呆機制（Fail-safe）：** 設定了 `MaxRetries`。如果 AI 遇到太複雜的底層依賴，連續寫錯三次，程式會優雅地停下來，把這個難搞的檔案留給你手動處理，避免浪費昂貴的 API Token。

## 下一步

1. 實作 `HttpClient` 串接 API 的部分
2. 怎麼寫 Regex（正規表達式 / Regular Expression）來乾淨地把 PowerBuilder 的業務邏輯萃取出來