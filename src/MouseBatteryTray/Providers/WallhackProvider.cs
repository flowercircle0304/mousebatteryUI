using HidSharp;

namespace MouseBatteryTray.Providers;

/// <summary>
/// WALLHACK's M-001 wireless mouse. Reverse engineered by the OpenMouse project
/// (github.com/OpenMouse-Project/mouse-protocol, src/wallhack/index.ts +
/// src/drivers/wallhack/mouse-hid.ts) from the vendor's own "WALLHACK Terminal" WebHID tool. Not yet
/// confirmed against hardware by OpenMouse either; no WALLHACK hardware was available here.
///
/// Wire protocol: output+input report id 4, 64 bytes total (1 report-id byte + 63-byte body) on the
/// vendor collection at usage page 0xFF1C, usage 0x92. Request: byte[3]=0xBA (battery command), rest
/// zero. Response: byte[3] echoes the command, byte[8]=battery percent (0-100, otherwise treat as
/// unknown), byte[9]=charging flag (1=charging).
/// </summary>
public sealed class WallhackProvider : IMouseBatteryProvider
{
    public string Id { get; }
    public string DisplayName { get; }

    private const int VendorId = 0x3879;
    private const int UsagePage = 0xFF1C;
    private const int Usage = 0x92;
    private const int ReportLength = 64; // 1 report-id byte + 63-byte body
    private const byte ReportId = 4;
    private const byte BatteryCommand = 0xBA;

    private static readonly int[] DefaultProductIds = { 0x1110, 0x0807 };

    private readonly IReadOnlySet<int> _productIds;

    public WallhackProvider(string id = "wallhack", string displayName = "WALLHACK M-001",
        IEnumerable<int>? productIds = null)
    {
        Id = id;
        DisplayName = displayName;
        _productIds = (productIds ?? DefaultProductIds).ToHashSet();
    }

    public bool OwnsVendorProduct(int vendorId, int productId) =>
        vendorId == VendorId && _productIds.Contains(productId);

    public IBatteryDeviceSession? TryOpen(IReadOnlyList<HidDevice> collections)
    {
        var target = collections.FirstOrDefault(d =>
            d.GetMaxOutputReportLength() == ReportLength
            && d.GetMaxInputReportLength() == ReportLength
            && HasVendorUsage(d));

        if (target is null) return null;
        if (!target.TryOpen(out var stream)) return null;

        return new Session(DisplayName, stream);
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
        private readonly HidStream _stream;
        private readonly object _lock = new();

        public string DeviceLabel { get; }

        public Session(string label, HidStream stream)
        {
            DeviceLabel = label;
            _stream = stream;
            _stream.ReadTimeout = 500;
            _stream.WriteTimeout = 1000;
        }

        public BatteryReading? GetLatest()
        {
            lock (_lock)
            {
                try
                {
                    var request = new byte[ReportLength];
                    request[0] = ReportId;
                    request[3] = BatteryCommand;
                    _stream.Write(request);

                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        var response = new byte[ReportLength];
                        int n = _stream.Read(response);
                        if (n < ReportLength || response[3] != BatteryCommand) continue;

                        int percent = response[8];
                        if (percent > 100) return null;
                        bool charging = response[9] == 1;
                        return new BatteryReading(percent, charging, null);
                    }
                    return null;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        public void Dispose() => _stream.Dispose();
    }
}
