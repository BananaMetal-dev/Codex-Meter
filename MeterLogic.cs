using System.Text.Json;

namespace CodexMeter;

internal sealed class NotificationLedger(string path)
{
    private readonly Dictionary<string, long> sent = Load(path);
    private static Dictionary<string, long> Load(string path)
    {
        if (!File.Exists(path)) return [];
        // Fail closed on corrupt state: never silently reset deduplication.
        return JsonSerializer.Deserialize<Dictionary<string, long>>(File.ReadAllText(path))
            ?? throw new IOException("通知済みデータを読み取れません");
    }
    public static bool IsDue(Credit credit, DateTimeOffset now) =>
        credit.ExpiresAt is { } expiry && expiry > now && expiry <= now.AddHours(24);

    public List<Credit> Reserve(IEnumerable<Credit> credits, DateTimeOffset now)
    {
        var due = credits.Where(c => IsDue(c, now) && !sent.ContainsKey(c.Id)).DistinctBy(c => c.Id).ToList();
        var next = sent.Where(p => p.Value > now.ToUnixTimeSeconds()).ToDictionary();
        foreach (var credit in due) next[credit.Id] = credit.ExpiresAt!.Value.ToUnixTimeSeconds();
        if (due.Count == 0 && next.Count == sent.Count) return due;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(file, next);
            file.Flush(flushToDisk: true);
        }
        File.Move(temp, path, overwrite: true);
        sent.Clear();
        foreach (var pair in next) sent.Add(pair.Key, pair.Value);
        return due;
    }
}

internal static class Display
{
    public static string WindowText(string title, Window? window, DateTimeOffset now, bool weekly)
    {
        if (window == null || window.ResetsAt <= now) return title + ":取得できません";
        var usage = window.UsedPercent is { } p ? $"残り{100 - p:0.#}%" : "取得できません";
        var reset = window.ResetsAt is { } date ? date.ToLocalTime().ToString(weekly ? "M月d日 HH:mm" : "HH:mm") : "取得できません";
        return $"{title}:{usage}  ﾘｾｯﾄ:{reset}";
    }
    public static string Title(Snapshot? snapshot) => snapshot?.AvailableCount is > 0
        ? $"Codex Meter  ﾁｹｯﾄ:{snapshot.AvailableCount}枚" : "Codex Meter";
    public static string TicketText(Snapshot snapshot, DateTimeOffset now)
    {
        if (snapshot.AvailableCount == 0) return "";
        var lines = new List<string>();
        if (snapshot.AvailableCount == null) lines.Add("ﾁｹｯﾄ:取得できません");
        if (snapshot.Credits == null || snapshot.Credits.Count == 0)
        {
            lines.Add("期限:取得できません");
            return string.Join("\n", lines);
        }
        var credits = snapshot.Credits.OrderBy(c => c.ExpiresAt ?? DateTimeOffset.MaxValue).ToList();
        for (int i = 0; i < credits.Count; i++)
        {
            var expiry = credits[i].ExpiresAt is { } date && date > now ? date.ToLocalTime().ToString("M月d日 HH:mm") : "取得できません";
            lines.Add($"{i + 1}枚目:{expiry}");
        }
        if (snapshot.AvailableCount > credits.Count) lines.Add("ほか:期限取得できません");
        return string.Join("\n", lines);
    }
}
