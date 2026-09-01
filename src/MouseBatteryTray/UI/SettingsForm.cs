using MouseBatteryTray.Providers;

namespace MouseBatteryTray.UI;

/// <summary>
/// General app preferences plus per-mouse monitoring on/off and companion-app links.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly List<(IMouseBatteryProvider Provider, CheckBox Check, TextBox Name, TextBox Path)> _rows = new();

    private CheckBox _startupCheck = null!;
    private CheckBox _lowBatteryCheck = null!;
    private NumericUpDown _lowBatteryThresholdInput = null!;
    private CheckBox _fullChargeCheck = null!;
    private NumericUpDown _fullChargeThresholdInput = null!;
    private CheckBox _autoUpdateCheck = null!;
    private ComboBox _languageCombo = null!;

    private int _deviceSectionTop;
    private int _deviceSectionBottom;

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;

        Text = Strings.SettingsTitle;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Background;
        ForeColor = Theme.TextPrimary;
        Font = new Font("Segoe UI", 9f);
        Padding = new Padding(16);

        int generalBottom = BuildGeneralSection();
        _deviceSectionTop = generalBottom + 16;
        BuildDeviceSection(_deviceSectionTop);
        BuildFooter();

        ClientSize = new Size(560, _deviceSectionBottom + 64);
    }

    private static Label SectionTitle(string text, int y) => new()
    {
        Text = text,
        ForeColor = Theme.AccentCyan,
        Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold),
        AutoSize = true,
        Location = new Point(16, y),
    };

    /// <summary>Builds the general section and returns the Y just below its last row.</summary>
    private int BuildGeneralSection()
    {
        Controls.Add(SectionTitle(Strings.SettingsSectionGeneral, 14));

        int y = 44;

        var languageLabel = new Label
        {
            Text = Strings.SettingsLanguage,
            Location = new Point(16, y + 3),
            AutoSize = true,
            ForeColor = Theme.TextPrimary,
        };
        Controls.Add(languageLabel);

        _languageCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(languageLabel.Right + 8, y),
            Width = 140,
            BackColor = Theme.CardBackground,
            ForeColor = Theme.TextPrimary,
        };
        _languageCombo.Items.Add("日本語");
        _languageCombo.Items.Add("English");
        _languageCombo.SelectedIndex = _settings.Language == "en" ? 1 : 0;
        Controls.Add(_languageCombo);
        y += 32;

        _startupCheck = new CheckBox
        {
            Text = Strings.SettingsAutoStart,
            Checked = StartupRegistration.IsEnabled(),
            Location = new Point(16, y),
            AutoSize = true,
            ForeColor = Theme.TextPrimary,
            FlatStyle = FlatStyle.Flat,
        };
        Controls.Add(_startupCheck);
        y += 28;

        _autoUpdateCheck = new CheckBox
        {
            Text = Strings.SettingsAutoUpdateCheck,
            Checked = _settings.AutoUpdateCheckEnabled,
            Location = new Point(16, y),
            AutoSize = true,
            ForeColor = Theme.TextPrimary,
            FlatStyle = FlatStyle.Flat,
        };
        Controls.Add(_autoUpdateCheck);
        y += 28;

        (_lowBatteryCheck, _lowBatteryThresholdInput) = BuildThresholdRow(
            y, Strings.SettingsLowBatteryPrefix, Strings.SettingsLowBatterySuffix,
            _settings.LowBatteryNotificationsEnabled, _settings.LowBatteryThreshold, 1, 90);
        y += 30;

        (_fullChargeCheck, _fullChargeThresholdInput) = BuildThresholdRow(
            y, Strings.SettingsFullChargePrefix, Strings.SettingsFullChargeSuffix,
            _settings.FullChargeNotificationsEnabled, _settings.FullChargeThreshold, 50, 100);
        y += 30;

        var exportButton = new Button
        {
            Text = Strings.SettingsExport,
            Location = new Point(16, y),
            Width = 150,
            Height = 24,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.CardBackground,
            ForeColor = Theme.AccentCyan,
        };
        exportButton.FlatAppearance.BorderColor = Theme.Border;
        exportButton.Click += (_, _) => ExportSettings();
        Controls.Add(exportButton);

        var importButton = new Button
        {
            Text = Strings.SettingsImport,
            Location = new Point(174, y),
            Width = 150,
            Height = 24,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.CardBackground,
            ForeColor = Theme.AccentCyan,
        };
        importButton.FlatAppearance.BorderColor = Theme.Border;
        importButton.Click += (_, _) => ImportSettings();
        Controls.Add(importButton);

        var diagButton = new Button
        {
            Text = Strings.SettingsSaveDiagnostics,
            Location = new Point(332, y),
            Width = 172,
            Height = 24,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.CardBackground,
            ForeColor = Theme.AccentCyan,
        };
        diagButton.FlatAppearance.BorderColor = Theme.Border;
        diagButton.Click += (_, _) => SaveDiagnostics();
        Controls.Add(diagButton);
        y += 30;

        return y;
    }

    private void SaveDiagnostics()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = Strings.DiagnosticsFileFilter,
            FileName = $"mouse-battery-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            Title = Strings.DiagnosticsDialogTitle,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var report = HidDiagnostics.BuildReport(_settings);
            File.WriteAllText(dlg.FileName, report);
            MessageBox.Show(this, Strings.DiagnosticsSaved, Strings.SettingsTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Strings.DiagnosticsFailed(ex.Message), Strings.SettingsTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private (CheckBox Check, NumericUpDown Threshold) BuildThresholdRow(
        int y, string prefixText, string suffixText, bool enabled, int threshold, int min, int max)
    {
        var check = new CheckBox
        {
            Text = prefixText,
            Checked = enabled,
            Location = new Point(16, y),
            AutoSize = true,
            ForeColor = Theme.TextPrimary,
            FlatStyle = FlatStyle.Flat,
        };
        Controls.Add(check);

        var input = new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(threshold, min, max),
            Location = new Point(check.Right + 4, y - 2),
            Width = 55,
            BackColor = Theme.CardBackground,
            ForeColor = Theme.TextPrimary,
            Enabled = enabled,
        };
        Controls.Add(input);

        var suffixLabel = new Label
        {
            Text = suffixText,
            Location = new Point(input.Right + 4, y + 2),
            AutoSize = true,
            ForeColor = Theme.TextPrimary,
        };
        Controls.Add(suffixLabel);

        check.CheckedChanged += (_, _) => input.Enabled = check.Checked;
        return (check, input);
    }

    private void ExportSettings()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = Strings.JsonFileFilter,
            FileName = "mouse-battery-settings.json",
            Title = Strings.ExportDialogTitle,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            _settings.ExportTo(dlg.FileName);
            MessageBox.Show(this, Strings.ExportSucceeded, Strings.SettingsTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Strings.ExportFailed(ex.Message), Strings.SettingsTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ImportSettings()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = Strings.JsonFileFilter,
            Title = Strings.ImportDialogTitle,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var imported = AppSettings.ImportFrom(dlg.FileName);
        if (imported is null)
        {
            MessageBox.Show(this, Strings.ImportReadFailed, Strings.SettingsTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var confirm = MessageBox.Show(
            this,
            Strings.ImportConfirmText,
            Strings.ImportConfirmTitle,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        CopyInto(imported, _settings);
        _settings.Save();

        MessageBox.Show(
            this,
            Strings.NoticeImported,
            Strings.SettingsTitle,
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        DialogResult = DialogResult.Cancel;
        Close();
    }

    private static void CopyInto(AppSettings source, AppSettings target)
    {
        target.Devices = source.Devices;
        target.DiscoveredDevices = source.DiscoveredDevices;
        target.LowBatteryNotificationsEnabled = source.LowBatteryNotificationsEnabled;
        target.LowBatteryThreshold = source.LowBatteryThreshold;
        target.FullChargeNotificationsEnabled = source.FullChargeNotificationsEnabled;
        target.FullChargeThreshold = source.FullChargeThreshold;
        target.PopupPinned = source.PopupPinned;
        target.PopupPinnedX = source.PopupPinnedX;
        target.PopupPinnedY = source.PopupPinnedY;
        target.AutoUpdateCheckEnabled = source.AutoUpdateCheckEnabled;
        target.Language = source.Language;
    }

    private void BuildDeviceSection(int top)
    {
        Controls.Add(SectionTitle(Strings.SettingsSectionDevices, top));

        var hint = new Label
        {
            Text = Strings.SettingsDeviceHint,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Location = new Point(16, top + 26),
        };
        Controls.Add(hint);

        var addButton = new Button
        {
            Text = Strings.SettingsAddMouse,
            Location = new Point(370, top - 4),
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
                    Strings.NoticeAdded,
                    Strings.SettingsTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        };
        Controls.Add(addButton);

        var templateButton = new Button
        {
            Text = Strings.SettingsAddFromTemplate,
            Location = new Point(370, top + 60),
            Width = 170,
            Height = 24,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.CardBackground,
            ForeColor = Theme.AccentViolet,
        };
        templateButton.FlatAppearance.BorderColor = Theme.Border;
        templateButton.Click += (_, _) =>
        {
            using var library = new TemplateLibraryForm(_settings);
            library.ShowDialog(this);
        };
        Controls.Add(templateButton);

        var allProviders = ProviderRegistry.BuildAll(_settings);
        var hiddenProviders = allProviders.Where(p => _settings.IsHidden(p.Id)).ToList();

        int y = top + 96;
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

            var nameBox = new TextBox
            {
                Text = provider.DisplayName,
                Location = new Point(40, y + 3),
                Width = 180,
                BackColor = Theme.CardBackground,
                ForeColor = Theme.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
            };

            var pathBox = new TextBox
            {
                Text = setting.CompanionPath,
                Location = new Point(224, y + 3),
                Width = 190,
                BackColor = Theme.CardBackground,
                ForeColor = Theme.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                PlaceholderText = Strings.SettingsCompanionPlaceholder,
            };

            var browse = new Button
            {
                Text = Strings.SettingsBrowse,
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
                    Filter = Strings.ExeFileFilter,
                    Title = Strings.SettingsChooseCompanionTitle(provider.DisplayName),
                };
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    pathBox.Text = dlg.FileName;
            };

            Controls.Add(check);
            Controls.Add(nameBox);
            Controls.Add(pathBox);
            Controls.Add(browse);

            bool isCustom = _settings.DiscoveredDevices.Any(d => d.Id == provider.Id);

            var delete = new Button
            {
                Text = Strings.SettingsDelete,
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

            _rows.Add((provider, check, nameBox, pathBox));
            y += 40;
        }

        if (hiddenProviders.Count > 0)
        {
            var unhideLink = new LinkLabel
            {
                Text = Strings.SettingsUnhideLink(hiddenProviders.Count),
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

    private void BuildFooter()
    {
        int y = _deviceSectionBottom;

        var saveButton = new Button
        {
            Text = Strings.SettingsSave,
            DialogResult = DialogResult.OK,
            Location = new Point(560 - 176, y + 16),
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
            Text = Strings.SettingsCancel,
            DialogResult = DialogResult.Cancel,
            Location = new Point(560 - 88, y + 16),
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
            Strings.ConfirmDeleteCustomText(provider.DisplayName),
            Strings.ConfirmDeleteCustomTitle,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        _settings.DiscoveredDevices.RemoveAll(d => d.Id == provider.Id);
        _settings.Devices.Remove(provider.Id);
        _settings.Save();

        MessageBox.Show(
            this,
            Strings.NoticeDeleted,
            Strings.SettingsTitle,
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void HideBuiltInDevice(IMouseBatteryProvider provider)
    {
        var confirm = MessageBox.Show(
            this,
            Strings.ConfirmHideBuiltInText(provider.DisplayName),
            Strings.ConfirmHideBuiltInTitle,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        _settings.GetOrCreate(provider.Id).Hidden = true;
        _settings.Save();

        MessageBox.Show(
            this,
            Strings.NoticeHidden,
            Strings.SettingsTitle,
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
            Strings.NoticeUnhidden,
            Strings.SettingsTitle,
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);

        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void SaveAndClose()
    {
        StartupRegistration.SetEnabled(_startupCheck.Checked);

        _settings.Language = _languageCombo.SelectedIndex == 1 ? "en" : "ja";
        _settings.AutoUpdateCheckEnabled = _autoUpdateCheck.Checked;
        _settings.LowBatteryNotificationsEnabled = _lowBatteryCheck.Checked;
        _settings.LowBatteryThreshold = (int)_lowBatteryThresholdInput.Value;
        _settings.FullChargeNotificationsEnabled = _fullChargeCheck.Checked;
        _settings.FullChargeThreshold = (int)_fullChargeThresholdInput.Value;

        foreach (var (provider, check, name, path) in _rows)
        {
            var setting = _settings.GetOrCreate(provider.Id);
            setting.Enabled = check.Checked;
            setting.CompanionPath = path.Text.Trim();

            var trimmedName = name.Text.Trim();
            if (trimmedName.Length > 0)
            {
                var spec = _settings.DiscoveredDevices.FirstOrDefault(d => d.Id == provider.Id);
                if (spec is not null) spec.DisplayName = trimmedName;
            }
        }
        _settings.Save();
    }
}
