using System.Diagnostics;
using System.Reflection;
using MouseBatteryTray.UI;

namespace MouseBatteryTray;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly DeviceManager _deviceManager;
    private readonly System.Windows.Forms.Timer _uiTimer;
    private readonly System.Windows.Forms.Timer _updateCheckTimer;
    private readonly BatteryPopupForm _popup;
    private readonly AppSettings _settings;
    private readonly HashSet<string> _lowBatteryNotified = new();
    private readonly HashSet<string> _fullChargeNotified = new();
    private Action? _pendingBalloonAction;

    private static readonly string AppVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public TrayApplicationContext()
    {
        _settings = AppSettings.Load();
        Strings.SetLanguage(_settings.Language);
        _deviceManager = new DeviceManager(_settings);

        _popup = new BatteryPopupForm(_settings);
        _popup.RefreshRequested += RefreshIcon;
        _popup.ExitRequested += ExitApp;
        _popup.SettingsRequested += OpenSettings;
        _popup.DeviceCardClicked += LaunchCompanionApp;

        _notifyIcon = new NotifyIcon
        {
            Icon = TrayIconRenderer.Render(null),
            Text = Strings.TrayScanning,
            Visible = true,
        };
        _notifyIcon.MouseUp += (_, e) =>
        {
            if (e.Button is MouseButtons.Left or MouseButtons.Right) TogglePopup();
        };
        _notifyIcon.BalloonTipClicked += (_, _) =>
        {
            _pendingBalloonAction?.Invoke();
            _pendingBalloonAction = null;
        };

        _uiTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _uiTimer.Tick += (_, _) => RefreshIcon();
        _uiTimer.Start();

        _updateCheckTimer = new System.Windows.Forms.Timer { Interval = (int)TimeSpan.FromHours(24).TotalMilliseconds };
        _updateCheckTimer.Tick += (_, _) => _ = CheckForUpdateAsync();
        _updateCheckTimer.Start();

        RefreshIcon();
        _ = CheckForUpdateAsync();
    }

    private void TogglePopup()
    {
        if (_popup.Visible)
        {
            _popup.Hide();
            return;
        }

        RefreshIcon();
        _popup.ShowNear(Cursor.Position);
    }

    private void OpenSettings()
    {
        _popup.Hide();
        using var form = new SettingsForm(_settings);
        if (form.ShowDialog() == DialogResult.OK)
        {
            Strings.SetLanguage(_settings.Language);
            _deviceManager.ApplySettings(_settings);
            RefreshIcon();
        }
    }

    private void LaunchCompanionApp(string providerId)
    {
        if (!_settings.Devices.TryGetValue(providerId, out var setting)) return;
        if (string.IsNullOrWhiteSpace(setting.CompanionPath)) return;

        try
        {
            Process.Start(new ProcessStartInfo(setting.CompanionPath) { UseShellExecute = true });
        }
        catch
        {
            MessageBox.Show(
                Strings.CompanionLaunchFailed(setting.CompanionPath),
                Strings.AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void RefreshIcon()
    {
        var readings = _deviceManager.GetReadings();

        if (readings.Count == 0)
        {
            _notifyIcon.Icon = TrayIconRenderer.Render(null);
            _notifyIcon.Text = Strings.TrayNoDevices;
            _popup.UpdateReadings(readings);
            return;
        }

        var known = readings.Where(r => r.Reading is not null).ToList();
        int? worst = known.Count > 0 ? known.Min(r => r.Reading!.Percent) : null;

        _notifyIcon.Icon = TrayIconRenderer.Render(worst);

        var lines = readings.Select(r => r.Reading is null
            ? Strings.TrayLineWaiting(r.Label)
            : Strings.TrayLineReading(r.Label, r.Reading.Percent, r.Reading.Charging == true));
        _notifyIcon.Text = Truncate(string.Join("\n", lines), 127);

        _popup.UpdateReadings(readings);
        CheckLowBattery(readings);
        CheckFullCharge(readings);
    }

    private void ShowBalloon(ToolTipIcon icon, string title, string text, Action? onClick)
    {
        _pendingBalloonAction = onClick;
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = text;
        _notifyIcon.ShowBalloonTip(8000);
    }

    private void CheckLowBattery(IReadOnlyList<DeviceManager.DeviceStatus> readings)
    {
        if (!_settings.LowBatteryNotificationsEnabled) return;

        const int hysteresis = 5; // re-arms once the battery has recovered a bit past the threshold
        foreach (var r in readings)
        {
            if (r.Reading is null) continue;

            if (r.Reading.Percent <= _settings.LowBatteryThreshold)
            {
                if (_lowBatteryNotified.Add(r.ProviderId))
                {
                    ShowBalloon(ToolTipIcon.Warning, Strings.LowBatteryTitle, Strings.BalloonRemaining(r.Label, r.Reading.Percent),
                        onClick: () => _popup.ShowNear(Cursor.Position));
                }
            }
            else if (r.Reading.Percent > _settings.LowBatteryThreshold + hysteresis)
            {
                _lowBatteryNotified.Remove(r.ProviderId);
            }
        }
    }

    private void CheckFullCharge(IReadOnlyList<DeviceManager.DeviceStatus> readings)
    {
        if (!_settings.FullChargeNotificationsEnabled) return;

        const int hysteresis = 10; // re-arms once it's drained a bit below the threshold again
        foreach (var r in readings)
        {
            if (r.Reading is null) continue;

            if (r.Reading.Percent >= _settings.FullChargeThreshold)
            {
                if (_fullChargeNotified.Add(r.ProviderId))
                {
                    ShowBalloon(ToolTipIcon.Info, Strings.FullChargeTitle, Strings.BalloonRemaining(r.Label, r.Reading.Percent),
                        onClick: () => _popup.ShowNear(Cursor.Position));
                }
            }
            else if (r.Reading.Percent < _settings.FullChargeThreshold - hysteresis)
            {
                _fullChargeNotified.Remove(r.ProviderId);
            }
        }
    }

    private async Task CheckForUpdateAsync()
    {
        if (!_settings.AutoUpdateCheckEnabled) return;

        var update = await UpdateChecker.CheckAsync(AppVersion);
        if (update is null) return;

        ShowBalloon(ToolTipIcon.Info, Strings.UpdateAvailableTitle,
            Strings.UpdateAvailableText(update.LatestVersion),
            onClick: () =>
            {
                try { Process.Start(new ProcessStartInfo(update.HtmlUrl) { UseShellExecute = true }); }
                catch { /* best-effort */ }
            });
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private void ExitApp()
    {
        _uiTimer.Stop();
        _updateCheckTimer.Stop();
        _notifyIcon.Visible = false;
        _deviceManager.Dispose();
        _notifyIcon.Dispose();
        _popup.Dispose();
        ExitThread();
    }
}
