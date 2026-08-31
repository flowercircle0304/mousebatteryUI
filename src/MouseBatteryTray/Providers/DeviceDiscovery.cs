using HidSharp;

namespace MouseBatteryTray.Providers;

/// <summary>
/// Purely mechanical, offline device discovery: no AI, no network — just enumerate HID reports and
/// compare raw bytes against a percentage the user typed in after checking the vendor's own app
/// (or the mouse's physical indicator). This is exactly the technique used to reverse engineer the
/// FURYCUBE and ATK protocols during this project's own development, automated.
/// </summary>
public static class DeviceDiscovery
{
    public sealed record UnrecognizedDevice(int VendorId, int ProductId, string DisplayName);

    public static IReadOnlyList<UnrecognizedDevice> ListUnrecognizedDevices(AppSettings settings)
    {
        var known = ProviderRegistry.BuildAll(settings);
        var groups = DeviceList.Local.GetHidDevices().GroupBy(d => (d.VendorID, d.ProductID));

        var result = new List<UnrecognizedDevice>();
        foreach (var g in groups)
        {
            if (known.Any(p => p.OwnsVendorProduct(g.Key.VendorID, g.Key.ProductID))) continue;

            string name;
            try { name = g.First().GetProductName(); } catch { name = ""; }
            if (string.IsNullOrWhiteSpace(name)) name = Strings.WizardUnknownDevice(g.Key.VendorID, g.Key.ProductID);

            result.Add(new UnrecognizedDevice(g.Key.VendorID, g.Key.ProductID, name));
        }
        return result;
    }

    public sealed record PassiveMatch(int ReportLength, int ByteOffset);

    /// <summary>Zero-risk: only reads. Listens for a few unsolicited input reports per HID collection and
    /// looks for a byte offset whose value equals <paramref name="targetPercent"/> in every sample.</summary>
    public static PassiveMatch? TryPassiveMatch(int vendorId, int productId, int targetPercent, Action<string>? log, CancellationToken ct)
    {
        var collections = DeviceList.Local.GetHidDevices()
            .Where(d => d.VendorID == vendorId && d.ProductID == productId)
            .ToList();

        foreach (var dev in collections)
        {
            if (ct.IsCancellationRequested) return null;

            int inLen = dev.GetMaxInputReportLength();
            if (inLen < 2 || inLen > 64) continue;
            if (!dev.TryOpen(out var stream)) continue;

            using (stream)
            {
                stream.ReadTimeout = 3000;
                var samples = new List<byte[]>();
                for (int i = 0; i < 3 && !ct.IsCancellationRequested; i++)
                {
                    try
                    {
                        var buf = new byte[inLen];
                        int n = stream.Read(buf);
                        if (n > 0) samples.Add(buf);
                    }
                    catch (TimeoutException) { }
                    catch { break; }
                }

                if (samples.Count == 0)
                {
                    log?.Invoke("  " + Strings.DiscoveryNoResponse(inLen));
                    continue;
                }

                for (int offset = 0; offset < inLen; offset++)
                {
                    if (samples.All(s => s[offset] == (byte)targetPercent))
                    {
                        log?.Invoke("  " + Strings.DiscoveryPassiveMatch(inLen, offset));
                        return new PassiveMatch(inLen, offset);
                    }
                }
                log?.Invoke("  " + Strings.DiscoveryNoMatch(inLen, samples.Count));
            }
        }
        return null;
    }

    public sealed record ActiveMatch(byte OutputReportId, byte CommandId, int ByteOffset);

    /// <summary>Sends exactly one already-validated "GetBatteryLevel"-shaped COMPX request
    /// (ReportId=8, commandId=4, checksum) and checks whether the response's byte[6] matches
    /// <paramref name="targetPercent"/>. Only tried against collections whose report lengths exactly
    /// match the COMPX shape (17-byte in/out), and only this one specific, already-proven-safe
    /// command is sent — no brute-forcing of unknown command ids.</summary>
    public static ActiveMatch? TryActiveCompxMatch(int vendorId, int productId, int targetPercent, Action<string>? log)
    {
        const byte outputReportId = 8;
        const byte commandId = 4;

        var collections = DeviceList.Local.GetHidDevices()
            .Where(d => d.VendorID == vendorId && d.ProductID == productId
                && d.GetMaxOutputReportLength() == 17 && d.GetMaxInputReportLength() == 17)
            .ToList();

        if (collections.Count == 0)
        {
            log?.Invoke("  " + Strings.DiscoveryNoCompxCollection);
            return null;
        }

        foreach (var dev in collections)
        {
            if (!dev.TryOpen(out var stream)) continue;

            using (stream)
            {
                stream.ReadTimeout = 2000;
                stream.WriteTimeout = 1000;
                try
                {
                    var outBuf = new byte[17];
                    outBuf[0] = outputReportId;
                    outBuf[1] = commandId;
                    int sum = outputReportId + outBuf[1];
                    outBuf[16] = unchecked((byte)(85 - sum));
                    stream.Write(outBuf);

                    var inBuf = new byte[17];
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        int n = stream.Read(inBuf);
                        if (n >= 10 && inBuf[1] == commandId && inBuf[6] == (byte)targetPercent)
                        {
                            log?.Invoke("  " + Strings.DiscoveryActiveMatch);
                            return new ActiveMatch(outputReportId, commandId, 6);
                        }
                    }
                    log?.Invoke("  " + Strings.DiscoveryActiveNoMatch);
                }
                catch (Exception ex)
                {
                    log?.Invoke("  " + Strings.DiscoveryError(ex.GetType().Name));
                }
            }
        }
        return null;
    }
}
