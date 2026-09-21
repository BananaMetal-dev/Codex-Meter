using System.Reflection;
namespace CodexMeter;
internal static class IntegrationTests
{
    public static int Run()
    {
        var results = new List<string>();
        void Check(bool value, string name) => results.Add((value ? "PASS " : "FAIL ") + name);
        using var context = new MeterContext(false);
        T Field<T>(string name) => (T)typeof(MeterContext).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(context)!;
        using var timer = new System.Windows.Forms.Timer { Interval = 200 };
        int phase = 0;
        var deadline = DateTime.UtcNow.AddSeconds(40);
        timer.Tick += (_, _) => {
            if (DateTime.UtcNow > deadline)
            {
                Check(false, "app-server integration timeout"); timer.Stop(); context.ExitThread(); return;
            }
            if (Field<bool>("reading")) return;
            var popup = Field<Form>("popup");
            var tray = Field<NotifyIcon>("tray");
            if (phase == 0)
            {
                Check(tray.Visible && !popup.Visible, "startup is tray-only");
                var data = Field<Snapshot?>("snapshot");
                Check(data?.FiveHour?.UsedPercent != null && data?.Weekly?.ResetsAt != null && data?.AvailableCount != null, "real app-server values reach UI model");
                typeof(NotifyIcon).GetMethod("OnMouseClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(tray, [new MouseEventArgs(MouseButtons.Left,1,0,0,0)]);
                Check(popup.Visible, "tray left-click handler opens popup");
                phase = 1;
            }
            else
            {
                Check(Field<Label>("content").Text.Contains("週:"), "popup renders refreshed values");
                popup.Close();
                Check(!popup.Visible && tray.Visible, "window close keeps tray alive");
                timer.Stop();
                ((ToolStripMenuItem)tray.ContextMenuStrip!.Items[1]).PerformClick();
                Check(!tray.Visible, "exit menu handler removes tray and exits message loop");
            }
        };
        timer.Start();
        Application.Run(context);
        int reads = 0;
        using (var failures = new MeterContext(false, _ => ++reads == 1
            ? Task.FromResult(new Snapshot(new Window(34, DateTimeOffset.UtcNow.AddHours(1)), null, 0, []))
            : Task.FromException<Snapshot>(new IOException("synthetic failure"))))
        {
            typeof(MeterContext).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(failures, null);
            var label = (Label)typeof(MeterContext).GetField("content", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(failures)!;
            Check(label.Text == "Codex情報を取得できません" && !label.Text.Contains("34"), "failed refresh clears previous successful value");
            failures.ExitThread();
        }
        var folder = Path.Combine(AppContext.BaseDirectory, "test-artifacts");
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "integration-results.txt"), results);
        return results.Any(r => r.StartsWith("FAIL")) ? 1 : 0;
    }
}
