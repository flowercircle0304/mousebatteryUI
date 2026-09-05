using HidSharp;

namespace MouseBatteryTray.Providers;

/// <summary>
/// Keychron Nape Pro wireless mouse (VIA-based raw HID protocol). Reverse engineered by the
/// OpenMouse project (github.com/OpenMouse-Project/mouse-protocol, src/keychron/index.ts +
/// src/drivers/keychron/nape-hid.ts). Only the mouse's own direct PID is matched here — the shared
/// Keychron Link-KM receiver PIDs are deliberately excluded, since OpenMouse's own driver only trusts
/// a receiver's battery reading after an extra compatibility check (confirming whatever is actually
/// paired to it really is a Nape Pro and not some other Keychron device), which this provider does
/// not replicate. No Keychron hardware was available to verify this here.
///
/// Wire protocol: unnumbered (report id 0) output+input reports, 33 bytes total (1 report-id byte +
/// 32-byte packet). Request: byte[1]=0xA7 ("misc" command group), byte[2]=0x31 (getBattery), rest
/// zero. Response: byte[1]/byte[2] echo the command group/subcommand, byte[3]=battery percent (0-100
/// direct), byte[4]=status (1=charging, 2=fully charged, otherwise discharging).
/// </summary>
public sealed class KeychronNapeProvider : IMouseBatteryProvider
{
    public string Id { get; }
    public string DisplayName { get; }

    private const int VendorId = 0x3434;
    private const int ProductId = 0x0440;
    private const int ReportLength = 33; // 1 report-id byte + 32-byte packet
    private const byte CommandMiscGroup = 0xA7;
    private const byte CommandGetBattery = 0x31;

    public KeychronNapeProvider(string id = "keychron-nape", string displayName = "Keychron Nape Pro")
    {
        Id = id;
        DisplayName = displayName;
    }

    public bool OwnsVendorProduct(int vendorId, int productId) =>
        vendorId == VendorId && productId == ProductId;

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
                    request[1] = CommandMiscGroup;
                    request[2] = CommandGetBattery;
                    _stream.Write(request);

                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        var response = new byte[ReportLength];
                        int n = _stream.Read(response);
                        if (n < ReportLength || response[1] != CommandMiscGroup || response[2] != CommandGetBattery)
                            continue;

                        int percent = Math.Clamp((int)response[3], 0, 100);
                        byte status = response[4];
                        bool charging = status == 1 || status == 2;
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
