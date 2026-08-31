using System.Diagnostics;
using MouseBatteryTray.UI;

namespace MouseBatteryTray;

public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly DeviceManager _deviceManager;
    private readonly System.Windows.Forms.Timer _uiTimer;
    private readonly BatteryPopupForm _popup;
    private readonly AppSettings _settings;
    private readonly HashSet<string> _lowBatteryNotified = new();

    public TrayApplicationContext()
    {
        _settings = AppSettings.Load();
        _deviceManager = new DeviceManager(_settings);

        _popup = new BatteryPopupForm(_settings);
        _popup.RefreshRequested += RefreshIcon;
        _popup.ExitRequested += ExitApp;
        _popup.SettingsRequested += OpenSettings;
        _popup.DeviceCardClicked += LaunchCompanionApp;

        _notifyIcon = new NotifyIcon
        {
            Icon = TrayIconRenderer.Render(null),
            Text = "マウスバッテリー: スキャン中...",
            Visible = true,
        };
        _notifyIcon.MouseUp += (_, e) =>
        {
            if (e.Button is MouseButtons.Left or MouseButtons.Right) TogglePopup();
        };

        _uiTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _uiTimer.Tick += (_, _) => RefreshIcon();
        _uiTimer.Start();

        RefreshIcon();
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
                $"連携ソフトを起動できませんでした:\n{setting.CompanionPath}",
                "マウスバッテリー",
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
            _notifyIcon.Text = "マウスバッテリー: 対応デバイス未検出";
            _popup.UpdateReadings(readings);
            return;
        }

        var known = readings.Where(r => r.Reading is not null).ToList();
        int? worst = known.Count > 0 ? known.Min(r => r.Reading!.Percent) : null;

        _notifyIcon.Icon = TrayIconRenderer.Render(worst);

        var lines = readings.Select(r => r.Reading is null
            ? $"{r.Label}: 応答待ち..."
            : $"{r.Label}: {r.Reading.Percent}%{(r.Reading.Charging == true ? " (充電中)" : "")}");
        _notifyIcon.Text = Truncate(string.Join("\n", lines), 127);

        _popup.UpdateReadings(readings);
        CheckLowBattery(readings);
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
                    _notifyIcon.BalloonTipIcon = ToolTipIcon.Warning;
                    _notifyIcon.BalloonTipTitle = "バッテリー残量が低下しています";
                    _notifyIcon.BalloonTipText = $"{r.Label}: 残り{r.Reading.Percent}%";
                    _notifyIcon.ShowBalloonTip(8000);
                }
            }
            else if (r.Reading.Percent > _settings.LowBatteryThreshold + hysteresis)
            {
                _lowBatteryNotified.Remove(r.ProviderId);
            }
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private void ExitApp()
    {
        _uiTimer.Stop();
        _notifyIcon.Visible = false;
        _deviceManager.Dispose();
        _notifyIcon.Dispose();
        _popup.Dispose();
        ExitThread();
    }
}
