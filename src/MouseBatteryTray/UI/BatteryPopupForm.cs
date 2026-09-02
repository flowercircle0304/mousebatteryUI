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

    // Drives the charging glyph's flicker — only ticks while visible and something is actually
    // charging, so an idle popup (or one with nothing plugged in) never wastes CPU repainting.
    private readonly System.Windows.Forms.Timer _chargeAnimTimer;
    private float _chargePhase;

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

        _chargeAnimTimer = new System.Windows.Forms.Timer { Interval = 60 };
        _chargeAnimTimer.Tick += (_, _) =>
        {
            if (!Visible || !AnyCharging())
            {
                _chargeAnimTimer.Stop();
                return;
            }
            _chargePhase += 0.16f;
            Invalidate();
        };
    }

    private bool AnyCharging() => _readings.Any(r => r.Reading?.Charging == true);

    private void StartChargeAnimIfNeeded()
    {
        if (Visible && AnyCharging() && !_chargeAnimTimer.Enabled) _chargeAnimTimer.Start();
    }

    public void UpdateReadings(IReadOnlyList<DeviceManager.DeviceStatus> readings)
    {
        _readings = readings;
        int count = Math.Max(readings.Count, 1);
        Height = HeaderHeight + count * (CardHeight + CardGap) + FooterHeight + Pad;
        Region = new Region(Gfx.RoundedRect(new RectangleF(0, 0, Width, Height), 14));
        StartChargeAnimIfNeeded();
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
        StartChargeAnimIfNeeded();
    }

    /// <summary>Re-shows the popup exactly where it already was (Hide() doesn't touch Location) —
    /// unlike <see cref="ShowNear"/>, which recomputes a position from a given screen point and
    /// would otherwise put it wherever the cursor happens to be, e.g. over Settings' centered Save
    /// button, instead of back where the user actually had it.</summary>
    public void ShowAgain()
    {
        Show();
        Activate();
        StartChargeAnimIfNeeded();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _chargeAnimTimer.Dispose();
        base.Dispose(disposing);
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

    private void DrawHeader(Graphics g)
    {
        using var titleFont = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold);
        using var titleBrush = new SolidBrush(Theme.AccentCyan);
        g.DrawString(Strings.PopupTitle, titleFont, titleBrush, Pad, 14);

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

    /// <summary>A proper toothed-gear silhouette (6 bold teeth) — the previous version was 8 thin
    /// spokes radiating past a circle, which read as a sun/asterisk rather than a gear at this
    /// icon's actual 20px size. Fewer, blockier teeth stay legible that small.</summary>
    private static void DrawGearGlyph(Graphics g, RectangleF rect, Color color)
    {
        const int teeth = 6;
        var center = new PointF(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
        float rOuter = rect.Width / 2f;
        float rInner = rOuter * 0.62f; // 1 - toothDepth(0.38)
        float hubR = rOuter * 0.34f;

        double toothAngle = 2 * Math.PI / teeth;
        double halfTop = toothAngle * 0.34 / 2;
        double halfGap = toothAngle * 0.66 / 2;

        var pts = new List<PointF>();
        for (int i = 0; i < teeth; i++)
        {
            double baseAngle = i * toothAngle - Math.PI / 2;
            pts.Add(PolarPoint(center, rInner, baseAngle - halfTop - halfGap));
            pts.Add(PolarPoint(center, rOuter, baseAngle - halfTop));
            pts.Add(PolarPoint(center, rOuter, baseAngle + halfTop));
            pts.Add(PolarPoint(center, rInner, baseAngle + halfTop + halfGap));
        }

        using var path = new GraphicsPath();
        path.AddPolygon(pts.ToArray());
        using var pen = new Pen(color, 1.7f) { LineJoin = LineJoin.Round };
        g.DrawPath(pen, path);
        g.DrawEllipse(pen, center.X - hubR, center.Y - hubR, hubR * 2, hubR * 2);
    }

    private static PointF PolarPoint(PointF center, float r, double angle) =>
        new((float)(center.X + Math.Cos(angle) * r), (float)(center.Y + Math.Sin(angle) * r));

    private static void DrawCloseGlyph(Graphics g, RectangleF rect, Color color)
    {
        using var pen = new Pen(color, 1.6f);
        float m = rect.Width * 0.2f;
        g.DrawLine(pen, rect.Left + m, rect.Top + m, rect.Right - m, rect.Bottom - m);
        g.DrawLine(pen, rect.Right - m, rect.Top + m, rect.Left + m, rect.Bottom - m);
    }

    /// <summary>A pushpin tilted ~35°, head to the upper-right and point to the lower-left — like
    /// Material Design's "push_pin" icon and the reference thumbtack image the tilt was requested
    /// from. A dead-straight needle (the previous two versions, in turn: a plain line, then a
    /// vertical map-marker) reads flatter and less recognizably "pinned down" than an angled one.</summary>
    private static void DrawPinGlyph(Graphics g, RectangleF rect, Color color, bool filled)
    {
        var state = g.Save();
        var center0 = new PointF(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
        g.TranslateTransform(center0.X, center0.Y);
        g.RotateTransform(-35);
        g.TranslateTransform(-center0.X, -center0.Y);

        float headR = rect.Width * 0.28f;
        var headCenter = new PointF(rect.X + rect.Width / 2f, rect.Y + headR * 1.1f);
        using var pen = new Pen(color, 1.4f);
        using var brush = new SolidBrush(color);
        if (filled) g.FillEllipse(brush, headCenter.X - headR, headCenter.Y - headR, headR * 2, headR * 2);
        else g.DrawEllipse(pen, headCenter.X - headR, headCenter.Y - headR, headR * 2, headR * 2);

        float baseHalfW = headR * 0.5f;
        var p1 = new PointF(headCenter.X - baseHalfW, headCenter.Y + headR * 0.6f);
        var p2 = new PointF(headCenter.X + baseHalfW, headCenter.Y + headR * 0.6f);
        var tip = new PointF(headCenter.X, rect.Bottom);
        g.FillPolygon(brush, new[] { p1, p2, tip });

        g.Restore(state);
    }

    /// <summary>A filled bolt shape (the classic "flash" silhouette), scaled to fit <paramref name="rect"/>.
    /// <paramref name="glowIntensity"/> (0-1) layers a soft halo behind it for the charging flicker —
    /// 0 draws a plain flat bolt, 1 draws it at full glow.</summary>
    private static void DrawLightningGlyph(Graphics g, RectangleF rect, Color color, float glowIntensity)
    {
        ReadOnlySpan<PointF> shape = stackalloc PointF[]
        {
            new(0.62f, 0.00f),
            new(0.20f, 0.55f),
            new(0.46f, 0.55f),
            new(0.38f, 1.00f),
            new(0.85f, 0.40f),
            new(0.56f, 0.40f),
        };
        var points = new PointF[shape.Length];
        for (int i = 0; i < shape.Length; i++)
            points[i] = new PointF(rect.X + shape[i].X * rect.Width, rect.Y + shape[i].Y * rect.Height);

        if (glowIntensity > 0.01f)
        {
            using var glowBrush = new SolidBrush(Color.FromArgb((int)(120 * glowIntensity), color));
            using var m = new Matrix();
            var center = new PointF(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
            float scale = 1f + 0.5f * glowIntensity;
            m.Translate(center.X, center.Y);
            m.Scale(scale, scale);
            m.Translate(-center.X, -center.Y);
            var glowPoints = (PointF[])points.Clone();
            m.TransformPoints(glowPoints);
            g.FillPolygon(glowBrush, glowPoints);
        }

        using var brush = new SolidBrush(color);
        g.FillPolygon(brush, points);
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
            // Two overlaid sine waves at slightly clashing frequencies read as an electric flicker
            // rather than a smooth, predictable breathing pulse — deterministic per-frame (not
            // random), so it never looks glitchy even redrawn at an irregular interval.
            float flicker = 0.5f
                + 0.3f * (float)Math.Sin(_chargePhase * 6.0)
                + 0.2f * (float)Math.Sin(_chargePhase * 13.7 + 1.3f);
            flicker = Math.Clamp(flicker, 0.15f, 1f);
            var boltColor = Color.FromArgb(255, Theme.Electric.R, Theme.Electric.G, Theme.Electric.B);

            using var chargeFont = new Font("Segoe UI Semibold", 7.5f, FontStyle.Bold);
            using var chargeBrush = new SolidBrush(boltColor);
            using var sf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
            var textRect = new RectangleF(rect.X, rect.Y + 6, rect.Width - 14 - 15, 16);
            g.DrawString(Strings.PopupCharging, chargeFont, chargeBrush, textRect, sf);

            var boltRect = new RectangleF(rect.Right - 24, rect.Y + 5, 12, 15);
            DrawLightningGlyph(g, boltRect, boltColor, flicker);
        }
        else if (status.EstimatedTimeRemaining is { } eta)
        {
            using var etaFont = new Font("Segoe UI", 7.5f, FontStyle.Regular);
            using var etaBrush = new SolidBrush(Theme.TextMuted);
            using var sf = new StringFormat { Alignment = StringAlignment.Far };
            g.DrawString(Strings.PopupEtaPrefix + FormatEta(eta), etaFont, etaBrush, new RectangleF(rect.X, rect.Y + 8, rect.Width - 14, 14), sf);
        }

        if (status.Reading?.SubReadings is { Count: > 0 } subReadings)
        {
            // A device that's really more than one physical battery (e.g. a pair of earbuds) — each
            // sub-reading gets its own short label and its own level color, so a listener that's
            // fine doesn't visually hide one that's actually running low.
            using var subLabelFont = new Font("Segoe UI", 8f, FontStyle.Regular);
            using var subPctFont = new Font("Segoe UI Semibold", 15f, FontStyle.Bold);
            using var subLabelBrush = new SolidBrush(Theme.TextMuted);
            float subX = textX;
            foreach (var (subLabel, subPercent) in subReadings)
            {
                g.DrawString(subLabel, subLabelFont, subLabelBrush, subX, rect.Y + 27);
                float labelW = g.MeasureString(subLabel, subLabelFont).Width;

                string subText = subPercent.ToString();
                using var subPctBrush = new SolidBrush(Theme.LevelColor(subPercent));
                g.DrawString(subText, subPctFont, subPctBrush, subX + labelW + 2, rect.Y + 21);
                float pctW = g.MeasureString(subText, subPctFont).Width;

                subX += labelW + pctW + 16;
            }
        }
        else
        {
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
        }

        // Mini battery gauge(s) on the right — one per sub-reading (e.g. L/R earbuds) when present,
        // so a listener that's fine can't visually mask one that's actually running low, matching
        // the individually-colored numbers above. Falls back to a single gauge otherwise.
        float gaugeW = 64;
        float gaugeBottom;
        if (status.Reading?.SubReadings is { Count: > 0 } gaugeSubReadings)
        {
            const float gh = 8f, gap = 4f;
            float totalH = gaugeSubReadings.Count * gh + (gaugeSubReadings.Count - 1) * gap;
            float startY = rect.Y + (rect.Height - totalH) / 2f;
            float gx = rect.Right - gaugeW - 14;

            for (int i = 0; i < gaugeSubReadings.Count; i++)
            {
                var subRect = new RectangleF(gx, startY + i * (gh + gap), gaugeW, gh);
                Gfx.DrawRoundedRect(g, subRect, gh / 2f, Theme.Border, 1f);

                float innerPad = 1.5f;
                var innerRect = new RectangleF(subRect.X + innerPad, subRect.Y + innerPad, subRect.Width - innerPad * 2, subRect.Height - innerPad * 2);
                float fillW = innerRect.Width * Math.Clamp(gaugeSubReadings[i].Percent, 0, 100) / 100f;
                if (fillW > 1)
                {
                    using var fillBrush = new SolidBrush(Theme.LevelColor(gaugeSubReadings[i].Percent));
                    Gfx.FillRoundedRect(g, new RectangleF(innerRect.X, innerRect.Y, fillW, innerRect.Height), innerRect.Height / 2f, Theme.LevelColor(gaugeSubReadings[i].Percent));
                }
            }
            gaugeBottom = startY + totalH;
        }
        else
        {
            float gaugeH = 10;
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
            gaugeBottom = gaugeRect.Bottom;
        }

        string? footNote = status.Reading is null
            ? Strings.PopupWaiting
            : HasCompanionApp(status.ProviderId) ? Strings.PopupClickToLaunch : null;
        if (footNote is not null)
        {
            using var footFont = new Font("Segoe UI", 7.5f, FontStyle.Regular);
            using var footBrush = new SolidBrush(status.Reading is null ? Theme.TextMuted : Theme.AccentCyan);
            using var sf = new StringFormat { Alignment = StringAlignment.Far };
            g.DrawString(footNote, footFont, footBrush, new RectangleF(rect.X, gaugeBottom + 3, rect.Width - 14, 14), sf);
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
