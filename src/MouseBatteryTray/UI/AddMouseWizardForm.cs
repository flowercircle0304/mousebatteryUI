using MouseBatteryTray.Providers;

namespace MouseBatteryTray.UI;

/// <summary>
/// Fully offline, deterministic "add a mouse" wizard — no AI involved. It listens for battery
/// reports (and, opt-in, probes the one already-validated COMPX request) and matches raw bytes
/// against a percentage the user typed in after checking the vendor's own app.
/// </summary>
internal sealed class AddMouseWizardForm : Form
{
    private readonly AppSettings _settings;
    private readonly ComboBox _deviceCombo;
    private readonly NumericUpDown _percentInput;
    private readonly Button _scanButton;
    private readonly Button _activeScanButton;
    private readonly TextBox _log;
    private readonly TextBox _nameInput;
    private readonly Button _saveButton;
    private readonly Label _resultLabel;

    private IReadOnlyList<DeviceDiscovery.UnrecognizedDevice> _devices = Array.Empty<DeviceDiscovery.UnrecognizedDevice>();
    private DeviceDiscovery.PassiveMatch? _passiveMatch;
    private DeviceDiscovery.ActiveMatch? _activeMatch;
    private CancellationTokenSource? _scanCts;

    public AddMouseWizardForm(AppSettings settings)
    {
        _settings = settings;

        Text = "新しいマウスを追加";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Background;
        ForeColor = Theme.TextPrimary;
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(520, 460);
        Padding = new Padding(16);

        var title = new Label
        {
            Text = "対象デバイス",
            ForeColor = Theme.AccentCyan,
            Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(16, 14),
        };
        Controls.Add(title);

        _deviceCombo = new ComboBox
        {
            Location = new Point(16, 42),
            Width = 340,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Theme.CardBackground,
            ForeColor = Theme.TextPrimary,
        };
        Controls.Add(_deviceCombo);

        var rescanButton = new Button
        {
            Text = "再スキャン",
            Location = new Point(364, 41),
            Width = 90,
            Height = 24,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.CardBackground,
            ForeColor = Theme.AccentCyan,
        };
        rescanButton.FlatAppearance.BorderColor = Theme.Border;
        rescanButton.Click += (_, _) => RefreshDeviceList();
        Controls.Add(rescanButton);

        var hint = new Label
        {
            Text = "対応済みでないマウスの受信機だけが一覧に出ます。見当たらない場合は挿し直して「再スキャン」してください。",
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            MaximumSize = new Size(490, 0),
            Location = new Point(16, 68),
        };
        Controls.Add(hint);

        var pctLabel = new Label
        {
            Text = "現在のバッテリー%（付属の公式ソフトや本体表示で確認）：",
            AutoSize = true,
            Location = new Point(16, 106),
            ForeColor = Theme.TextPrimary,
        };
        Controls.Add(pctLabel);

        _percentInput = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 100,
            Value = 50,
            Location = new Point(pctLabel.Right + 8, 104),
            Width = 60,
            BackColor = Theme.CardBackground,
            ForeColor = Theme.TextPrimary,
        };
        Controls.Add(_percentInput);

        _scanButton = new Button
        {
            Text = "スキャン開始（受信待ち・安全）",
            Location = new Point(16, 138),
            Width = 220,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.AccentCyan,
            ForeColor = Theme.Background,
        };
        _scanButton.FlatAppearance.BorderSize = 0;
        _scanButton.Click += (_, _) => RunPassiveScan();
        Controls.Add(_scanButton);

        _activeScanButton = new Button
        {
            Text = "アクティブ探索も試す（診断コマンド送信）",
            Location = new Point(244, 138),
            Width = 250,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.CardBackground,
            ForeColor = Theme.TextMuted,
            Enabled = false,
        };
        _activeScanButton.FlatAppearance.BorderColor = Theme.Border;
        _activeScanButton.Click += (_, _) => RunActiveScan();
        Controls.Add(_activeScanButton);

