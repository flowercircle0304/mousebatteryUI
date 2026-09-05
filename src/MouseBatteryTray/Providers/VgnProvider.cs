using HidSharp;

namespace MouseBatteryTray.Providers;

/// <summary>
/// VGN's Dragonfly F2 Master+ wireless mouse ("8K protocol"). Reverse engineered by the OpenMouse
/// project (github.com/OpenMouse-Project/mouse-protocol, src/vgn/index.ts +
/// src/drivers/vgn/hid.ts). Same checksum/framing family as this codebase's ATK and Endgame Gear
/// providers (report id included in the checksum sum, target 0x55), but carried over output/input
/// reports rather than Feature reports, and without any "host control" handshake first (unlike
/// Pulsar's protocol). No VGN hardware was available to verify this here.
///
/// Wire protocol: output+input report id 8, 17 bytes total (1 report-id byte + 16-byte payload).
/// Request: byte[1]=0x04 (battery command), rest zero except byte[16]=checksum where checksum =
/// (0x55 - (byte[0]+...+byte[15])) &amp; 0xFF. Response: byte[1] echoes the command, byte[2]=status
/// (0=ok), byte[6]=battery percent (reject if &gt;100 — that's a "not ready" sentinel), byte[7]=
/// charging flag (1=charging), byte[8..9]=battery voltage in millivolts, big-endian.
/// </summary>
public sealed class VgnProvider : IMouseBatteryProvider
{
    public string Id { get; }
    public string DisplayName { get; }

    private const int VendorId = 0x3554;
    private const int ReportLength = 17; // 1 report-id byte + 16-byte payload
    private const byte ReportId = 0x08;
    private const byte BatteryCommand = 0x04;

    private static readonly int[] DefaultProductIds = { 0xFB56, 0xFB57 };

    private readonly IReadOnlySet<int> _productIds;

    public VgnProvider(string id = "vgn-f2", string displayName = "VGN Dragonfly F2 Master+",
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
            d.GetMaxOutputReportLength() == ReportLength && d.GetMaxInputReportLength() == ReportLength);

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
                    request[1] = BatteryCommand;
                    int sum = 0;
                    for (int i = 0; i < ReportLength - 1; i++) sum += request[i];
                    request[ReportLength - 1] = unchecked((byte)(0x55 - sum));

                    _stream.Write(request);

                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        var response = new byte[ReportLength];
                        int n = _stream.Read(response);
                        if (n < ReportLength || response[1] != BatteryCommand || response[2] != 0) continue;

                        int percent = response[6];
                        if (percent > 100) continue; // not-ready sentinel — try the next reply

                        bool charging = response[7] == 1;
                        int voltageMv = (response[8] << 8) | response[9];
                        return new BatteryReading(percent, charging, voltageMv);
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
