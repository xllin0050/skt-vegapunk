using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using GitHub.Copilot.SDK;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// 每個執行工作以 runId 對應一個 Channel，供 SSE 端點消費
var runs = new ConcurrentDictionary<string, Channel<string>>();

// ──────────────────────────────────────────────
// Copilot CLI 單例客戶端（懶啟動，供狀態查詢用）
// ──────────────────────────────────────────────
CopilotClient? copilotClient = null;
var copilotLock = new SemaphoreSlim(1, 1);

async Task<CopilotClient?> GetCopilotAsync(CancellationToken ct = default)
{
    if (copilotClient is not null) return copilotClient;
    await copilotLock.WaitAsync(ct);
    try
    {
        if (copilotClient is not null) return copilotClient;
        var c = new CopilotClient();
        using var startCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        startCts.CancelAfter(TimeSpan.FromSeconds(15));
        await c.StartAsync(startCts.Token);
        copilotClient = c;
        return c;
    }
    catch
    {
        return null;
    }
    finally
    {
        copilotLock.Release();
    }
}

app.Lifetime.ApplicationStopped.Register(() =>
    copilotClient?.DisposeAsync().AsTask().GetAwaiter().GetResult());

// ──────────────────────────────────────────────
// API: 查詢 Copilot CLI 認證狀態
// ──────────────────────────────────────────────
app.MapGet("/api/copilot/status", async (CancellationToken ct) =>
{
    try
    {
        var client = await GetCopilotAsync(ct);
        if (client is null)
            return Results.Ok(new { isAuthenticated = false, login = (string?)null, statusMessage = "無法啟動 Copilot CLI" });

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        var status = await client.GetAuthStatusAsync(cts.Token);
        return Results.Ok(new
        {
            isAuthenticated = status.IsAuthenticated,
            login = status.Login,
            statusMessage = status.StatusMessage
        });
    }
    catch (Exception ex)
    {
        return Results.Ok(new { isAuthenticated = false, login = (string?)null, statusMessage = ex.Message });
    }
});

// ──────────────────────────────────────────────
// API: 取得可選 AI 模型清單（從 Copilot CLI 動態取得，失敗時回退靜態清單）
// ──────────────────────────────────────────────
string[] FallbackModels =
    ["gpt-5.4", "gpt-5-mini", "gpt-5.4-mini", "claude-sonnet-4.6", "claude-opus-4.6", "gemini-3-flash-preview"];

app.MapGet("/api/config/models", async (CancellationToken ct) =>
{
    try
    {
        var client = await GetCopilotAsync(ct);
        if (client is null) return Results.Ok(FallbackModels);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        var models = await client.ListModelsAsync(cts.Token);
        var ids = models.Select(m => m.Id).Where(id => !string.IsNullOrWhiteSpace(id)).ToArray();
        return Results.Ok(ids.Length > 0 ? ids : FallbackModels);
    }
    catch
    {
        return Results.Ok(FallbackModels);
    }
});

// ──────────────────────────────────────────────
// API: 列出子目錄（給路徑選擇器使用）
// ──────────────────────────────────────────────
app.MapGet("/api/dirs", (string? path) =>
{
    var root = string.IsNullOrWhiteSpace(path)
        ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        : path;

    if (!Directory.Exists(root))
        return Results.BadRequest(new { error = "路徑不存在" });

    try
    {
        var parent = Directory.GetParent(root)?.FullName;
        var entries = Directory.GetDirectories(root)
            .Select(d => new { name = Path.GetFileName(d), fullPath = d })
            .OrderBy(e => e.name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Results.Ok(new { path = root, parent, entries });
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Ok(new { path = root, parent = (string?)null, entries = Array.Empty<object>() });
    }
});

// ──────────────────────────────────────────────
// API: 啟動執行工作，回傳 runId
// ──────────────────────────────────────────────
app.MapPost("/api/run", (RunRequest req) =>
{
    var runId = Guid.NewGuid().ToString("N");
    var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    runs[runId] = channel;

    _ = Task.Run(async () =>
    {
        try
        {
            var consoleProjPath = FindConsoleProjPath();
            var cliArgs = BuildCliArgs(req, consoleProjPath);

            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = cliArgs,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(consoleProjPath)!
            };

            // 以環境變數覆蓋 model，讓 Console 讀到正確設定
            if (!string.IsNullOrWhiteSpace(req.Model))
                psi.Environment["Agent__ModelName"] = req.Model;

            using var process = new Process { StartInfo = psi };

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null) channel.Writer.TryWrite(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null) channel.Writer.TryWrite($"[ERR] {e.Data}");
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            channel.Writer.TryWrite($"__EXIT__{process.ExitCode}");
        }
        catch (Exception ex)
        {
            channel.Writer.TryWrite($"[ERR] 啟動失敗：{ex.Message}");
            channel.Writer.TryWrite("__EXIT__-1");
        }
        finally
        {
            channel.Writer.TryComplete();
        }
    });

    return Results.Ok(new { runId });
});

