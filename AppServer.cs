using System.Diagnostics;
using System.Text.Json;

namespace CodexMeter;

// Only this file knows the app-server wire format. No token or cookie access.
internal static class AppServer
{
    public static string FindExecutable()
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var exe = Path.Combine(dir.Trim('"'), "codex.exe");
            if (File.Exists(exe)) return exe;
        }
        var npm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "npm", "node_modules", "@openai", "codex", "node_modules", "@openai");
        if (Directory.Exists(npm))
        {
            var exe = Directory.EnumerateFiles(npm, "codex.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (exe != null) return exe;
        }
        throw new IOException("Codex CLIが見つかりません");
    }

    public static async Task<Snapshot> ReadAsync(CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        token = timeout.Token;
        var start = new ProcessStartInfo(FindExecutable()) {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in new[] { "app-server", "--stdio", "-c", "analytics.enabled=false" }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Codexを起動できません");
        using var cancelProcess = token.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } });
        // Drain without retaining or logging server diagnostics.
        var drain = Task.Run(async () => {
            var buffer = new char[2048];
            try { while (await process.StandardError.ReadAsync(buffer.AsMemory(), token) > 0) { } }
            catch (OperationCanceledException) { }
            catch (IOException) { }
        });
        try
        {
            await process.StandardInput.WriteLineAsync("{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"codex_meter\",\"version\":\"1.0.0\"}}}");
            await Reply(process, 1, token);
            await process.StandardInput.WriteLineAsync("{\"method\":\"initialized\",\"params\":{}}");
            await process.StandardInput.WriteLineAsync("{\"id\":2,\"method\":\"account/rateLimits/read\"}");
            return Snapshot.Parse(await Reply(process, 2, token));
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            await drain;
        }
    }

    private static async Task<JsonElement> Reply(Process process, int id, CancellationToken token)
    {
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync(token) ?? throw new IOException("接続が終了しました");
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("id", out var value) || !value.TryGetInt32(out var replyId) || replyId != id) continue;
            if (root.TryGetProperty("error", out _)) throw new IOException("Codex情報を取得できません");
            return root.GetProperty("result").Clone();
        }
    }
}

internal sealed record Window(double? UsedPercent, DateTimeOffset? ResetsAt);
internal sealed record Credit(string Id, DateTimeOffset? ExpiresAt);
internal sealed record Snapshot(Window? FiveHour, Window? Weekly, long? AvailableCount, List<Credit>? Credits)
{
    public static Snapshot Parse(JsonElement result)
    {
        var bucket = Field(result, "rateLimits");
        var buckets = Field(result, "rateLimitsByLimitId");
        if (buckets.ValueKind == JsonValueKind.Object) bucket = Field(buckets, "codex");
        var limitId = Field(bucket, "limitId");
        if (limitId.ValueKind == JsonValueKind.String && limitId.GetString() != "codex") bucket = default;
        Window? five = null, weekly = null;
        foreach (var name in new[] { "primary", "secondary" })
        {
            var w = Field(bucket, name);
            var duration = Number(w, "windowDurationMins");
            var percent = Number(w, "usedPercent");
            if (percent < 0 || percent > 100) percent = null;
            var value = new Window(percent, Timestamp(Field(w, "resetsAt")));
            if (duration == 300) five = value;
            if (duration == 10080) weekly = value;
        }
        var summary = Field(result, "rateLimitResetCredits");
        long? count = Field(summary, "availableCount").TryLong();
        if (count < 0) count = null;
        List<Credit>? credits = null;
        var rows = Field(summary, "credits");
        if (rows.ValueKind == JsonValueKind.Array)
        {
            credits = [];
            foreach (var row in rows.EnumerateArray())
            {
                if (Field(row, "status").ToString() != "available" || Field(row, "resetType").ToString() != "codexRateLimits") continue;
                var id = Field(row, "id");
                if (id.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(id.GetString()))
                    credits.Add(new Credit(id.GetString()!, Timestamp(Field(row, "expiresAt"))));
            }
        }
        return new(five, weekly, count, credits);
    }

    private static JsonElement Field(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;
    private static double? Number(JsonElement e, string name) =>
        Field(e, name) is var v && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n) && double.IsFinite(n) ? n : null;
    private static DateTimeOffset? Timestamp(JsonElement e)
    {
        var n = e.TryLong();
        if (n == null) return null;
        try { return DateTimeOffset.FromUnixTimeSeconds(n.Value); } catch (ArgumentOutOfRangeException) { return null; }
    }
}
internal static class JsonExtensions
{
    public static long? TryLong(this JsonElement e) => e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var n) ? n : null;
}
