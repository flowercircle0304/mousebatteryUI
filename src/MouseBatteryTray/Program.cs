using MouseBatteryTray.Providers;
using MouseBatteryTray.UI;

namespace MouseBatteryTray;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
#if DEBUG
        if (args.Length > 0 && args[0] == "--snapshot")
        {
            string lang = args.Length > 2 ? args[2] : "ja";
            Strings.SetLanguage(lang);
            RenderSnapshot(args.Length > 1 ? args[1] : @"C:\Users\junya\AppData\Local\Temp\claude\D----------\f48a32ea-da3d-4df7-8d92-6e23c6df5fe2\scratchpad\popup_snapshot.png");
            return;
        }
        if (args.Length > 0 && args[0] == "--test-discovery")
        {
            TestDiscovery();
            return;
        }
        if (args.Length > 0 && args[0] == "--diagnostics")
        {
            Console.WriteLine(HidDiagnostics.BuildReport(AppSettings.Load()));
            return;
        }
#endif
        // Named per-user mutex: a second launch (e.g. double-clicking the exe again, or a stale
        // shortcut) should tell the user this is already running rather than starting a competing
        // instance that would fight the first one over the same HID handles and settings.json.
        _singleInstanceMutex = new Mutex(true, "MouseBatteryTray-SingleInstance-3f6a9c2e-8b41-4d7a-9c3e-1a2b3c4d5e6f", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(Strings.AppAlreadyRunningText, Strings.AppAlreadyRunningTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }

    // Held for the process lifetime so the mutex isn't released until this instance actually
    // exits — deliberately never disposed; the OS reclaims it on process exit.
    private static Mutex? _singleInstanceMutex;

#if DEBUG
    private static void RenderSnapshot(string outPath)
    {
        var settings = new AppSettings();
        settings.GetOrCreate("atk-compx").CompanionPath = @"C:\Program Files\Vendor App\VendorApp.exe";
        settings.DiscoveredDevices.Add(new DiscoveredDeviceSpec
        {
            Kind = "compx", Id = "atk-compx", DisplayName = "Sample Mouse A", VendorId = 0x1, ProductId = 0x1,
        });

        using var popup = new BatteryPopupForm(settings);
        var _ = popup.Handle; // force native handle creation so DrawToBitmap works

        var sample = new[]
        {
            new DeviceManager.DeviceStatus("atk-compx", "Wireless Mouse A", new BatteryReading(12, true, 3550), null),
            new DeviceManager.DeviceStatus("furycube-f1", "Wireless Mouse B", new BatteryReading(28, false, null), TimeSpan.FromHours(6.4)),
            new DeviceManager.DeviceStatus("sony-inzone-buds", "INZONE Buds", new BatteryReading(41, null, null, new[] { ("L", 41), ("R", 67) }), null),
            new DeviceManager.DeviceStatus("unknown", "Unknown Mouse", null, null),
        };
        popup.UpdateReadings(sample);
        popup.PerformLayout();

        using var bmp = new Bitmap(popup.Width, popup.Height);
        popup.DrawToBitmap(bmp, new Rectangle(0, 0, popup.Width, popup.Height));
        bmp.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine($"Saved snapshot to {outPath}");

        // Also render the tray icon itself at a few representative sizes, upscaled for inspection.
        var dir = Path.GetDirectoryName(outPath)!;
        int[] percents = { 95, 28, 12, -1 };
        using var iconSheet = new Bitmap(4 * 80, 80);
        using (var sg = Graphics.FromImage(iconSheet))
        {
            sg.Clear(Color.FromArgb(255, 30, 30, 30));
            sg.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            for (int i = 0; i < percents.Length; i++)
            {
                int? p = percents[i] < 0 ? null : percents[i];
                using var icon = TrayIconRenderer.Render(p);
                using var iconBmp = icon.ToBitmap();
                sg.DrawImage(iconBmp, i * 80 + 8, 8, 64, 64);
            }
        }
        string iconPath = Path.Combine(dir, "icon_sheet.png");
        iconSheet.Save(iconPath, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine($"Saved icon sheet to {iconPath}");

        using var settingsForm = new SettingsForm(settings);
        settingsForm.StartPosition = FormStartPosition.Manual;
        settingsForm.Location = new Point(-3000, -3000); // off-screen: avoids real-window z-order issues while still forcing a real Show()
        settingsForm.Show();
        for (int i = 0; i < 10; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(50); }
        using var settingsBmp = new Bitmap(settingsForm.Width, settingsForm.Height);
        settingsForm.DrawToBitmap(settingsBmp, new Rectangle(0, 0, settingsForm.Width, settingsForm.Height));
        settingsForm.Hide();
        string settingsPath = Path.Combine(dir, "settings_snapshot.png");
        settingsBmp.Save(settingsPath, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine($"Saved settings snapshot to {settingsPath}");

        using var templateForm = new TemplateLibraryForm(settings);
        templateForm.StartPosition = FormStartPosition.Manual;
        templateForm.Location = new Point(-3000, -3000); // off-screen: avoids real-window z-order issues while still forcing a real Show()
        templateForm.Show();
        for (int i = 0; i < 60; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(100); }
        using var templateBmp = new Bitmap(templateForm.Width, templateForm.Height);
        templateForm.DrawToBitmap(templateBmp, new Rectangle(0, 0, templateForm.Width, templateForm.Height));
        templateForm.Hide();
        string templatePath = Path.Combine(dir, "template_snapshot.png");
        templateBmp.Save(templatePath, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine($"Saved template snapshot to {templatePath}");

        RenderIconCandidates(dir);
    }

    // Scratch comparison sheet for redesigning the gear/pin header glyphs — not shipped, just a
    // side-by-side render so a choice can be made before touching BatteryPopupForm's real icons.
    private static void RenderIconCandidates(string dir)
    {
        const int cell = 96;
        const int cols = 6;
        const int rows = 2;
        using var sheet = new Bitmap(cell * cols, cell * rows);
        using var g = Graphics.FromImage(sheet);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(Theme.Background);

        using var labelFont = new Font("Segoe UI", 8f);
        using var labelBrush = new SolidBrush(Theme.TextMuted);

        void Cell(int col, int row, string label, Action<RectangleF> draw)
        {
            var iconRect = new RectangleF(col * cell + cell * 0.28f, row * cell + 6, cell * 0.44f, cell * 0.44f);
            Gfx.FillRoundedRect(g, new RectangleF(col * cell + 4, row * cell + 4, cell - 8, cell - 8), 8, Theme.CardBackground);
            draw(iconRect);
            var textRect = new RectangleF(col * cell, row * cell + cell - 26, cell, 22);
            using var sf = new StringFormat { Alignment = StringAlignment.Center };
            g.DrawString(label, labelFont, labelBrush, textRect, sf);
        }

        Cell(0, 0, "現行(歯車)", r => DrawGearCurrent(g, r, Theme.TextMuted));
        Cell(1, 0, "歯車A(細歯 x8)", r => DrawGearCandidate(g, r, Theme.TextMuted, 8, 0.30f, 1.5f));
        Cell(2, 0, "歯車B(太歯 x6)", r => DrawGearCandidate(g, r, Theme.TextMuted, 6, 0.38f, 1.7f));
        Cell(3, 0, "レンチ", r => DrawWrenchCandidate(g, r, Theme.TextMuted, 1.8f));
        Cell(4, 0, "現行(ピン)", r => DrawPinCurrent(g, r, Theme.AccentCyan, false));
        Cell(5, 0, "現行(ピン留め済)", r => DrawPinCurrent(g, r, Theme.AccentCyan, true));

        Cell(0, 1, "画鋲A", r => DrawPinThumbtack(g, r, Theme.AccentCyan, false));
        Cell(1, 1, "画鋲A(留め済)", r => DrawPinThumbtack(g, r, Theme.AccentCyan, true));
        Cell(2, 1, "マーカーB", r => DrawPinMarker(g, r, Theme.AccentCyan, false));
        Cell(3, 1, "マーカーB(留め済)", r => DrawPinMarker(g, r, Theme.AccentCyan, true));

        string path = Path.Combine(dir, "icon_candidates.png");
        sheet.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine($"Saved icon candidates to {path}");
    }

    private static void DrawGearCurrent(Graphics g, RectangleF rect, Color color)
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

    private static void DrawPinCurrent(Graphics g, RectangleF rect, Color color, bool filled)
    {
        var headRect = new RectangleF(rect.X + rect.Width * 0.15f, rect.Y, rect.Width * 0.7f, rect.Width * 0.7f);
        using var pen = new Pen(color, 1.4f);
        if (filled) { using var b = new SolidBrush(color); g.FillEllipse(b, headRect); }
        else g.DrawEllipse(pen, headRect);
        float cx = rect.X + rect.Width / 2f;
        g.DrawLine(pen, cx, headRect.Bottom - 1, cx, rect.Bottom);
    }

    private static PointF PolarPoint(PointF center, float r, double angle) =>
        new((float)(center.X + Math.Cos(angle) * r), (float)(center.Y + Math.Sin(angle) * r));

    private static void DrawGearCandidate(Graphics g, RectangleF rect, Color color, int teeth, float toothDepth, float strokeWidth)
    {
        var center = new PointF(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
        float rOuter = rect.Width / 2f;
        float rInner = rOuter * (1f - toothDepth);
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

        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddPolygon(pts.ToArray());
        using var pen = new Pen(color, strokeWidth) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
        g.DrawPath(pen, path);
        g.DrawEllipse(pen, center.X - hubR, center.Y - hubR, hubR * 2, hubR * 2);
    }

    private static void DrawWrenchCandidate(Graphics g, RectangleF rect, Color color, float strokeWidth)
    {
        var state = g.Save();
        var center = new PointF(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
        g.TranslateTransform(center.X, center.Y);
        g.RotateTransform(45);
        g.TranslateTransform(-center.X, -center.Y);

        using var pen = new Pen(color, strokeWidth) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
        float w = rect.Width;
        float cx = rect.X + w / 2f;

        g.DrawLine(pen, cx, rect.Y + w * 0.24f, cx, rect.Bottom - w * 0.24f);

        float jawR = w * 0.24f;
        var topJawRect = new RectangleF(cx - jawR, rect.Y, jawR * 2, jawR * 2);
        g.DrawArc(pen, topJawRect, 50, 260);
        var botJawRect = new RectangleF(cx - jawR, rect.Bottom - jawR * 2, jawR * 2, jawR * 2);
        g.DrawArc(pen, botJawRect, 230, 260);

        g.Restore(state);
    }

    private static void DrawPinThumbtack(Graphics g, RectangleF rect, Color color, bool filled)
    {
        float headR = rect.Width * 0.30f;
        var headCenter = new PointF(rect.X + rect.Width / 2f, rect.Y + headR);
        using var pen = new Pen(color, 1.4f);
        using var brush = new SolidBrush(color);
        if (filled) g.FillEllipse(brush, headCenter.X - headR, headCenter.Y - headR, headR * 2, headR * 2);
        else g.DrawEllipse(pen, headCenter.X - headR, headCenter.Y - headR, headR * 2, headR * 2);

        float baseHalfW = headR * 0.55f;
        var p1 = new PointF(headCenter.X - baseHalfW, headCenter.Y + headR * 0.55f);
        var p2 = new PointF(headCenter.X + baseHalfW, headCenter.Y + headR * 0.55f);
        var tip = new PointF(headCenter.X, rect.Bottom);
        g.FillPolygon(brush, new[] { p1, p2, tip });
    }

    private static void DrawPinMarker(Graphics g, RectangleF rect, Color color, bool filled)
    {
        float r = rect.Width * 0.30f;
        var center = new PointF(rect.X + rect.Width / 2f, rect.Y + r * 1.05f);
        var tip = new PointF(center.X, rect.Bottom);

        using var pen = new Pen(color, 1.4f);
        using var brush = new SolidBrush(color);
        float baseHalfW = r * 0.85f;
        var p1 = new PointF(center.X - baseHalfW, center.Y + r * 0.35f);
        var p2 = new PointF(center.X + baseHalfW, center.Y + r * 0.35f);
        if (filled)
        {
            g.FillPolygon(brush, new[] { p1, p2, tip });
            g.FillEllipse(brush, center.X - r, center.Y - r, r * 2, r * 2);
        }
        else
        {
            g.DrawLine(pen, p1, tip);
            g.DrawLine(pen, p2, tip);
            g.DrawEllipse(pen, center.X - r, center.Y - r, r * 2, r * 2);
        }
    }

    private static void TestDiscovery()
    {
        // Regression test: run the wizard's generic matcher against the two ALREADY-KNOWN real
        // devices and confirm it independently rediscovers the exact same report length/offset
        // that ProviderRegistry has hardcoded for them.
        Console.WriteLine("=== FURYCUBE F1 (expect passive match: reportLength=5, offset=4) ===");
        {
            const int vendorId = 0x1D57, productId = 0xFA60;
            var live = new PassivePushHidProvider("probe", "probe", vendorId, productId, 5, 4);
            var collections = HidSharp.DeviceList.Local.GetHidDevices().Where(d => d.VendorID == vendorId && d.ProductID == productId).ToList();
            using var session = live.TryOpen(collections);
            BatteryReading? reading = null;
            for (int i = 0; i < 10 && reading is null; i++) { Thread.Sleep(500); reading = session?.GetLatest(); }
            if (reading is null) { Console.WriteLine("  現在値の取得に失敗（デバイス未接続?）"); }
            else
            {
                Console.WriteLine($"  現在値: {reading.Percent}%");
                var match = DeviceDiscovery.TryPassiveMatch(vendorId, productId, reading.Percent, s => Console.WriteLine(s), CancellationToken.None);
                Console.WriteLine(match is { ReportLength: 5, ByteOffset: 4 } ? "  RESULT: PASS" : $"  RESULT: FAIL (got {match})");
            }
        }

        Console.WriteLine("=== ATK 8K Dongle (expect active match: offset=6) ===");
        {
            const int vendorId = 0x373B, productId = 4145;
            var collections = HidSharp.DeviceList.Local.GetHidDevices().Where(d => d.VendorID == vendorId && d.ProductID == productId).ToList();
            Console.WriteLine($"  collections found: {collections.Count}");
            var target = collections.FirstOrDefault(d => d.GetMaxOutputReportLength() == 17 && d.GetMaxInputReportLength() == 17);
            Console.WriteLine($"  target collection: {(target is null ? "NOT FOUND" : target.DevicePath)}");
            if (target is not null)
            {
                bool opened = target.TryOpen(out var stream);
                Console.WriteLine($"  TryOpen: {opened}");
                if (opened)
                {
                    using (stream)
                    {
                        stream.ReadTimeout = 2000;
                        stream.WriteTimeout = 1000;
                        try
                        {
                            var outBuf = new byte[17];
                            outBuf[0] = 8; outBuf[1] = 4;
                            outBuf[16] = unchecked((byte)(85 - (8 + 4)));
                            stream.Write(outBuf);
                            var inBuf = new byte[17];
                            int n = stream.Read(inBuf);
                            Console.WriteLine($"  raw response ({n} bytes): {BitConverter.ToString(inBuf, 0, n)}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"  raw I/O error: {ex}");
                        }
                    }
                }
            }

            var live = new CompxDongleProvider("probe", "probe", vendorId, new[] { productId });
            using var session = live.TryOpen(collections);
            var reading = session?.GetLatest();
            if (reading is null) { Console.WriteLine("  現在値の取得に失敗（デバイス未接続? ATK HUBが起動していると失敗することがあります）"); }
            else
            {
                Console.WriteLine($"  現在値: {reading.Percent}%");
                var match = DeviceDiscovery.TryActiveCompxMatch(vendorId, productId, reading.Percent, s => Console.WriteLine(s));
                Console.WriteLine(match is { ByteOffset: 6 } ? "  RESULT: PASS" : $"  RESULT: FAIL (got {match})");
            }
        }
    }
#endif
}
