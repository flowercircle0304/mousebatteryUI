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
    private const int CardHeight = 72;
    private const int CardGap = 8;
    private const int HeaderHeight = 46;
    private const int FooterHeight = 40;

    private readonly AppSettings _settings;
    private IReadOnlyList<DeviceManager.DeviceStatus> _readings = Array.Empty<DeviceManager.DeviceStatus>();
    private RectangleF _refreshButtonRect;
    private RectangleF _exitButtonRect;
    private RectangleF _footerExitButtonRect;
    private RectangleF _settingsButtonRect;
    private RectangleF _pinButtonRect;
    private RectangleF _closeButtonRect;
    private readonly List<(RectangleF Rect, string ProviderId)> _cardRects = new();
    private readonly List<(RectangleF Rect, string ProviderId, int LinkIndex)> _companionLinkRects = new();

    private bool _pinned;
    private bool _dragging;
    private Point _dragStartMouseScreen;
    private Point _dragStartFormLocation;

    // Drives the charging glyph's flicker — only ticks while visible and something is actually
    // charging, so an idle popup (or one with nothing plugged in) never wastes CPU repainting.
    private readonly System.Windows.Forms.Timer _chargeAnimTimer;
    private float _chargePhase;

    // "今すぐ更新" is a cache re-read, not a hardware re-poll, so it always completes instantly —
    // without this, clicking it gives no sign anything happened. This holds a brief inverted/dimmed
    // state after the click purely so the click feels acknowledged, for exactly as long as this
    // timer runs, not tied to any real work finishing.
    private readonly System.Windows.Forms.Timer _refreshFeedbackTimer;
    private bool _refreshFeedbackActive;

    public event Action? RefreshRequested;
    public event Action? ExitRequested;
    public event Action? SettingsRequested;
    public event Action<string, int>? DeviceCardClicked;

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

        _refreshFeedbackTimer = new System.Windows.Forms.Timer { Interval = 350 };
        _refreshFeedbackTimer.Tick += (_, _) =>
        {
            _refreshFeedbackTimer.Stop();
            _refreshFeedbackActive = false;
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
        _companionLinkRects.Clear();

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

        if (_refreshFeedbackActive)
        {
            using var dimBrush = new SolidBrush(Color.FromArgb(70, Color.Black));
            g.FillRectangle(dimBrush, new RectangleF(0, 0, Width, y));
        }

        DrawFooter(g, y);
    }

    private void DrawHeader(Graphics g)
    {
        using var titleFont = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold);
        using var titleBrush = new SolidBrush(Theme.AccentCyan);
        g.DrawString(Strings.PopupTitle, titleFont, titleBrush, Pad, 14);

        // Titlebar-style row, right to left: gear (settings), pin, − (minimize to tray), × (exit).
        _exitButtonRect = new RectangleF(Width - Pad - 16, 14, 16, 16);
        DrawCloseGlyph(g, _exitButtonRect, Theme.TextMuted);

        _closeButtonRect = new RectangleF(_exitButtonRect.X - 8 - 16, 14, 16, 16);
        DrawMinimizeGlyph(g, _closeButtonRect, Theme.TextMuted);

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

    /// <summary>A single horizontal dash — the standard Windows minimize glyph (matches e.g. a
    /// browser's own titlebar "−" button) — since this button actually just hides the popup back to
    /// the tray rather than closing/exiting the app; an "×" implied the wrong action.</summary>
    private static void DrawMinimizeGlyph(Graphics g, RectangleF rect, Color color)
    {
        using var pen = new Pen(color, 1.6f);
        float m = rect.Width * 0.2f;
        float y = rect.Top + rect.Height * 0.6f;
        g.DrawLine(pen, rect.Left + m, y, rect.Right - m, y);
    }

    /// <summary>An "×" — used for the header's actual exit-the-app button, moved up from the footer
    /// to sit alongside minimize/pin/settings like a titlebar (gear, pin, −, ×).</summary>
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

        float gaugeBottom;
        if (status.Reading?.SubReadings is { Count: > 0 } subReadings)
        {
            // A device that's really more than one physical battery (e.g. a pair of earbuds) — each
            // sub-reading gets its own column pairing its number directly above its own gauge, so
            // "which gauge belongs to which number" doesn't need to be inferred by matching them up
            // across two separate zones of the card the way stacking all the numbers on the left and
            // all the gauges on the right did.
            float contentLeft = textX;
            float contentRight = rect.Right - 14;
            float colGap = 10f;
            float colWidth = (contentRight - contentLeft - colGap * (subReadings.Count - 1)) / subReadings.Count;

            using var subLabelFont = new Font("Segoe UI", 8f, FontStyle.Regular);
            using var subPctFont = new Font("Segoe UI Semibold", 13f, FontStyle.Bold);
            using var subLabelBrush = new SolidBrush(Theme.TextMuted);

            const float gh = 8f;
            float gaugeY = rect.Y + 42;

            for (int i = 0; i < subReadings.Count; i++)
            {
                var (subLabel, subPercent) = subReadings[i];
                var subColor = Theme.LevelColor(subPercent);
                float colX = contentLeft + i * (colWidth + colGap);

                g.DrawString(subLabel, subLabelFont, subLabelBrush, colX, rect.Y + 22);
                float labelW = g.MeasureString(subLabel, subLabelFont).Width;
                string subText = subPercent + "%";
                using var subPctBrush = new SolidBrush(subColor);
                g.DrawString(subText, subPctFont, subPctBrush, colX + labelW + 3, rect.Y + 18);

                var gaugeRect = new RectangleF(colX, gaugeY, colWidth, gh);
                Gfx.DrawRoundedRect(g, gaugeRect, gh / 2f, Theme.Border, 1f);
                float innerPad = 1.5f;
                var innerRect = new RectangleF(gaugeRect.X + innerPad, gaugeRect.Y + innerPad, gaugeRect.Width - innerPad * 2, gaugeRect.Height - innerPad * 2);
                float fillW = innerRect.Width * Math.Clamp(subPercent, 0, 100) / 100f;
                if (fillW > 1)
                    Gfx.FillRoundedRect(g, new RectangleF(innerRect.X, innerRect.Y, fillW, innerRect.Height), innerRect.Height / 2f, subColor);
            }
            gaugeBottom = gaugeY + gh;
        }
        else
        {
            // Number directly above one long, full-width gauge — the single-value equivalent of the
            // "value paired with its own gauge" layout above, instead of number-left/gauge-right.
            string pctText = status.Reading is null ? "--" : status.Reading.Percent.ToString();
            using var pctFont = new Font("Segoe UI Semibold", 16f, FontStyle.Bold);
            using var pctBrush = new SolidBrush(level);
            g.DrawString(pctText, pctFont, pctBrush, textX - 1, rect.Y + 16);

            float pctWidth = g.MeasureString(pctText, pctFont).Width;
            if (status.Reading is not null)
            {
                using var unitFont = new Font("Segoe UI", 9f, FontStyle.Regular);
                using var unitBrush = new SolidBrush(Theme.TextMuted);
                g.DrawString("%", unitFont, unitBrush, textX + pctWidth + 2, rect.Y + 24);
            }

            float gaugeH = 9;
            var gaugeRect = new RectangleF(textX, rect.Y + 44, rect.Right - 14 - textX, gaugeH);
            Gfx.DrawRoundedRect(g, gaugeRect, gaugeH / 2f, Theme.Border, 1f);
            if (status.Reading is { } reading)
            {
                float innerPad = 1.5f;
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

        if (status.Reading is null)
        {
            using var footFont = new Font("Segoe UI", 7.5f, FontStyle.Regular);
            using var footBrush = new SolidBrush(Theme.TextMuted);
            using var sf = new StringFormat { Alignment = StringAlignment.Far };
            g.DrawString(Strings.PopupWaiting, footFont, footBrush, new RectangleF(rect.X, gaugeBottom + 3, rect.Width - 14, 14), sf);
            return;
        }

        bool hasLink1 = HasCompanionApp(status.ProviderId);
        bool hasLink2 = HasSecondCompanionApp(status.ProviderId);
        if (!hasLink1 && !hasLink2) return;

        using var linkFont = new Font("Segoe UI", 7.5f, FontStyle.Regular);
        using var linkBrush = new SolidBrush(Theme.AccentCyan);
        float footY = gaugeBottom + 3;

        if (hasLink1 && hasLink2)
        {
            // Two distinct launch targets (e.g. a web config tool and a native app) — each gets its
            // own small clickable label instead of the whole card triggering just one of them.
            string text2 = Strings.PopupLaunchLink(2);
            string text1 = Strings.PopupLaunchLink(1);
            float width2 = g.MeasureString(text2, linkFont).Width;
            float width1 = g.MeasureString(text1, linkFont).Width;
            const float linkGap = 8f;

            var rect2 = new RectangleF(rect.Right - 14 - width2, footY, width2, 14);
            var rect1 = new RectangleF(rect2.X - linkGap - width1, footY, width1, 14);

            g.DrawString(text1, linkFont, linkBrush, rect1.Location);
            g.DrawString(text2, linkFont, linkBrush, rect2.Location);

            _companionLinkRects.Add((rect1, status.ProviderId, 0));
            _companionLinkRects.Add((rect2, status.ProviderId, 1));
        }
        else
        {
            using var sf = new StringFormat { Alignment = StringAlignment.Far };
            g.DrawString(Strings.PopupClickToLaunch, linkFont, linkBrush, new RectangleF(rect.X, footY, rect.Width - 14, 14), sf);
        }
    }

    private static string FormatEta(TimeSpan eta) =>
        eta.TotalHours >= 1 ? Strings.PopupEtaHours((int)eta.TotalHours) : Strings.PopupEtaMinutes(Math.Max(1, (int)eta.TotalMinutes));

    private bool HasCompanionApp(string providerId) =>
        _settings.Devices.TryGetValue(providerId, out var s) && !string.IsNullOrWhiteSpace(s.CompanionPath);

    private bool HasSecondCompanionApp(string providerId) =>
        _settings.Devices.TryGetValue(providerId, out var s) && !string.IsNullOrWhiteSpace(s.CompanionPath2);

    private void DrawFooter(Graphics g, float y)
    {
        using var font = new Font("Segoe UI", 8.5f, FontStyle.Regular);

        _refreshButtonRect = new RectangleF(Pad, y + 8, 110, 24);
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
        {
            if (_refreshFeedbackActive)
            {
                // Pressed feedback: solid-fill and swap the text color instead of just outlining, so
                // the button itself visibly reacts to the click for the brief life of the timer.
                Gfx.FillRoundedRect(g, _refreshButtonRect, 12, Theme.AccentCyan);
                using var textBrush = new SolidBrush(Theme.Background);
                g.DrawString(Strings.PopupRefresh, font, textBrush, _refreshButtonRect, sf);
            }
            else
            {
                Gfx.DrawRoundedRect(g, _refreshButtonRect, 12, Theme.AccentCyan, 1f);
                using var textBrush = new SolidBrush(Theme.AccentCyan);
                g.DrawString(Strings.PopupRefresh, font, textBrush, _refreshButtonRect, sf);
            }
        }

        // Same exit action as the header's ×, just also reachable down here.
        _footerExitButtonRect = new RectangleF(Width - Pad - 80, y + 8, 80, 24);
        Gfx.DrawRoundedRect(g, _footerExitButtonRect, 12, Theme.TextMuted, 1f);
        using (var b = new SolidBrush(Theme.TextMuted))
        using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            g.DrawString(Strings.PopupExit, font, b, _footerExitButtonRect, sf);
    }

    private void RequestExit()
    {
        var confirm = MessageBox.Show(this, Strings.PopupExitConfirmText, Strings.PopupExitConfirmTitle,
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm == DialogResult.Yes) ExitRequested?.Invoke();
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (_refreshButtonRect.Contains(e.Location))
        {
            _refreshFeedbackActive = true;
            _refreshFeedbackTimer.Stop();
            _refreshFeedbackTimer.Start();
            Invalidate();
            RefreshRequested?.Invoke();
            return;
        }
        if (_exitButtonRect.Contains(e.Location) || _footerExitButtonRect.Contains(e.Location)) { RequestExit(); return; }
        if (_settingsButtonRect.Contains(e.Location)) { SettingsRequested?.Invoke(); return; }
        if (_closeButtonRect.Contains(e.Location)) { Hide(); return; }
        if (_pinButtonRect.Contains(e.Location)) { TogglePinned(); return; }

        foreach (var (linkRect, providerId, linkIndex) in _companionLinkRects)
        {
            if (linkRect.Contains(e.Location))
            {
                DeviceCardClicked?.Invoke(providerId, linkIndex);
                return;
            }
        }

        // Whole-card click only stands in for a launch when there's just one companion link —
        // once a device has two, clicking has to pick one via its own small label above instead of
        // guessing which of the two the user meant.
        foreach (var (rect, providerId) in _cardRects)
        {
            if (rect.Contains(e.Location) && HasCompanionApp(providerId) && !HasSecondCompanionApp(providerId))
            {
                DeviceCardClicked?.Invoke(providerId, 0);
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
        _refreshButtonRect.Contains(p) || _exitButtonRect.Contains(p) || _footerExitButtonRect.Contains(p)
        || _settingsButtonRect.Contains(p)
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
            || _footerExitButtonRect.Contains(e.Location)
            || _settingsButtonRect.Contains(e.Location)
            || _pinButtonRect.Contains(e.Location)
            || _closeButtonRect.Contains(e.Location)
            || _companionLinkRects.Any(c => c.Rect.Contains(e.Location))
            || _cardRects.Any(c => c.Rect.Contains(e.Location) && HasCompanionApp(c.ProviderId) && !HasSecondCompanionApp(c.ProviderId));
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
