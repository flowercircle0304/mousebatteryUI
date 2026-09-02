using HidSharp;

namespace MouseBatteryTray.Providers;

/// <summary>
/// Sony INZONE Buds (WF-G700N) wireless gaming earbuds. Not a mouse, but they connect over the same
/// shape of device this app already supports — a 2.4GHz USB-C dongle, no Bluetooth required — so
/// the same passive-listen architecture applies even though the device itself isn't a mouse.
///
/// Implemented from and cross-checked against the HeadsetControl project
/// (github.com/Sapd/HeadsetControl, lib/devices/sony_inzone_buds.hpp) — an actively maintained,
/// widely-used open-source headset battery/control utility. <b>UNVERIFIED</b> against real hardware
/// in this project specifically.
///
/// Wire protocol: the dongle pushes unsolicited 64-byte Input reports of several different kinds;
/// only the one identified by byte[1]=0x12, byte[2]=0x04 carries battery data. Within that report:
/// byte[14]=right earbud % (0-100 direct), byte[16]=left earbud %, byte[18]=charging case %.
/// Reported level is the lower of the two earbuds — matching HeadsetControl's own choice, since
/// that's the one that actually limits how much longer they can be used.
/// </summary>
public sealed class SonyInzoneBudsProvider : IMouseBatteryProvider
{
    public string Id => "sony-inzone-buds";
    public string DisplayName { get; }

    private const int VendorId = 0x054C;
    private const int ProductId = 0x0EC2;
    private const int ReportLength = 64;
    private const byte BatteryReportType = 0x12;
    private const byte BatteryReportSubtype = 0x04;
    private const int RightEarbudOffset = 14;
    private const int LeftEarbudOffset = 16;

    public SonyInzoneBudsProvider(string displayName = "Sony INZONE Buds")
    {
        DisplayName = displayName;
    }

    public bool OwnsVendorProduct(int vendorId, int productId) =>
        vendorId == VendorId && productId == ProductId;

    public IBatteryDeviceSession? TryOpen(IReadOnlyList<HidDevice> collections)
    {
        var target = collections.FirstOrDefault(d => d.GetMaxInputReportLength() == ReportLength);
        if (target is null) return null;
        if (!target.TryOpen(out var stream)) return null;

        return new Session(DisplayName, stream);
    }

    private sealed class Session : IBatteryDeviceSession
    {
        private readonly HidStream _stream;
        private readonly object _lock = new();

        public string DeviceLabel { get; }

        public Session(string label, HidStream stream)
        {
            DeviceLabel = label;
            _stream = stream;
            _stream.ReadTimeout = 800;
        }

        public BatteryReading? GetLatest()
        {
            lock (_lock)
            {
                var buf = new byte[ReportLength];

                // The battery report is just one of several kinds the dongle pushes, so several
                // quick reads may land on an unrelated report first. Bounded to a few seconds worst
                // case (unlike HeadsetControl's own one-shot-CLI budget of up to 45 * 5s) since this
                // runs inside a background poll loop shared with every other device — if the battery
                // report doesn't show up in this window, the next poll tick a few seconds later
                // tries again.
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    int n;
                    try { n = _stream.Read(buf); }
                    catch (TimeoutException) { continue; }
                    catch { return null; }

                    if (n > LeftEarbudOffset && buf[1] == BatteryReportType && buf[2] == BatteryReportSubtype)
                    {
                        int right = buf[RightEarbudOffset];
                        int left = buf[LeftEarbudOffset];
                        int percent = Math.Clamp(Math.Min(left, right), 0, 100);
                        return BatteryReading.OfPercent(percent);
                    }
                }
                return null;
            }
        }

        public void Dispose() => _stream.Dispose();
    }
}
