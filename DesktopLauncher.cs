using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CodexMeter;

internal static class DesktopLauncher
{
    // Stable MSIX application identity verified from the installed package manifest.
    // This is independent of the versioned WindowsApps installation directory.
    internal const string AppId = "OpenAI.Codex_2p2nqsd0c76g0!App";

    public static void Launch()
    {
        object? shell = null, folder = null, item = null;
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application") ?? throw new IOException("Windows Shellを利用できません。");
            shell = Activator.CreateInstance(type)!;
            folder = ((dynamic)shell).NameSpace("shell:AppsFolder");
            item = folder == null ? null : ((dynamic)folder).ParseName(AppId);
            if (item == null) throw new IOException("Microsoft Store版のCodex Desktopが見つかりません。");
            var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            start.ArgumentList.Add("shell:AppsFolder\\" + AppId);
            Process.Start(start)?.Dispose();
        }
        finally
        {
            foreach (var value in new[] { item, folder, shell })
                if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
        }
    }
}
