using HidSharp;

namespace MouseBatteryTray.Providers;

/// <summary>
/// Purely mechanical, offline device discovery: no AI, no network — just enumerate HID reports and
/// compare raw bytes against a percentage the user typed in after checking the vendor's own app
/// (or the mouse's physical indicator). This is exactly the technique used to reverse engineer the
/// FURYCUBE and ATK protocols during this project's own development, automated.
///
/// Three independent techniques, tried cheapest/safest first: <see cref="TryPassiveMatch"/> (listen
/// for unsolicited Input reports), <see cref="TryPassiveFeatureMatch"/> (poll Feature reports —
/// still read-only, no protocol assumed), then <see cref="TryActiveCompxMatch"/> (send the one
/// known-safe COMPX read command and see what comes back). None of them assume a device matches any
/// previously-seen dongle's exact report length or byte layout — every offset/length that could
/// plausibly hold the answer gets scanned, so this isn't limited to exact clones of a device this
/// project has already reverse-engineered.
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

                // Tolerate one stray sample out of 3+ (e.g. a non-battery report that happened to
                // interleave on the same collection) instead of demanding every single sample agree.
                int required = samples.Count <= 2 ? samples.Count : samples.Count - 1;
                for (int offset = 0; offset < inLen; offset++)
                {
                    if (samples.Count(s => s[offset] == (byte)targetPercent) >= required)
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

    /// <summary>Zero-risk: only reads (HidD_GetFeature), never writes. Some simpler dongle firmwares
    /// keep battery% sitting in a Feature report with no request/handshake needed at all — this
    /// checks for that directly, independent of any particular protocol family, so it can pick up
    /// devices this app has no specific driver for yet. Uses <see cref="RawHidFeatureIo"/> rather
    /// than HidSharp's own Open(), since the target collection is very often the mouse's own primary
    /// usage, which Windows blocks from full read/write access.</summary>
    public static PassiveMatch? TryPassiveFeatureMatch(int vendorId, int productId, int targetPercent, Action<string>? log, CancellationToken ct = default)
    {
        var collections = DeviceList.Local.GetHidDevices()
            .Where(d => d.VendorID == vendorId && d.ProductID == productId)
            .ToList();

        foreach (var dev in collections)
        {
            if (ct.IsCancellationRequested) return null;

            int featLen = dev.GetMaxFeatureReportLength();
            // Some vendors' configuration channel (DPI/buttons/RGB/battery all sharing one big
            // feature report) run to several hundred bytes — a cap tuned for small dongle reports
            // would silently skip exactly the collection most likely to hold a battery byte.
            if (featLen < 2 || featLen > 2048) continue;

            var handle = RawHidFeatureIo.Open(dev.DevicePath);
            if (handle is null)
            {
                log?.Invoke("  " + Strings.DiscoveryFeatureOpenFailed(featLen));
                continue;
            }

            using (handle)
            {
                var samples = new List<byte[]>();
                for (int i = 0; i < 3 && !ct.IsCancellationRequested; i++)
                {
                    var buf = new byte[featLen];
                    if (RawHidFeatureIo.GetFeature(handle, buf)) samples.Add(buf);
                    Thread.Sleep(200);
                }

                if (samples.Count == 0)
                {
                    log?.Invoke("  " + Strings.DiscoveryFeatureNoResponse(featLen));
                    continue;
                }

                for (int offset = 0; offset < featLen; offset++)
                {
                    if (samples.All(s => s[offset] == (byte)targetPercent))
                    {
                        log?.Invoke("  " + Strings.DiscoveryFeatureMatch(featLen, offset));
                        return new PassiveMatch(featLen, offset);
                    }
                }
                log?.Invoke("  " + Strings.DiscoveryFeatureNoMatch(featLen, samples.Count));
            }
        }
        return null;
    }

    public sealed record ActiveMatch(int ReportLength, byte OutputReportId, byte CommandId, int ByteOffset);

    /// <summary>Sends exactly one already-validated "GetBatteryLevel"-shaped COMPX request
    /// (ReportId=8, commandId=4, checksum) to every collection whose input/output reports are the
    /// same length (the COMPX header+checksum shape, regardless of exact size — not hardcoded to
    /// ATK's own 17 bytes), then scans the whole response for a byte matching
    /// <paramref name="targetPercent"/> instead of assuming ATK's own offset. Only this one specific,
    /// already-proven-safe read command is ever sent — no brute-forcing of unknown command ids.</summary>
    public static ActiveMatch? TryActiveCompxMatch(int vendorId, int productId, int targetPercent, Action<string>? log, CancellationToken ct = default)
    {
        const byte outputReportId = 8;
        const byte commandId = 4;

        var candidates = DeviceList.Local.GetHidDevices()
            .Where(d => d.VendorID == vendorId && d.ProductID == productId
                && d.GetMaxOutputReportLength() == d.GetMaxInputReportLength()
                && d.GetMaxOutputReportLength() is >= 8 and <= 64)
            .ToList();

        if (candidates.Count == 0)
        {
            log?.Invoke("  " + Strings.DiscoveryNoCompxCollection);
            return null;
        }

        foreach (var dev in candidates)
        {
            if (ct.IsCancellationRequested) return null;

            int len = dev.GetMaxOutputReportLength();
            if (!dev.TryOpen(out var stream)) continue;

            using (stream)
            {
                stream.ReadTimeout = 2000;
                stream.WriteTimeout = 1000;
                try
                {
                    var outBuf = new byte[len];
                    outBuf[0] = outputReportId;
                    outBuf[1] = commandId;
                    int sum = outputReportId + outBuf[1];
                    outBuf[len - 1] = unchecked((byte)(85 - sum));
                    stream.Write(outBuf);

                    var samples = new List<byte[]>();
                    for (int attempt = 0; attempt < 3 && !ct.IsCancellationRequested; attempt++)
                    {
                        var inBuf = new byte[len];
                        int n = stream.Read(inBuf);
                        if (n >= 3 && inBuf[1] == commandId) samples.Add(inBuf);
                    }

                    if (samples.Count == 0)
                    {
                        log?.Invoke("  " + Strings.DiscoveryActiveNoMatch);
                        continue;
                    }

                    // Skip the echoed header (reportId, commandId) and the checksum trailer — a
                    // real percentage landing there would be coincidence, not signal.
                    for (int offset = 2; offset < len - 1; offset++)
                    {
                        if (samples.All(s => s[offset] == (byte)targetPercent))
                        {
                            log?.Invoke("  " + Strings.DiscoveryActiveMatch(len, offset));
                            return new ActiveMatch(len, outputReportId, commandId, offset);
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
