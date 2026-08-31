using MouseBatteryTray.Providers;

namespace MouseBatteryTray.UI;

/// <summary>
/// General app preferences plus per-mouse monitoring on/off and companion-app links.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly List<(IMouseBatteryProvider Provider, CheckBox Check, TextBox Path)> _rows = new();

    private CheckBox _startupCheck = null!;
    private CheckBox _notifyCheck = null!;
    private NumericUpDown _thresholdInput = null!;

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;

        Text = "マウスバッテリー設定";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        ForeColor = Theme.TextPrimary;
        Font = new Font("Segoe UI", 9f);
        int visibleCount = ProviderRegistry.BuildAll(settings).Count(p => !settings.IsHidden(p.Id));
        int hiddenCount = ProviderRegistry.BuildAll(settings).Count - visibleCount;
        ClientSize = new Size(560, 260 + visibleCount * 40 + (hiddenCount > 0 ? 30 : 0));
        Padding = new Padding(16);

        BuildGeneralSection();
        BuildDeviceSection();
        BuildFooter();
    }

    private static Label SectionTitle(string text, int y) => new()
    {
        Text = text,
        ForeColor = Theme.AccentCyan,
        Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold),
        AutoSize = true,
        Location = new Point(16, y),
    };

    private void BuildGeneralSection()
    {
        Controls.Add(SectionTitle("全般", 14));

        _startupCheck = new CheckBox
        {
            Text = "Windows ログイン時に自動起動する",
            Checked = StartupRegistration.IsEnabled(),
            Location = new Point(16, 44),
            AutoSize = true,
            ForeColor = Theme.TextPrimary,
            FlatStyle = FlatStyle.Flat,
        };
        Controls.Add(_startupCheck);

        _notifyCheck = new CheckBox
        {
            Text = "バッテリー残量が下がったら通知する（しきい値：",
            Checked = _settings.LowBatteryNotificationsEnabled,
            Location = new Point(16, 74),
            AutoSize = true,
            ForeColor = Theme.TextPrimary,
            FlatStyle = FlatStyle.Flat,
        };
        Controls.Add(_notifyCheck);

        _thresholdInput = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 90,
            Value = Math.Clamp(_settings.LowBatteryThreshold, 1, 90),
            Location = new Point(_notifyCheck.Right + 4, 72),
            Width = 55,
            BackColor = Theme.CardBackground,
            ForeColor = Theme.TextPrimary,
        };
        Controls.Add(_thresholdInput);

        var percentLabel = new Label
        {
            Text = "% 以下）",
            Location = new Point(_thresholdInput.Right + 4, 76),
            AutoSize = true,
            ForeColor = Theme.TextPrimary,
        };
        Controls.Add(percentLabel);

        _notifyCheck.CheckedChanged += (_, _) => _thresholdInput.Enabled = _notifyCheck.Checked;
        _thresholdInput.Enabled = _notifyCheck.Checked;
    }

    private void BuildDeviceSection()
    {
        Controls.Add(SectionTitle("対応マウス", 118));

        var hint = new Label
        {
            Text = "チェックを外すとそのマウスの監視を停止します。連携ソフトのパス(または URL)を登録すると、\nポップアップでそのデバイスをクリックしたときに起動できます。",
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Location = new Point(16, 144),
        };
        Controls.Add(hint);

        var addButton = new Button
        {
            Text = "＋ 新しいマウスを追加...",
            Location = new Point(370, 114),
            Width = 170,
            Height = 24,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.CardBackground,
            ForeColor = Theme.AccentCyan,
        };
        addButton.FlatAppearance.BorderColor = Theme.Border;
        addButton.Click += (_, _) =>
        {
            using var wizard = new AddMouseWizardForm(_settings);
            if (wizard.ShowDialog(this) == DialogResult.OK)
            {
                MessageBox.Show(
                    this,
                    "マウスを追加しました。反映するには一度この設定画面を閉じて開き直してください。",
                    "マウスバッテリー設定",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        };
        Controls.Add(addButton);

        var allProviders = ProviderRegistry.BuildAll(_settings);
        var hiddenProviders = allProviders.Where(p => _settings.IsHidden(p.Id)).ToList();

        int y = 188;
        foreach (var provider in allProviders)
        {
            if (_settings.IsHidden(provider.Id)) continue;

            var setting = _settings.GetOrCreate(provider.Id);

            var check = new CheckBox
            {
                Checked = setting.Enabled,
                Location = new Point(16, y + 4),
                Width = 20,
                FlatStyle = FlatStyle.Flat,
            };

            var label = new Label
            {
                Text = provider.DisplayName,
                Location = new Point(40, y + 6),
                Width = 180,
                ForeColor = Theme.TextPrimary,
                AutoEllipsis = true,
            };

            var pathBox = new TextBox
            {
                Text = setting.CompanionPath,
                Location = new Point(224, y + 3),
                Width = 190,
                BackColor = Theme.CardBackground,
                ForeColor = Theme.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                PlaceholderText = "未設定（クリックしても何も起きません）",
            };

            var browse = new Button
            {
                Text = "参照...",
                Location = new Point(420, y + 2),
                Width = 60,
                Height = 24,
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.CardBackground,
                ForeColor = Theme.AccentCyan,
            };
            browse.FlatAppearance.BorderColor = Theme.Border;
            browse.Click += (_, _) =>
            {
                using var dlg = new OpenFileDialog
                {
                    Filter = "実行ファイル (*.exe)|*.exe|すべてのファイル (*.*)|*.*",
                    Title = $"{provider.DisplayName} の連携ソフトを選択",
                };
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    pathBox.Text = dlg.FileName;
            };

            Controls.Add(check);
            Controls.Add(label);
            Controls.Add(pathBox);
            Controls.Add(browse);

            bool isCustom = _settings.DiscoveredDevices.Any(d => d.Id == provider.Id);

            var delete = new Button
            {
                Text = "削除",
                Location = new Point(486, y + 2),
                Width = 58,
                Height = 24,
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.CardBackground,
                ForeColor = Theme.LevelLow,
            };
            delete.FlatAppearance.BorderColor = Theme.Border;
            delete.Click += (_, _) =>
            {
                if (isCustom) DeleteDiscoveredDevice(provider);
                else HideBuiltInDevice(provider);
            };
            Controls.Add(delete);

            _rows.Add((provider, check, pathBox));
            y += 40;
        }

        if (hiddenProviders.Count > 0)
        {
            var unhideLink = new LinkLabel
            {
                Text = $"非表示にした {hiddenProviders.Count} 件を再表示する",
                Location = new Point(16, y + 6),
                AutoSize = true,
                LinkColor = Theme.AccentCyan,
                ActiveLinkColor = Theme.AccentViolet,
                VisitedLinkColor = Theme.AccentCyan,
            };
            unhideLink.Click += (_, _) => UnhideAllDevices(hiddenProviders);
            Controls.Add(unhideLink);
            y += 30;
        }

        _deviceSectionBottom = y;
    }

    private int _deviceSectionBottom;

    private void BuildFooter()
    {
        int y = _deviceSectionBottom;

        var saveButton = new Button
        {
            Text = "保存",
            DialogResult = DialogResult.OK,
            Location = new Point(ClientSize.Width - 176, y + 16),
            Width = 80,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.AccentCyan,
            ForeColor = Theme.Background,
        };
        saveButton.FlatAppearance.BorderSize = 0;
        saveButton.Click += (_, _) => SaveAndClose();

        var cancelButton = new Button
        {
            Text = "キャンセル",
            DialogResult = DialogResult.Cancel,
            Location = new Point(ClientSize.Width - 88, y + 16),
            Width = 72,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.CardBackground,
            ForeColor = Theme.TextMuted,
        };
        cancelButton.FlatAppearance.BorderColor = Theme.Border;

        Controls.Add(saveButton);
        Controls.Add(cancelButton);
        AcceptButton = saveButton;
        CancelButton = cancelButton;
    }

    private void DeleteDiscoveredDevice(IMouseBatteryProvider provider)
    {
        var confirm = MessageBox.Show(
            this,
            $"「{provider.DisplayName}」をウィザードで追加した一覧から削除しますか？\n（既定で対応しているマウスには影響しません）",
            "削除の確認",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        _settings.DiscoveredDevices.RemoveAll(d => d.Id == provider.Id);
        _settings.Devices.Remove(provider.Id);
        _settings.Save();

        MessageBox.Show(
            this,
            "削除しました。反映するには一度この設定画面を閉じて開き直してください。",
            "マウスバッテリー設定",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void HideBuiltInDevice(IMouseBatteryProvider provider)
    {
        var confirm = MessageBox.Show(
            this,
            $"「{provider.DisplayName}」を一覧から非表示にしますか？\n監視も停止します。後から「非表示にしたマウスを再表示する」でいつでも元に戻せます。",
            "非表示の確認",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        _settings.GetOrCreate(provider.Id).Hidden = true;
        _settings.Save();

        MessageBox.Show(
            this,
            "非表示にしました。反映するには一度この設定画面を閉じて開き直してください。",
            "マウスバッテリー設定",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void UnhideAllDevices(IReadOnlyList<IMouseBatteryProvider> hiddenProviders)
    {
        foreach (var provider in hiddenProviders)
            _settings.GetOrCreate(provider.Id).Hidden = false;
        _settings.Save();

        MessageBox.Show(
            this,
            "再表示しました。反映するには一度この設定画面を閉じて開き直してください。",
            "マウスバッテリー設定",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void SaveAndClose()
    {
        StartupRegistration.SetEnabled(_startupCheck.Checked);

        _settings.LowBatteryNotificationsEnabled = _notifyCheck.Checked;
        _settings.LowBatteryThreshold = (int)_thresholdInput.Value;

        foreach (var (provider, check, path) in _rows)
        {
            var setting = _settings.GetOrCreate(provider.Id);
            setting.Enabled = check.Checked;
            setting.CompanionPath = path.Text.Trim();
        }
        _settings.Save();
    }
}
