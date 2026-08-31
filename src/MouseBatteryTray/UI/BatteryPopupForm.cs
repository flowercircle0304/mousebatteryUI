using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using MouseBatteryTray.Providers;

namespace MouseBatteryTray.UI;

/// <summary>
/// A borderless HUD-style popup listing every connected device's battery status. Shown near the
/// tray icon on left-click, dismisses itself when it loses focus (click-away).
/// </summary>
internal sealed class BatteryPopupForm : Form
{
    private const int PopupWidth = 300;
    private const int Pad = 14;
    private const int CardHeight = 66;
    private const int CardGap = 8;
    private const int HeaderHeight = 46;
    private const int FooterHeight = 40;

    private readonly AppSettings _settings;
    private IReadOnlyList<DeviceManager.DeviceStatus> _readings = Array.Empty<DeviceManager.DeviceStatus>();
    private RectangleF _refreshButtonRect;
    private RectangleF _exitButtonRect;
    private RectangleF _settingsButtonRect;
    private RectangleF _pinButtonRect;
    private RectangleF _closeButtonRect;
    private readonly List<(RectangleF Rect, string ProviderId)> _cardRects = new();

    private bool _pinned;
    private bool _dragging;
    private Point _dragStartMouseScreen;
    private Point _dragStartFormLocation;

    public event Action? RefreshRequested;
    public event Action? ExitRequested;
    public event Action? SettingsRequested;
    public event Action<string>? DeviceCardClicked;

    public BatteryPopupForm(AppSettings settings)
    {
        _settings = settings;
        _pinned = settings.PopupPinned;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Theme.Background;
        DoubleBuffered = true;
        Width = PopupWidth;
        Height = HeaderHeight + FooterHeight + Pad;

        Deactivate += (_, _) => { if (!_pinned) Hide(); };
    }

    public void UpdateReadings(IReadOnlyList<DeviceManager.DeviceStatus> readings)
    {
        _readings = readings;
        int count = Math.Max(readings.Count, 1);
        Height = HeaderHeight + count * (CardHeight + CardGap) + FooterHeight + Pad;
        Region = new Region(Gfx.RoundedRect(new RectangleF(0, 0, Width, Height), 14));
        Invalidate();
    }

    public void ShowNear(Point trayIconScreenPoint)
    {
        if (_pinned && _settings.PopupPinnedX is int px && _settings.PopupPinnedY is int py)
        {
            Location = ClampToAnyScreen(new Point(px, py));
        }
        else
        {
            var workArea = Screen.FromPoint(trayIconScreenPoint).WorkingArea;
            int x = Math.Clamp(trayIconScreenPoint.X - Width + 20, workArea.Left + 8, workArea.Right - Width - 8);
            int y = workArea.Bottom - Height - 8;
            Location = new Point(x, y);
        }
        Show();
        Activate();
    }

    private Point ClampToAnyScreen(Point p)
    {
        var workArea = Screen.FromPoint(p).WorkingArea;
        int x = Math.Clamp(p.X, workArea.Left, workArea.Right - Width);
        int y = Math.Clamp(p.Y, workArea.Top, workArea.Bottom - Height);
        return new Point(x, y);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using (var bg = new SolidBrush(Theme.Background))
            g.FillPath(bg, Gfx.RoundedRect(new RectangleF(0, 0, Width, Height), 14));
        Gfx.DrawRoundedRect(g, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 14, Theme.Border, 1f);

        DrawHeader(g);
        _cardRects.Clear();

        float y = HeaderHeight;
        if (_readings.Count == 0)
        {
            using var font = new Font("Segoe UI", 9f, FontStyle.Regular);
            using var brush = new SolidBrush(Theme.TextMuted);
            var rect = new RectangleF(Pad, y, Width - Pad * 2, CardHeight);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(Strings.PopupNoDevices, font, brush, rect, sf);
            y += CardHeight + CardGap;
        }
        else
        {
            foreach (var r in _readings)
            {
                var cardRect = new RectangleF(Pad, y, Width - Pad * 2, CardHeight);
                DrawCard(g, cardRect, r);
                _cardRects.Add((cardRect, r.ProviderId));
                y += CardHeight + CardGap;
            }
        }

        DrawFooter(g, y);
    }

