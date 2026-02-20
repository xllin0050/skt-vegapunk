# SktVegapunk Agent Guide

## Repo Overview

- `SktVegapunk.Console`: 主控台入口，讀取設定並呼叫 Core client。
- `SktVegapunk.Core`: OpenRouter 呼叫邏輯與資料模型。
- `SktVegapunk.Tests`: xUnit 測試專案。
- 全域設定在 `Directory.Build.props`：`Nullable=enable`、`TreatWarningsAsErrors=true`、`TargetFramework=net10.0`。

## Local Setup

1. 使用 `global.json` 指定的 SDK（目前 `10.0.103`）。
2. 設定 OpenRouter 金鑰（不要寫入程式碼或 `appsettings.json`）：
   - `dotnet user-secrets set "OpenRouter:ApiKey" "<your-key>" --project SktVegapunk.Console`
3. 驗證 secrets：
   - `dotnet user-secrets list --project SktVegapunk.Console`

## Configuration Rules

- 目前 `Program.cs` 使用的載入順序是：
  1. `appsettings.json`
  2. `AddUserSecrets<Program>()`
  3. `AddEnvironmentVariables()`
- 依 Microsoft Learn，後載入者覆蓋先載入者。
- 環境變數階層鍵使用 `__`，例如 `Agent__ModelName` 對應 `Agent:ModelName`。

## Build/Test/Format Commands

- `dotnet run --project SktVegapunk.Console`
- `dotnet build SktVegapunk.slnx`
- `dotnet test SktVegapunk.slnx`
- `dotnet format SktVegapunk.slnx --verify-no-changes`

## Coding Expectations

- 不提交任何 secrets。
- 保持可測試性：業務邏輯放在 `Core`，I/O 與入口流程放在 `Console`。
- HTTP 呼叫若要擴充，優先採用 Microsoft Learn 建議的 `IHttpClientFactory` 模式，不要每次請求建立/銷毀 client。
- 變更後至少要能通過 `build` 與 `test`。
