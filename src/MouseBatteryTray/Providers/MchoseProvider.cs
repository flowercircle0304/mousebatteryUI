using HidSharp;
using Microsoft.Win32.SafeHandles;

namespace MouseBatteryTray.Providers;

/// <summary>
/// MCHOSE's A7 V2 mouse family (Pro/Pro+/Ultra/Ultra+). Matches by VendorId alone plus the vendor
/// collection's usage (like <see cref="LogitechHidPpProvider"/>/<see cref="EndgameGearWeProvider"/>)
/// rather than a specific model PID — per OpenMouse's own reverse engineering
/// (github.com/OpenMouse-Project/mouse-protocol, src/mchose/index.ts +
/// src/drivers/mchose/hid.ts), the enumerated receiver PID is shared across the whole A7 V2 family;
/// only the battery reply's own embedded product id actually distinguishes the specific model. No
/// MCHOSE hardware was available to verify this here.
///
/// Wire protocol: Feature report id 0x11 (the "short" 20-token command channel), 65 bytes total (1
/// report-id byte + 64-byte body). The distinguishing quirk, per OpenMouse's own doc comment, is that
/// <b>the whole command body is sent bit-inverted, and replies come back inverted too</b>
/// (`sendFeatureReport(0x11, [~cmd, ~arg0, ...])` / `receiveFeatureReport(0x11) -> [reportId, ~cmd,
/// ~payload0, ...]`) — note OpenMouse's own comment that, unlike an input-report event,
/// `receiveFeatureReport`'s return value already includes the report id as its first byte, matching
/// this codebase's own convention of always including it.
///
/// Request for the battery command (0x06): byte[1]=(~0x06)&amp;0xFF, byte[2..20]=0xFF (19 unused
/// argument bytes, inverted from zero), byte[21..64]=0x00 (left as the codec's own encoder leaves
/// them — untouched, not inverted, past the 20-token window). Response: byte[1] inverts back to
/// 0x06 to confirm it's answering this command, byte[11]=(~raw)&amp;0xFF is the battery percent (0-100),
/// byte[12]=(~raw)&amp;0xFF is the charging flag (nonzero = charging). OpenMouse's own client requires
/// two consecutive identical decoded replies before trusting one (the firmware can answer before it
/// has actually filled the reply in) — replicated here with the same read-twice check.
/// </summary>
public sealed class MchoseProvider : IMouseBatteryProvider
{
    public string Id { get; }
    public string DisplayName { get; }

    private const int VendorId = 0x3837;
    private const int UsagePage = 0xFF01;
    private const int Usage = 0x01;
    private const int FeatLen = 65; // 1 report-id byte + 64-byte body
    private const byte ShortReportId = 0x11;
    private const byte BatteryCommand = 0x06;

    public MchoseProvider(string id = "mchose", string displayName = "MCHOSE A7 V2")
    {
        Id = id;
        DisplayName = displayName;
    }

    public bool OwnsVendorProduct(int vendorId, int productId) => vendorId == VendorId;

    public IBatteryDeviceSession? TryOpen(IReadOnlyList<HidDevice> collections)
    {
        var target = collections.FirstOrDefault(d => d.GetMaxFeatureReportLength() == FeatLen && HasVendorUsage(d));
        if (target is null) return null;

        var handle = RawHidFeatureIo.Open(target.DevicePath);
        if (handle is null) return null;

        return new Session(DisplayName, handle);
    }

    private static bool HasVendorUsage(HidDevice device)
    {
        try
        {
            var descriptor = device.GetReportDescriptor();
            foreach (var item in descriptor.DeviceItems)
            {
                foreach (uint usage in item.Usages.GetAllValues())
                {
                    if ((int)(usage >> 16) == UsagePage && (int)(usage & 0xFFFF) == Usage) return true;
                }
            }
        }
        catch (Exception)
        {
            // Some collections' descriptors can't be parsed — treat that as "not a match".
        }
        return false;
    }

    private sealed class Session : IBatteryDeviceSession
    {
        private readonly SafeFileHandle _handle;
        private readonly object _lock = new();

        public string DeviceLabel { get; }

        public Session(string label, SafeFileHandle handle)
        {
            DeviceLabel = label;
            _handle = handle;
        }

        public BatteryReading? GetLatest()
        {
            lock (_lock)
            {
                var request = new byte[FeatLen];
                request[0] = ShortReportId;
                request[1] = unchecked((byte)~BatteryCommand);
                for (int i = 2; i <= 20; i++) request[i] = 0xFF; // 19 unused arg bytes, inverted zero

                (int Percent, bool Charging)? previous = null;

                for (int attempt = 0; attempt < 20; attempt++)
                {
                    if (!RawHidFeatureIo.SetFeature(_handle, request)) return null;
                    Thread.Sleep(90);

                    var response = new byte[FeatLen];
                    if (RawHidFeatureIo.GetFeature(_handle, response))
                    {
                        byte command = unchecked((byte)~response[1]);
                        if (command == BatteryCommand)
                        {
                            int percent = unchecked((byte)~response[11]);
                            bool charging = unchecked((byte)~response[12]) != 0;
                            if (percent <= 100)
                            {
                                if (previous is { } p && p.Percent == percent && p.Charging == charging)
                                    return new BatteryReading(percent, charging, null);
                                previous = (percent, charging);
                            }
                        }
                    }
                }
                return null;
            }
        }

        public void Dispose() => _handle.Dispose();
    }
}