        _log = new TextBox
        {
            Location = new Point(16, 180),
            Width = 488,
            Height = 160,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Theme.CardBackground,
            ForeColor = Theme.TextMuted,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 8.5f),
        };
        Controls.Add(_log);

        _resultLabel = new Label
        {
            Text = "",
            AutoSize = true,
            Location = new Point(16, 350),
            ForeColor = Theme.LevelHigh,
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
        };
        Controls.Add(_resultLabel);

        var nameLabel = new Label { Text = "表示名：", Location = new Point(16, 380), AutoSize = true, ForeColor = Theme.TextPrimary };
        Controls.Add(nameLabel);

        _nameInput = new TextBox
        {
            Location = new Point(80, 377),
            Width = 300,
            BackColor = Theme.CardBackground,
            ForeColor = Theme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Enabled = false,
        };
        Controls.Add(_nameInput);

        _saveButton = new Button
        {
            Text = "この設定を保存",
            Location = new Point(16, 412),
            Width = 140,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.LevelHigh,
            ForeColor = Theme.Background,
            Enabled = false,
        };
        _saveButton.FlatAppearance.BorderSize = 0;
        _saveButton.Click += (_, _) => SaveAndClose();
        Controls.Add(_saveButton);

        var closeButton = new Button
        {
            Text = "閉じる",
            DialogResult = DialogResult.Cancel,
            Location = new Point(ClientSize.Width - 88, 412),
            Width = 72,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.CardBackground,
            ForeColor = Theme.TextMuted,
        };
        closeButton.FlatAppearance.BorderColor = Theme.Border;
        Controls.Add(closeButton);
        CancelButton = closeButton;

        RefreshDeviceList();
    }

    private void RefreshDeviceList()
    {
        _devices = DeviceDiscovery.ListUnrecognizedDevices(_settings);
        _deviceCombo.Items.Clear();
        foreach (var d in _devices)
            _deviceCombo.Items.Add($"{d.DisplayName} (VID_{d.VendorId:X4}&PID_{d.ProductId:X4})");
        if (_deviceCombo.Items.Count > 0) _deviceCombo.SelectedIndex = 0;
    }

    private void AppendLog(string line)
    {
        if (InvokeRequired) { BeginInvoke(() => AppendLog(line)); return; }
        _log.AppendText(line + Environment.NewLine);
    }

    private void RunPassiveScan()
    {
        if (_deviceCombo.SelectedIndex < 0)
        {
            AppendLog("デバイスを選択してください。");
            return;
        }

        var device = _devices[_deviceCombo.SelectedIndex];
        int target = (int)_percentInput.Value;

        _log.Clear();
        _resultLabel.Text = "";
        _saveButton.Enabled = false;
        _nameInput.Enabled = false;
        _scanButton.Enabled = false;
        _activeScanButton.Enabled = false;

        AppendLog($"[受信待ちスキャン] {device.DisplayName} / 目標値 {target}%");
        AppendLog("マウスを軽く動かすかクリックすると受信間隔が早まることがあります。");

        _scanCts = new CancellationTokenSource();
        var ct = _scanCts.Token;

        Task.Run(() =>
        {
            var match = DeviceDiscovery.TryPassiveMatch(device.VendorId, device.ProductId, target, AppendLog, ct);

            BeginInvoke(() =>
            {
                _scanButton.Enabled = true;
                if (match is not null)
                {
                    _passiveMatch = match;
                    _activeMatch = null;
                    _resultLabel.ForeColor = Theme.LevelHigh;
                    _resultLabel.Text = "✓ 見つかりました（受信待ち方式）";
                    _nameInput.Text = device.DisplayName;
                    _nameInput.Enabled = true;
                    _saveButton.Enabled = true;
                }
                else
                {
                    AppendLog("受信待ちスキャンでは見つかりませんでした。");
                    _activeScanButton.Enabled = true;
                }
            });
        }, ct);
    }

    private void RunActiveScan()
    {
        if (_deviceCombo.SelectedIndex < 0) return;
        var device = _devices[_deviceCombo.SelectedIndex];
        int target = (int)_percentInput.Value;

        var confirm = MessageBox.Show(
            this,
            "デバイスに1件だけ診断コマンド（バッテリー残量取得用、既知の安全なコマンド）を送信します。\n" +
            "通常は問題ありませんが、対応していないデバイスの場合は無視されるか、想定外の反応をする可能性があります。続行しますか？",
            "アクティブ探索の確認",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        _activeScanButton.Enabled = false;
        AppendLog($"[アクティブ探索] {device.DisplayName} / 目標値 {target}%");
        AppendLog("マウスがスリープ状態だと応答しないことがあります。軽く動かしてから実行してください。");

        Task.Run(() =>
        {
            var match = DeviceDiscovery.TryActiveCompxMatch(device.VendorId, device.ProductId, target, AppendLog);

            BeginInvoke(() =>
            {
                if (match is not null)
                {
                    _activeMatch = match;
                    _passiveMatch = null;
                    _resultLabel.ForeColor = Theme.LevelHigh;
                    _resultLabel.Text = "✓ 見つかりました（COMPX方式）";
                    _nameInput.Text = device.DisplayName;
                    _nameInput.Enabled = true;
                    _saveButton.Enabled = true;
                }
                else
                {
                    _resultLabel.ForeColor = Theme.LevelLow;
                    _resultLabel.Text = "✗ 自動では見つかりませんでした。手動解析が必要です。";
                    _activeScanButton.Enabled = true;
                }
            });
        });
    }

    private void SaveAndClose()
    {
        if (_deviceCombo.SelectedIndex < 0) return;
        var device = _devices[_deviceCombo.SelectedIndex];
        string displayName = string.IsNullOrWhiteSpace(_nameInput.Text) ? device.DisplayName : _nameInput.Text.Trim();
        string id = $"custom-{device.VendorId:x4}-{device.ProductId:x4}";

        var spec = new DiscoveredDeviceSpec
        {
            Id = id,
            DisplayName = displayName,
            VendorId = device.VendorId,
            ProductId = device.ProductId,
        };

        if (_passiveMatch is { } pm)
        {
            spec.Kind = "passive-push";
            spec.ReportLength = pm.ReportLength;
            spec.BatteryByteOffset = pm.ByteOffset;
        }
        else if (_activeMatch is { } am)
        {
            spec.Kind = "compx";
            spec.OutputReportId = am.OutputReportId;
            spec.CommandId = am.CommandId;
        }
        else
        {
            return;
        }

        _settings.DiscoveredDevices.RemoveAll(d => d.VendorId == device.VendorId && d.ProductId == device.ProductId);
        _settings.DiscoveredDevices.Add(spec);
        _settings.Save();

        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _scanCts?.Cancel();
        base.OnFormClosed(e);
    }
}
