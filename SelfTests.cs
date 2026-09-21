using System.Text.Json;

namespace CodexMeter;
internal static class SelfTests
{
    public static int Persistence(bool check)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "test-artifacts", "restart-test.json");
        if (!check && File.Exists(path)) File.Delete(path);
        var ledger = new NotificationLedger(path);
        var now = DateTimeOffset.FromUnixTimeSeconds(1800000000);
        var due = ledger.Reserve([new Credit("synthetic-restart", now.AddHours(1))], now);
        bool pass = due.Count == (check ? 0 : 1);
        if (check) File.Delete(path);
        return pass ? 0 : 1;
    }
    public static int Run()
    {
        var results = new List<string>();
        void Check(bool value, string name) { results.Add((value ? "PASS " : "FAIL ") + name); }
        var now = DateTimeOffset.FromUnixTimeSeconds(1800000000);
        Snapshot Parse(string json) { using var doc = JsonDocument.Parse(json); return Snapshot.Parse(doc.RootElement); }
        var empty = Parse("{}");
        Check(empty.FiveHour == null && empty.Weekly == null && empty.AvailableCount == null && empty.Credits == null, "missing values remain unknown");
        Check(Display.WindowText("5時間枠", null, now, false).Contains("取得できません"), "unknown display is not zero");
        var data = Parse("""{"rateLimits":{"limitId":"codex","primary":{"windowDurationMins":10080,"usedPercent":61,"resetsAt":1800300000},"secondary":{"windowDurationMins":300,"usedPercent":34,"resetsAt":1800008100}},"rateLimitResetCredits":{"availableCount":2,"credits":null}}""");
        Check(data.FiveHour?.UsedPercent == 34 && data.Weekly?.UsedPercent == 61, "windows selected by duration");
        Check(Display.WindowText("5時間枠", data.FiveHour, now, false).Contains(data.FiveHour!.ResetsAt!.Value.ToLocalTime().ToString("HH:mm")), "5h reset time");
        Check(Display.WindowText("週間枠", data.Weekly, now, true).Contains(data.Weekly!.ResetsAt!.Value.ToLocalTime().ToString("M月d日 HH:mm")), "weekly reset date");
        Check(Display.Title(data).Contains("2枚") && Display.TicketText(data, now).Contains("取得できません"), "count without fabricated expiry");
        Check(Display.WindowText("5時間枠", data.FiveHour, now.AddDays(1), false).Contains("取得できません"), "passed reset never displays old usage");
        var map = Parse("""{"rateLimits":{"primary":{"windowDurationMins":300,"usedPercent":99}},"rateLimitsByLimitId":{"other":{} }}""");
        Check(map.FiveHour == null, "no fallback to unrelated bucket");
        var invalid = Parse("""{"rateLimits":{"primary":{"windowDurationMins":300,"usedPercent":-1,"resetsAt":999999999999999999}},"rateLimitResetCredits":{"availableCount":-1}}""");
        Check(invalid.FiveHour?.UsedPercent == null && invalid.FiveHour?.ResetsAt == null && invalid.AvailableCount == null, "invalid values remain unknown");
        var filtered = Parse("""{"rateLimitResetCredits":{"availableCount":4,"credits":[{"id":"a","status":"redeemed","resetType":"codexRateLimits","expiresAt":1800000010},{"id":"b","status":"redeeming","resetType":"codexRateLimits","expiresAt":1800000010},{"id":"c","status":"unknown","resetType":"codexRateLimits","expiresAt":1800000010},{"id":"d","status":"available","resetType":"codexRateLimits","expiresAt":1800000010}]}}""");
        Check(filtered.Credits?.Count == 1 && filtered.Credits[0].Id == "d", "redeemed/redeeming/unknown excluded");
        Check(Display.TicketText(filtered, now).Contains("ほか:期限取得できません"), "capped details remain unknown");
        Check(NotificationLedger.IsDue(new("a", now.AddHours(24)), now), "24 hours inclusive");
        Check(!NotificationLedger.IsDue(new("a", now.AddHours(24).AddSeconds(1)), now), "over 24 hours excluded");
        Check(!NotificationLedger.IsDue(new("a", now), now), "expired excluded");
        Check(!NotificationLedger.IsDue(new("a", null), now), "unknown expiry excluded");
        var folder = Path.Combine(AppContext.BaseDirectory, "test-artifacts");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "ledger-" + Guid.NewGuid().ToString("N") + ".json");
        var credit = new Credit("synthetic-credit", now.AddHours(1));
        try
        {
            var first = new NotificationLedger(path);
            Check(first.Reserve([credit, credit], now).Count == 1, "duplicate rows notified once");
            Check(first.Reserve([credit], now).Count == 0, "same session deduplication");
            var restarted = new NotificationLedger(path);
            Check(restarted.Reserve([credit], now).Count == 0, "restart deduplication");
            restarted.Reserve([], now.AddHours(2));
            Check(File.ReadAllText(path) == "{}", "expired notification state pruned");
            File.WriteAllText(path, "invalid");
            bool rejected = false;
            try { _ = new NotificationLedger(path); } catch (JsonException) { rejected = true; }
            Check(rejected, "corrupt state fails closed");
        }
        finally { File.Delete(path); }
        File.WriteAllLines(Path.Combine(folder, "results.txt"), results);
        return results.Any(r => r.StartsWith("FAIL")) ? 1 : 0;
    }
}
