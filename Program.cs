namespace CodexMeter;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--self-test")) return SelfTests.Run();
        if (args.Contains("--integration-test")) return IntegrationTests.Run();
        if (args.Contains("--persistence-write")) return SelfTests.Persistence(false);
        if (args.Contains("--persistence-check")) return SelfTests.Persistence(true);
        if (args.Contains("--with-codex"))
        {
            try { DesktopLauncher.Launch(); }
            catch { MessageBox.Show("Codex Desktopを起動できません。Microsoft Store版がインストールされているか確認してください。", "Codex Meter", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        }
        using var mutex = new Mutex(true, @"Local\CodexMeterV1", out bool created);
        if (!created) return 0;
        try { using var app = new MeterContext(args.Contains("--show")); Application.Run(app); }
        finally { mutex.ReleaseMutex(); }
        return 0;
    }
}

internal sealed class MeterContext : ApplicationContext
{
    private readonly Form popup = new() {
        Text = "Codex Meter", ClientSize = new Size(240, 30), ShowInTaskbar = false,
        FormBorderStyle = FormBorderStyle.FixedToolWindow, MaximizeBox = false,
        StartPosition = FormStartPosition.Manual, AutoScaleMode = AutoScaleMode.Dpi
    };
    private readonly Label content = new() {
        AutoSize = true, Padding = new Padding(8, 6, 8, 6),
        Font = new Font("Yu Gothic UI", 11, FontStyle.Bold), Text = "Codex情報を取得しています"
    };
    private readonly NotifyIcon tray;
    private readonly System.Windows.Forms.Timer clock = new() { Interval = 1000 };
    private readonly CancellationTokenSource stop = new();
    private readonly NotificationLedger? ledger;
    private readonly Func<CancellationToken, Task<Snapshot>> read;
    private Snapshot? snapshot;
    private DateTimeOffset fetched, nextRead = DateTimeOffset.MinValue;
    private bool reading, closing;
    private string? notificationError;

    public MeterContext(bool show, Func<CancellationToken, Task<Snapshot>>? reader = null)
    {
        read = reader ?? AppServer.ReadAsync;
        popup.AutoScroll = true;
        popup.Controls.Add(content);
        _ = popup.Handle; // Create the UI handle for synchronization, without showing a window.
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        popup.FormClosing += (_, e) => { if (!closing) { e.Cancel = true; popup.Hide(); } };
        var menu = new ContextMenuStrip();
        menu.Items.Add("表示", null, (_, _) => Open());
        menu.Items.Add("終了", null, (_, _) => ExitThread());
        tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "Codex Meter", Visible = true, ContextMenuStrip = menu };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) Open(); };
        try { ledger = new NotificationLedger(Path.Combine(AppContext.BaseDirectory, "state", "notified.json")); }
        catch { notificationError = "通知済みデータを読み取れないため、期限通知を停止しています"; }
        clock.Tick += async (_, _) => {
            if (!reading && DateTimeOffset.UtcNow >= nextRead) await Refresh();
            else { Render(); NotifyDue(); }
        };
        clock.Start();
        if (show) Open(); else _ = Refresh();
    }

    private void Open()
    {
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        popup.Location = new Point(area.Right - popup.Width - 12, area.Bottom - popup.Height - 12);
        popup.Show(); popup.Activate();
        _ = Refresh();
    }

    private async Task Refresh()
    {
        if (reading || closing) return;
        reading = true;
        snapshot = null; // Never keep a previous value visible through a failed refresh.
        Render();
        try
        {
            var value = await read(stop.Token);
            if (!closing) { snapshot = value; fetched = DateTimeOffset.UtcNow; }
        }
        catch { snapshot = null; }
        finally
        {
            reading = false;
            nextRead = DateTimeOffset.UtcNow.AddMinutes(5);
            if (!closing) { Render(); NotifyDue(); }
        }
    }

    private void Render()
    {
        var now = DateTimeOffset.UtcNow;
        if (snapshot != null && now - fetched > TimeSpan.FromMinutes(5)) snapshot = null;
        popup.Text = Display.Title(snapshot);
        content.Text = snapshot == null
            ? (reading ? "Codex情報を取得しています" : "Codex情報を取得できません")
            : Display.WindowText("5h", snapshot.FiveHour, now, false) + "\n" +
              Display.WindowText("週", snapshot.Weekly, now, true);
        if (snapshot != null && Display.TicketText(snapshot, now) is { Length: > 0 } tickets) content.Text += "\n" + tickets;
        if (notificationError != null) content.Text += "\n\n" + notificationError;
        content.MaximumSize = Size.Empty;
        var size = content.GetPreferredSize(Size.Empty);
        size.Width = Math.Max(size.Width, TextRenderer.MeasureText(popup.Text, SystemFonts.CaptionFont).Width + SystemInformation.SmallCaptionButtonSize.Width + 12);
        if (popup.ClientSize != size)
        {
            var area = Screen.FromControl(popup).WorkingArea;
            popup.ClientSize = size;
            popup.Location = new Point(Math.Max(area.Left, Math.Min(popup.Left, area.Right - popup.Width)),
                Math.Max(area.Top, Math.Min(popup.Top, area.Bottom - popup.Height)));
        }
    }

    private void NotifyDue()
    {
        if (ledger == null || snapshot?.Credits == null || notificationError != null || snapshot.AvailableCount is not > 0) return;
        try
        {
            // Reserve durably before delivery, prioritizing no duplicate after restart.
            var due = ledger.Reserve(snapshot.Credits, DateTimeOffset.UtcNow);
            if (due.Count == 0) return;
            // One notification groups simultaneously expiring credits so balloons do not replace one another.
            var dates = string.Join("\n", due.Select(c => $"期限：{c.ExpiresAt!.Value.ToLocalTime():M月d日 HH:mm}").Distinct());
            tray.ShowBalloonTip(10000, "Codex Meter", "リセットチケットの有効期限まで24時間以内です。\n\n" + dates, ToolTipIcon.Info);
        }
        catch { notificationError = "期限通知を処理できません。通知済みデータの保存先を確認してください"; Render(); }
    }

    protected override void ExitThreadCore()
    {
        closing = true;
        stop.Cancel(); clock.Stop(); tray.Visible = false;
        popup.Close();
        base.ExitThreadCore();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { clock.Dispose(); tray.ContextMenuStrip?.Dispose(); tray.Dispose(); popup.Dispose(); stop.Dispose(); }
        base.Dispose(disposing);
    }
}
