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
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }

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
        templateForm.Location = new Point(0, 0);
        templateForm.Show();
        for (int i = 0; i < 60; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(100); }
        var templateRect = templateForm.Bounds;
        using var templateBmp = new Bitmap(templateRect.Width, templateRect.Height);
        using (var sg3 = Graphics.FromImage(templateBmp))
            sg3.CopyFromScreen(templateRect.Location, Point.Empty, templateRect.Size);
        templateForm.Hide();
        string templatePath = Path.Combine(dir, "template_snapshot.png");
        templateBmp.Save(templatePath, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine($"Saved template snapshot to {templatePath}");
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