    private static void DrawSpacedText(Graphics g, string text, Font font, Brush brush, float x, float y, float spacing)
    {
        float cx = x;
        foreach (char c in text)
        {
            g.DrawString(c.ToString(), font, brush, cx, y);
            cx += g.MeasureString(c.ToString(), font).Width + spacing;
        }
    }

    private void DrawHeader(Graphics g)
    {
        using var titleFont = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold);
        using var titleBrush = new SolidBrush(Theme.AccentCyan);
        DrawSpacedText(g, Strings.PopupTitle, titleFont, titleBrush, Pad, 14, 1.6f);

        _closeButtonRect = new RectangleF(Width - Pad - 16, 14, 16, 16);
        DrawCloseGlyph(g, _closeButtonRect, Theme.TextMuted);

        _pinButtonRect = new RectangleF(_closeButtonRect.X - 8 - 16, 14, 16, 16);
        DrawPinGlyph(g, _pinButtonRect, _pinned ? Theme.AccentCyan : Theme.TextMuted, _pinned);

        _settingsButtonRect = new RectangleF(_pinButtonRect.X - 8 - 20, 12, 20, 20);
        DrawGearGlyph(g, _settingsButtonRect, Theme.TextMuted);

        using var lineBrush = new LinearGradientBrush(
            new PointF(Pad, 0), new PointF(Width - Pad, 0),
            Theme.AccentCyan, Color.FromArgb(0, Theme.AccentViolet));
        using var linePen = new Pen(lineBrush, 1.4f);
        g.DrawLine(linePen, Pad, HeaderHeight - 4, Width - Pad, HeaderHeight - 4);
    }

    private static void DrawGearGlyph(Graphics g, RectangleF rect, Color color)
    {
        var center = new PointF(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
        float outer = rect.Width / 2f;
        float inner = outer * 0.55f;

        using var pen = new Pen(color, 1.6f);
        g.DrawEllipse(pen, center.X - inner * 0.9f, center.Y - inner * 0.9f, inner * 1.8f, inner * 1.8f);

        for (int i = 0; i < 8; i++)
        {
            double angle = i * Math.PI / 4;
            float x1 = center.X + (float)(Math.Cos(angle) * inner);
            float y1 = center.Y + (float)(Math.Sin(angle) * inner);
            float x2 = center.X + (float)(Math.Cos(angle) * outer);
            float y2 = center.Y + (float)(Math.Sin(angle) * outer);
            g.DrawLine(pen, x1, y1, x2, y2);
        }
    }

    private static void DrawCloseGlyph(Graphics g, RectangleF rect, Color color)
    {
        using var pen = new Pen(color, 1.6f);
        float m = rect.Width * 0.2f;
        g.DrawLine(pen, rect.Left + m, rect.Top + m, rect.Right - m, rect.Bottom - m);
        g.DrawLine(pen, rect.Right - m, rect.Top + m, rect.Left + m, rect.Bottom - m);
    }

    private static void DrawPinGlyph(Graphics g, RectangleF rect, Color color, bool filled)
    {
        var headRect = new RectangleF(rect.X + rect.Width * 0.15f, rect.Y, rect.Width * 0.7f, rect.Width * 0.7f);
        using var pen = new Pen(color, 1.4f);
        if (filled)
        {
            using var brush = new SolidBrush(color);
            g.FillEllipse(brush, headRect);
        }
        else
        {
            g.DrawEllipse(pen, headRect);
        }

        float cx = rect.X + rect.Width / 2f;
        g.DrawLine(pen, cx, headRect.Bottom - 1, cx, rect.Bottom);
    }

    private void DrawCard(Graphics g, RectangleF rect, DeviceManager.DeviceStatus status)
    {
        var level = Theme.LevelColor(status.Reading?.Percent);

        Gfx.DrawGlowOutline(g, rect, 10, level, layers: 3, maxWidth: 5f);
        Gfx.FillRoundedRect(g, rect, 10, Theme.CardBackground);
        Gfx.DrawRoundedRect(g, rect, 10, Color.FromArgb(90, level), 1f);

        // Left accent bar, with its own soft glow.
        var accentRect = new RectangleF(rect.X, rect.Y + 8, 3, rect.Height - 16);
        using (var glowBrush = new SolidBrush(Color.FromArgb(70, level)))
            g.FillRectangle(glowBrush, accentRect.X - 2, accentRect.Y, accentRect.Width + 4, accentRect.Height);
        using (var accentBrush = new SolidBrush(level))
            g.FillRectangle(accentBrush, accentRect);

        float textX = rect.X + 16;

        using var labelFont = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        using var labelBrush = new SolidBrush(Theme.TextMuted);
        g.DrawString(status.Label, labelFont, labelBrush, textX, rect.Y + 9);

        if (status.Reading?.Charging == true)
        {
            using var chargeFont = new Font("Segoe UI", 7.5f, FontStyle.Regular);
            using var chargeBrush = new SolidBrush(Theme.AccentViolet);
            using var sf = new StringFormat { Alignment = StringAlignment.Far };
            g.DrawString(Strings.PopupCharging, chargeFont, chargeBrush, new RectangleF(rect.X, rect.Y + 8, rect.Width - 14, 14), sf);
        }
        else if (status.EstimatedTimeRemaining is { } eta)
        {
            using var etaFont = new Font("Segoe UI", 7.5f, FontStyle.Regular);
            using var etaBrush = new SolidBrush(Theme.TextMuted);
            using var sf = new StringFormat { Alignment = StringAlignment.Far };
            g.DrawString(Strings.PopupEtaPrefix + FormatEta(eta), etaFont, etaBrush, new RectangleF(rect.X, rect.Y + 8, rect.Width - 14, 14), sf);
        }

        string pctText = status.Reading is null ? "--" : status.Reading.Percent.ToString();
        using var pctFont = new Font("Segoe UI Semibold", 18f, FontStyle.Bold);
        using var pctBrush = new SolidBrush(level);
        g.DrawString(pctText, pctFont, pctBrush, textX - 1, rect.Y + 21);

        float pctWidth = g.MeasureString(pctText, pctFont).Width;
        if (status.Reading is not null)
        {
            using var unitFont = new Font("Segoe UI", 9f, FontStyle.Regular);
            using var unitBrush = new SolidBrush(Theme.TextMuted);
            g.DrawString("%", unitFont, unitBrush, textX + pctWidth + 2, rect.Y + 30);
        }

        // Mini battery gauge on the right.
        float gaugeW = 64, gaugeH = 10;
        var gaugeRect = new RectangleF(rect.Right - gaugeW - 14, rect.Y + (rect.Height - gaugeH) / 2f, gaugeW, gaugeH);
        Gfx.DrawRoundedRect(g, gaugeRect, gaugeH / 2f, Theme.Border, 1f);
        if (status.Reading is { } reading)
        {
            float innerPad = 2f;
            var innerRect = new RectangleF(gaugeRect.X + innerPad, gaugeRect.Y + innerPad, gaugeRect.Width - innerPad * 2, gaugeRect.Height - innerPad * 2);
            float fillW = innerRect.Width * Math.Clamp(reading.Percent, 0, 100) / 100f;
            if (fillW > 1)
            {
                using var fillBrush = new SolidBrush(level);
                Gfx.FillRoundedRect(g, new RectangleF(innerRect.X, innerRect.Y, fillW, innerRect.Height), innerRect.Height / 2f, level);
            }
        }

        string? footNote = status.Reading is null
            ? Strings.PopupWaiting
            : HasCompanionApp(status.ProviderId) ? Strings.PopupClickToLaunch : null;
        if (footNote is not null)
        {
            using var footFont = new Font("Segoe UI", 7.5f, FontStyle.Regular);
            using var footBrush = new SolidBrush(status.Reading is null ? Theme.TextMuted : Theme.AccentCyan);
            using var sf = new StringFormat { Alignment = StringAlignment.Far };
            g.DrawString(footNote, footFont, footBrush, new RectangleF(rect.X, gaugeRect.Bottom + 3, rect.Width - 14, 14), sf);
        }
    }

    private static string FormatEta(TimeSpan eta) =>
        eta.TotalHours >= 1 ? Strings.PopupEtaHours((int)eta.TotalHours) : Strings.PopupEtaMinutes(Math.Max(1, (int)eta.TotalMinutes));

    private bool HasCompanionApp(string providerId) =>
        _settings.Devices.TryGetValue(providerId, out var s) && !string.IsNullOrWhiteSpace(s.CompanionPath);

    private void DrawFooter(Graphics g, float y)
    {
        using var font = new Font("Segoe UI", 8.5f, FontStyle.Regular);

        _refreshButtonRect = new RectangleF(Pad, y + 8, 110, 24);
        Gfx.DrawRoundedRect(g, _refreshButtonRect, 12, Theme.AccentCyan, 1f);
        using (var b = new SolidBrush(Theme.AccentCyan))
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            g.DrawString(Strings.PopupRefresh, font, b, _refreshButtonRect, sf);

        _exitButtonRect = new RectangleF(Width - Pad - 80, y + 8, 80, 24);
        Gfx.DrawRoundedRect(g, _exitButtonRect, 12, Theme.TextMuted, 1f);
        using (var b = new SolidBrush(Theme.TextMuted))
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            g.DrawString(Strings.PopupExit, font, b, _exitButtonRect, sf);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (_refreshButtonRect.Contains(e.Location)) { RefreshRequested?.Invoke(); return; }
        if (_exitButtonRect.Contains(e.Location)) { ExitRequested?.Invoke(); return; }
        if (_settingsButtonRect.Contains(e.Location)) { SettingsRequested?.Invoke(); return; }
        if (_closeButtonRect.Contains(e.Location)) { Hide(); return; }
        if (_pinButtonRect.Contains(e.Location)) { TogglePinned(); return; }

        foreach (var (rect, providerId) in _cardRects)
        {
            if (rect.Contains(e.Location) && HasCompanionApp(providerId))
            {
                DeviceCardClicked?.Invoke(providerId);
                return;
            }
        }
    }

    private void TogglePinned()
    {
        _pinned = !_pinned;
        _settings.PopupPinned = _pinned;
        if (_pinned)
        {
            _settings.PopupPinnedX = Location.X;
            _settings.PopupPinnedY = Location.Y;
        }
        _settings.Save();
        Invalidate();
    }

    private bool IsOverInteractiveElement(Point p) =>
        _refreshButtonRect.Contains(p) || _exitButtonRect.Contains(p) || _settingsButtonRect.Contains(p)
        || _pinButtonRect.Contains(p) || _closeButtonRect.Contains(p)
        || _cardRects.Any(c => c.Rect.Contains(p));

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (IsOverInteractiveElement(e.Location)) return;

        _dragging = true;
        _dragStartMouseScreen = Cursor.Position;
        _dragStartFormLocation = Location;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging) return;

        _dragging = false;
        if (_pinned)
        {
            _settings.PopupPinnedX = Location.X;
            _settings.PopupPinnedY = Location.Y;
            _settings.Save();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_dragging)
        {
            var current = Cursor.Position;
            int dx = current.X - _dragStartMouseScreen.X;
            int dy = current.Y - _dragStartMouseScreen.Y;
            Location = new Point(_dragStartFormLocation.X + dx, _dragStartFormLocation.Y + dy);
            return;
        }

        bool overClickable = _refreshButtonRect.Contains(e.Location)
            || _exitButtonRect.Contains(e.Location)
            || _settingsButtonRect.Contains(e.Location)
            || _pinButtonRect.Contains(e.Location)
            || _closeButtonRect.Contains(e.Location)
            || _cardRects.Any(c => c.Rect.Contains(e.Location) && HasCompanionApp(c.ProviderId));
        Cursor = overClickable ? Cursors.Hand : Cursors.Default;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            const int WS_EX_TOOLWINDOW = 0x80;
            cp.ExStyle |= WS_EX_TOOLWINDOW; // keep it out of alt-tab
            return cp;
        }
    }
}