// ──────────────────────────────────────────────
// API: SSE 串流（EventSource 連接此 endpoint）
// ──────────────────────────────────────────────
app.MapGet("/api/run/{runId}/stream", async (string runId, HttpContext ctx) =>
{
    if (!runs.TryGetValue(runId, out var channel))
    {
        ctx.Response.StatusCode = 404;
        return;
    }

    ctx.Response.Headers["Content-Type"] = "text/event-stream";
    ctx.Response.Headers["Cache-Control"] = "no-cache";
    ctx.Response.Headers["X-Accel-Buffering"] = "no";
    ctx.Response.Headers["Connection"] = "keep-alive";

    await foreach (var line in channel.Reader.ReadAllAsync(ctx.RequestAborted))
    {
        if (line.StartsWith("__EXIT__"))
        {
            var exitCode = line["__EXIT__".Length..];
            await ctx.Response.WriteAsync($"event: done\ndata: {exitCode}\n\n");
        }
        else
        {
            var escaped = line.Replace("\n", " ");
            await ctx.Response.WriteAsync($"data: {escaped}\n\n");
        }

        await ctx.Response.Body.FlushAsync();
    }

    runs.TryRemove(runId, out _);
});

// ──────────────────────────────────────────────
// API: 列出 output 目錄下所有 .md 檔
// ──────────────────────────────────────────────
app.MapGet("/api/artifacts", (string? @base) =>
{
    if (string.IsNullOrWhiteSpace(@base) || !Directory.Exists(@base))
        return Results.BadRequest(new { error = "路徑不存在" });

    var files = Directory.EnumerateFiles(@base, "*.md", SearchOption.AllDirectories)
        .Select(f => new
        {
            name = Path.GetFileName(f),
            relativePath = Path.GetRelativePath(@base, f).Replace('\\', '/'),
            fullPath = f
        })
        .OrderBy(f => f.relativePath, StringComparer.OrdinalIgnoreCase)
        .ToList();

    return Results.Ok(new { files });
});

// ──────────────────────────────────────────────
// API: 回傳指定 .md 檔的原始內容
// ──────────────────────────────────────────────
app.MapGet("/api/artifacts/content", async (string? path) =>
{
    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        return Results.NotFound();

    var content = await File.ReadAllTextAsync(path);
    return Results.Text(content, "text/plain; charset=utf-8");
});

// ──────────────────────────────────────────────
// API: 全文搜尋 output 目錄下的 .md 檔
// ──────────────────────────────────────────────
app.MapGet("/api/artifacts/search", (string? @base, string? q) =>
{
    if (string.IsNullOrWhiteSpace(@base) || string.IsNullOrWhiteSpace(q) || !Directory.Exists(@base))
        return Results.BadRequest(new { error = "缺少參數或路徑不存在" });

    var results = Directory.EnumerateFiles(@base, "*.md", SearchOption.AllDirectories)
        .SelectMany(f =>
        {
            try
            {
                var content = File.ReadAllText(f);
                var idx = content.IndexOf(q, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) return [];
                var start = Math.Max(0, idx - 60);
                var end = Math.Min(content.Length, idx + q.Length + 60);
                var snippet = content[start..end].Replace('\n', ' ').Replace('\r', ' ').Trim();
                return (IEnumerable<object>)[new
                {
                    name = Path.GetFileName(f),
                    relativePath = Path.GetRelativePath(@base, f).Replace('\\', '/'),
                    fullPath = f,
                    snippet
                }];
            }
            catch
            {
                return [];
            }
        })
        .Take(50)
        .ToList();

    return Results.Ok(new { results });
});

app.Run();

// ──────────────────────────────────────────────
// 輔助函式
// ──────────────────────────────────────────────

static string FindConsoleProjPath()
{
    // 從 BaseDirectory 向上找 .slnx/.sln，定位 solution root
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (dir.GetFiles("*.slnx").Length > 0 || dir.GetFiles("*.sln").Length > 0)
        {
            var consoleProjPath = Path.Combine(
                dir.FullName, "SktVegapunk.Console", "SktVegapunk.Console.csproj");
            if (File.Exists(consoleProjPath))
                return consoleProjPath;
        }

        dir = dir.Parent;
    }

    throw new InvalidOperationException("找不到 SktVegapunk.Console.csproj，請確認目錄結構正確。");
}

static string BuildCliArgs(RunRequest req, string consoleProjPath)
{
    var q = (string s) => $"\"{s}\"";

    return req.Mode switch
    {
        "spec" =>
            $"run --project {q(consoleProjPath)} -- " +
            $"--spec-source {q(req.SourcePath)} " +
            $"--spec-output {q(req.OutputPath)}",

        "migration" =>
            $"run --project {q(consoleProjPath)} -- " +
            $"--source {q(req.SourcePath)} " +
            $"--output {q(req.OutputFilePath ?? string.Empty)} " +
            $"--target-project {q(req.TargetPath ?? string.Empty)}",

        _ => throw new ArgumentException($"不支援的模式：{req.Mode}")
    };
}

record RunRequest(
    string Mode,
    string Model,
    string SourcePath,
    string OutputPath,
    string? OutputFilePath = null,
    string? TargetPath = null);
