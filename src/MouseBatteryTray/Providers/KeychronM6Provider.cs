using HidSharp;

namespace MouseBatteryTray.Providers;

/// <summary>
/// Keychron M6 wired mouse, and the same mouse paired to a Keychron Link-KM 2.4GHz receiver. A
/// different, non-VIA protocol from the Nape Pro (see <see cref="KeychronNapeProvider"/>) — matched
/// separately by its own PIDs and usage. Reverse engineered by the OpenMouse project
/// (github.com/OpenMouse-Project/mouse-protocol, src/keychron/index.ts +
/// src/drivers/keychron/m6-hid.ts). No Keychron hardware was available to verify this here.
///
/// Wire protocol: numbered output report id 0xB3 carrying a status query, answered by a numbered
/// input report id 0xB4, both 64 bytes total (1 report-id byte + 63-byte packet) on the vendor
/// collection at usage page 0xFFC1, usage 0x01. Request: byte[1]=0x06 (status command), rest zero.
/// Response arrives on report id 0xB4 — byte[1] echoes the status command, byte[20]&amp;0x7F is the
/// battery percent (0-100), byte[20]&amp;0x80 is the charging flag.
/// </summary>
public sealed class KeychronM6Provider : IMouseBatteryProvider
{
    public string Id { get; }
    public string DisplayName { get; }

    private const int VendorId = 0x3434;
    private const int ProductId = 0xD060; // wired
    private const int ReceiverProductId = 0xD029; // Keychron Link-KM
    private const int UsagePage = 0xFFC1;
    private const int Usage = 0x01;
    private const int ReportLength = 64; // 1 report-id byte + 63-byte packet
    private const byte CommandReportId = 0xB3;
    private const byte ResponseReportId = 0xB4;
    private const byte StatusCommand = 0x06;

    public KeychronM6Provider(string id = "keychron-m6", string displayName = "Keychron M6")
    {
        Id = id;
        DisplayName = displayName;
    }

    public bool OwnsVendorProduct(int vendorId, int productId) =>
        vendorId == VendorId && (productId == ProductId || productId == ReceiverProductId);

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
                    request[0] = CommandReportId;
                    request[1] = StatusCommand;
                    _stream.Write(request);

                    for (int attempt = 0; attempt < 5; attempt++)
                    {
                        var response = new byte[ReportLength];
                        int n = _stream.Read(response);
                        if (n < ReportLength || response[0] != ResponseReportId || response[1] != StatusCommand)
                            continue;

                        int percent = response[20] & 0x7F;
                        if (percent > 100) return null;
                        bool charging = (response[20] & 0x80) != 0;
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
