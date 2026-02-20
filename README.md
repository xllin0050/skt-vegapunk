# SktVegapunk

## 快速開始

### 1. 事前準備

- [.NET SDK 10.0.103](https://dotnet.microsoft.com/download)
- [OpenRouter](https://openrouter.ai/) 帳號與 API 金鑰（格式：`sk-or-v1-...`）

### 2. Clone 專案

```bash
git clone <repo-url>
cd skt-vegapunk
```

### 3. 設定 API 金鑰

本專案使用 **dotnet user-secrets** 管理金鑰，金鑰儲存在本機使用者目錄，不會進入版本控制。

```bash
dotnet user-secrets set "OpenRouter:ApiKey" "sk-or-v1-你的金鑰" --project SktVegapunk.Console
```

> **macOS/Linux** 金鑰存放於 `~/.microsoft/usersecrets/<guid>/secrets.json`  
> **Windows** 金鑰存放於 `%APPDATA%\Microsoft\UserSecrets\<guid>\secrets.json`

確認金鑰已設定：

```bash
dotnet user-secrets list --project SktVegapunk.Console
```

### 4. 調整設定（選用）

模型與系統提示詞定義在 `SktVegapunk.Console/appsettings.json`，直接編輯即可：

```json
{
  "Agent": {
    "ModelName": "anthropic/claude-3-haiku",
    "SystemPrompt": "你是一個資深的 .NET 開發者。..."
  }
}
```

也可以在執行時用環境變數臨時覆蓋，無需修改檔案（`__` 代表階層分隔）：

```bash
# macOS / Linux
Agent__ModelName="google/gemini-2.5-flash" dotnet run --project SktVegapunk.Console

# Windows PowerShell
$env:Agent__ModelName="google/gemini-2.5-flash"; dotnet run --project SktVegapunk.Console
```

**設定優先順序（後者蓋前者）：**

```
appsettings.json → user-secrets → 環境變數
```

| 設定 | 位置 | 說明 |
|---|---|---|
| `Agent:ModelName` | `appsettings.json` | 使用的 AI 模型，進版控 |
| `Agent:SystemPrompt` | `appsettings.json` | 系統提示詞，進版控 |
| `OpenRouter:ApiKey` | user-secrets | API 金鑰，**不進版控** |

### 5. 執行

```bash
dotnet run --project SktVegapunk.Console
```

---

## 專案結構

skt-vegapunk/
├── global.json
├── Directory.Build.props
├── SktVegapunk.slnx
├── SktVegapunk.Console/
│   └── SktVegapunk.Console.csproj
├── SktVegapunk.Core/
│   └── SktVegapunk.Core.csproj
└── SktVegapunk.Tests/
    ├── SktVegapunk.Tests.csproj
    └── UnitTest1.cs
