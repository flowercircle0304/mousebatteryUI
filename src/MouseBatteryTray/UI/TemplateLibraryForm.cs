using MouseBatteryTray.Providers;

namespace MouseBatteryTray.UI;

/// <summary>
/// Lets the user add a mouse from the community template library (see
/// Providers/DeviceTemplateLibrary.cs) instead of running the discovery wizard — for protocols
/// (Logitech HID++, Razer) that are documented well enough to pre-configure without needing the
/// user's own hardware to reverse engineer against.
/// </summary>
internal sealed class TemplateLibraryForm : Form
{
    private readonly AppSettings _settings;
    private readonly Panel _listPanel;
    private readonly Label _statusLabel;

    public TemplateLibraryForm(AppSettings settings)
    {
        _settings = settings;

        Text = Strings.TemplateTitle;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Theme.Background;
        ForeColor = Theme.TextPrimary;
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(520, 440);
        Padding = new Padding(16);

        var title = new Label
        {
            Text = Strings.TemplateTitle,
            ForeColor = Theme.AccentCyan,
            Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(16, 14),
        };
        Controls.Add(title);

        var hint = new Label
        {
            Text = Strings.TemplateHint,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            MaximumSize = new Size(488, 0),
            Location = new Point(16, 40),
        };
        Controls.Add(hint);

        _statusLabel = new Label
        {
            Text = Strings.TemplateLoading,
            ForeColor = Theme.TextMuted,
            AutoSize = true,
            Location = new Point(16, 84),
        };
        Controls.Add(_statusLabel);

        _listPanel = new Panel
        {
            Location = new Point(16, 106),
            Size = new Size(488, 280),
            AutoScroll = true,
            BackColor = Theme.CardBackground,
            BorderStyle = BorderStyle.FixedSingle,
        };
        Controls.Add(_listPanel);

        var closeButton = new Button
        {
            Text = Strings.TemplateClose,
            DialogResult = DialogResult.Cancel,
            Location = new Point(ClientSize.Width - 88, 396),
            Width = 72,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.CardBackground,
            ForeColor = Theme.TextMuted,
        };
        closeButton.FlatAppearance.BorderColor = Theme.Border;
        Controls.Add(closeButton);
        CancelButton = closeButton;

        Load += async (_, _) => await LoadTemplatesAsync();
    }

    private async Task LoadTemplatesAsync()
    {
        var templates = await DeviceTemplateLibrary.LoadAsync();
        if (IsDisposed) return;

        _statusLabel.Text = "";
        PopulateList(templates);
    }

    private void PopulateList(IReadOnlyList<DeviceTemplate> templates)
    {
        _listPanel.SuspendLayout();
        _listPanel.Controls.Clear();

        int y = 8;
        foreach (var group in templates.GroupBy(t => t.Manufacturer).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var header = new Label
            {
                Text = group.Key,
                ForeColor = Theme.AccentViolet,
                Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(8, y),
            };
            _listPanel.Controls.Add(header);
            y += 24;

            foreach (var template in group.OrderBy(t => t.Model, StringComparer.Ordinal))
            {
                var nameLabel = new Label
                {
                    Text = template.Model,
                    ForeColor = Theme.TextPrimary,
                    AutoSize = false,
                    Size = new Size(280, 18),
                    Location = new Point(16, y + 2),
                    AutoEllipsis = true,
                };
                _listPanel.Controls.Add(nameLabel);

                var badge = new Label
                {
                    Text = template.Verified ? Strings.TemplateVerified : Strings.TemplateUnverified,
                    ForeColor = template.Verified ? Theme.LevelHigh : Theme.TextMuted,
                    Font = new Font("Segoe UI", 7.5f),
                    AutoSize = true,
                    Location = new Point(300, y + 3),
                };
                _listPanel.Controls.Add(badge);

                var addButton = new Button
                {
                    Text = Strings.TemplateAddButton,
                    Location = new Point(400, y),
                    Width = 60,
                    Height = 24,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Theme.Background,
                    ForeColor = Theme.AccentCyan,
                };
                addButton.FlatAppearance.BorderColor = Theme.Border;
                addButton.Click += (_, _) => AddTemplate(template);
                _listPanel.Controls.Add(addButton);

                if (!string.IsNullOrWhiteSpace(template.Notes))
                {
                    var notes = new Label
                    {
                        Text = template.Notes,
                        ForeColor = Theme.TextMuted,
                        Font = new Font("Segoe UI", 7.5f),
                        AutoSize = false,
                        Size = new Size(444, 28),
                        Location = new Point(16, y + 20),
                    };
                    _listPanel.Controls.Add(notes);
                    y += 50;
                }
                else
                {
                    y += 28;
                }
            }
            y += 8;
        }

        _listPanel.ResumeLayout();
    }

    private void AddTemplate(DeviceTemplate template)
    {
        string id = template.Kind == "logitech-hidpp"
            ? "logitech-hidpp"
            : $"razer-{template.ProductId:x4}";

        var spec = new DiscoveredDeviceSpec
        {
            Kind = template.Kind,
            Id = id,
            DisplayName = $"{template.Manufacturer} {template.Model}",
            VendorId = template.VendorId,
            ProductId = template.ProductId,
            RazerTransactionId = template.RazerTransactionId,
        };

        _settings.DiscoveredDevices.RemoveAll(d => d.Id == id);
        _settings.DiscoveredDevices.Add(spec);
        _settings.Save();

        MessageBox.Show(this, Strings.TemplateAddedNotice, Strings.SettingsTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);

        DialogResult = DialogResult.OK;
        Close();
    }
}
