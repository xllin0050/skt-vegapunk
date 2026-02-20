# 務實方案：「結構化區塊提取」+「狀態機」

與其去解析每一行程式碼的「語法（Syntax）」，我們只需要解析檔案的「結構（Structure）」。

PowerBuilder 的匯出檔（.srw 視窗檔、.sru 使用者物件）其實是具有高度規律的純文字檔。我們不需要懂裡面的邏輯，只需要把**「UI 定義區塊」跟「業務邏輯區塊（Event Scripts）」**乾淨地切開就好。

## 作法：在 C# 中寫一個簡單的「狀態機（State Machine）」或「逐行讀取器（Line-by-line Reader）」

### 逐行掃描：

使用 C# 的 StreamReader 逐行讀取檔案，而不是用複雜的正則表達式（Regex 處理巢狀結構很容易崩潰）。

### 狀態切換：

當讀到類似 on clicked; 的關鍵字時，將程式狀態切換為 [正在讀取邏輯]，並把接下來的每一行存進一個 StringBuilder 中。

當讀到 end on 時，將狀態切換回 [尋找下一個區塊]，並把剛剛收集到的邏輯存成一個乾淨的字串。

過濾雜訊： 遇到類似 x=100, y=200, font.face="Tahoma" 這種純 UI 屬性的行，直接略過（捨棄），因為這些對於後端 C# Web API 毫無意義（前端 Vue.js 則可以另外交由前端代理去推敲佈局）。

### 好處

最大化利用 AI： 你把切出來的「純淨 PowerScript 邏輯區塊」餵給 LLM，告訴它：「這是一個按鈕點擊事件的程式碼，請幫我轉成 C# 邏輯。」剩下的語法理解（它是迴圈還是條件判斷），讓聰明且便宜的 AI 去傷腦筋就好。

## 示範

### 無痛過濾雜訊：

類似 integer x = 100 或 string text = "Submit" 這種對於後端 C# Web API 毫無意義的 UI 佈局程式碼，在 Scanning 狀態下會被程式自動無視，連一個字元都不會存起來。

### 精準打包：

狀態機會精準地把 event clicked; 到 end event 之間的核心業務邏輯裝進 scriptBuffer 裡面。

### 極省 Token：

最終丟給 AI API 的，就只剩下 List<PbEventBlock> 裡面的 ScriptBody。這會讓你的 API 費用大幅降低，而且 AI 不會被落落長的 UI 屬性干擾，減少產生幻覺（Hallucination）的機率。

```c#
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PbParserAgent
{
    // 定義解析器的狀態
    public enum ParserState
    {
        Scanning,       // 正在掃描 (尋找目標，略過無用 UI 屬性)
        InEventScript   // 正在事件區塊內 (收集業務邏輯)
    }

    // 用來存放切出來的邏輯區塊
    public class PbEventBlock
    {
        public string EventName { get; set; } = "";
        public string ScriptBody { get; set; } = "";
    }

    class Program
    {
        static void Main(string[] args)
        {
            // 模擬一個 PowerBuilder 的 .srw 匯出檔內容
            string mockPbFileContent = @"
            forward
            global type w_main from window
            end type
            end forward

            global type w_main from window
            integer width = 2000
            integer height = 1000
            boolean titlebar = true
            string title = ""Main Window""
            end type

            type cb_submit from commandbutton within w_main
            integer x = 100
            integer y = 200
            integer width = 400
            integer height = 112
            string text = ""Submit""
            end type

            event clicked;
            // 這裡才是我們要轉換的黃金邏輯
            string ls_username
            int li_status

            ls_username = sle_name.text
            li_status = 1

            if ls_username = """" then
                MessageBox(""Error"", ""Name cannot be empty!"")
                return
            end if

            // Call database save function here...
            end event

            type sle_name from singlelineedit within w_main
            integer x = 100
            integer y = 50
            integer width = 400
            integer height = 100
            end type
            ";
            // 執行狀態機解析
            List<PbEventBlock> extractedBlocks = ParsePbSource(mockPbFileContent);

            // 印出結果
            Console.WriteLine("=== 提取結果 ===");
            foreach (var block in extractedBlocks)
            {
                Console.WriteLine($"[事件名稱]: {block.EventName}");
                Console.WriteLine($"[邏輯內容]:\n{block.ScriptBody}");
                Console.WriteLine("-------------------");
            }
        }

        static List<PbEventBlock> ParsePbSource(string fileContent)
        {
            var blocks = new List<PbEventBlock>();
            var currentState = ParserState.Scanning;
            
            PbEventBlock currentBlock = null;
            StringBuilder scriptBuffer = new StringBuilder();

            // 使用 StringReader 模擬逐行讀取檔案 (實戰中替換為 StreamReader 讀取實體檔案)
            using (StringReader reader = new StringReader(fileContent))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string trimmedLine = line.Trim();

                    // --- 狀態機邏輯 ---
                    switch (currentState)
                    {
                        case ParserState.Scanning:
                            // 尋找事件區塊的開頭，例如 "event clicked;"
                            if (trimmedLine.StartsWith("event ", StringComparison.OrdinalIgnoreCase))
                            {
                                // 切換狀態：進入事件區塊
                                currentState = ParserState.InEventScript;
                                currentBlock = new PbEventBlock
                                {
                                    EventName = trimmedLine.Replace("event ", "").Replace(";", "")
                                };
                                scriptBuffer.Clear();
                            }
                            // 如果是其他屬性 (如 integer x = 100)，因為在 Scanning 狀態，直接略過
                            break;

                        case ParserState.InEventScript:
                            // 尋找事件區塊的結尾，例如 "end event"
                            if (trimmedLine.Equals("end event", StringComparison.OrdinalIgnoreCase))
                            {
                                // 打包資料
                                currentBlock.ScriptBody = scriptBuffer.ToString();
                                blocks.Add(currentBlock);

                                // 切換狀態：回到掃描模式
                                currentState = ParserState.Scanning;
                                currentBlock = null;
                            }
                            else
                            {
                                // 還沒遇到結尾，把這行程式碼收集起來
                                scriptBuffer.AppendLine(line);
                            }
                            break;
                    }
                }
            }

            return blocks;
        }
    }
}
```