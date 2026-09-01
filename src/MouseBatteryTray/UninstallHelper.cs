using System.Diagnostics;

namespace MouseBatteryTray;

/// <summary>
/// This app has no formal installer — it's a portable single exe (see the publish settings in
/// MouseBatteryTray.csproj) — so "uninstall" means undoing everything it did on its own: the
/// autostart registry entry and its %AppData% settings folder, then deleting its own exe file.
/// Windows won't let a running process delete its own open file directly, so that last step is
/// handed off to a short-lived detached cmd process that waits for this process to exit first.
/// </summary>
internal static class UninstallHelper
{
    /// <summary>Removes autostart and the settings folder, then schedules the exe file itself for
    /// deletion once this process exits. The caller is responsible for actually exiting afterward —
    /// this only schedules the delete, it doesn't wait for it.</summary>
    public static void Run()
    {
        try { StartupRegistration.SetEnabled(false); }
        catch { /* best-effort: the app is being removed either way */ }

        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MouseBatteryTray");
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch { /* best-effort */ }

        ScheduleSelfDelete();
    }

    private static void ScheduleSelfDelete()
    {
        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath)) return;

        try
        {
            // "ping -n 3" is a portable ~2s delay that doesn't need a real console (unlike
            // `timeout`), giving this process time to fully exit and release the file before del runs.
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/C ping 127.0.0.1 -n 3 >nul & del /f /q \"{exePath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            Process.Start(psi);
        }
        catch { /* best-effort — worst case the user deletes the exe by hand */ }
    }
}
