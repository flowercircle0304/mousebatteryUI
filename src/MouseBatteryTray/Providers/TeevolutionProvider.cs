using HidSharp;

namespace MouseBatteryTray.Providers;

/// <summary>
/// Teevolution's Terra Pro wireless mouse ("compX-terra-v1" protocol per OpenMouse). Reverse
/// engineered by the OpenMouse project (github.com/OpenMouse-Project/mouse-protocol,
/// src/teevolution/index.ts + src/drivers/teevolution/hid.ts) — structurally near-identical to this
/// codebase's <see cref="PulsarProvider"/> (same command ids, same "host control" handshake before
/// reading, same checksum family, just a differently-ordered but mathematically equivalent checksum
/// formula). No Teevolution hardware was available to verify this here.
///
/// Wire protocol: output+input report id 8, 17 bytes total (1 report-id byte + 16-byte packet).
/// Request: byte[1]=command, byte[6]=1/0 for the deviceOnline(0x03) command, byte[16]=checksum where
/// checksum = (0x55 - (byte[0]+...+byte[15])) &amp; 0xFF. Response: byte[1] echoes the command,
/// byte[2]=status (0=accepted), byte[6]=battery percent (reject if &gt;100), byte[7]=charging flag.
/// Reading the battery requires first sending deviceOnline(true) — this is also where a sleeping
/// mouse shows up, matching the same real-hardware sleep behavior seen elsewhere in this project —
/// and deviceOnline(false) is sent afterward on a best-effort basis.
/// </summary>
public sealed class TeevolutionProvider : IMouseBatteryProvider
{
    public string Id { get; }
    public string DisplayName { get; }

    private const int VendorId = 0x3554;
    private const int ReportLength = 17; // 1 report-id byte + 16-byte packet
    private const byte ReportId = 0x08;
    private const byte CommandDeviceOnline = 0x03;
    private const byte CommandBatteryLevel = 0x04;

    private static readonly int[] DefaultProductIds = { 0xF520, 0xF523, 0xF5BB, 0xF522 };

    private readonly IReadOnlySet<int> _productIds;

    public TeevolutionProvider(string id = "teevolution", string displayName = "Teevolution Terra Pro",
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
                    if (!SetDeviceOnline(true)) return null;
                    try
                    {
                        var response = Exchange(BuildPacket(CommandBatteryLevel));
                        if (response is null || response[2] != 0) return null;
                        int percent = response[6];
                        if (percent > 100) return null;
                        bool charging = response[7] == 1;
                        return new BatteryReading(percent, charging, null);
                    }
                    finally
                    {
                        SetDeviceOnline(false);
                    }
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        private bool SetDeviceOnline(bool enabled)
        {
            var request = BuildPacket(CommandDeviceOnline);
            request[6] = (byte)(enabled ? 1 : 0);
            FinishChecksum(request);

            var response = Exchange(request);
            return response is not null && response[2] == 0;
        }

        private static byte[] BuildPacket(byte command)
        {
            var packet = new byte[ReportLength];
            packet[0] = ReportId;
            packet[1] = command;
            FinishChecksum(packet);
            return packet;
        }

        private static void FinishChecksum(byte[] packet)
        {
            int sum = 0;
            for (int i = 0; i < ReportLength - 1; i++) sum += packet[i];
            packet[ReportLength - 1] = unchecked((byte)(0x55 - sum));
        }

        private byte[]? Exchange(byte[] request)
        {
            try
            {
                _stream.Write(request);
                byte command = request[1];
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    var response = new byte[ReportLength];
                    int n = _stream.Read(response);
                    if (n >= ReportLength && response[1] == command) return response;
                }
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public void Dispose() => _stream.Dispose();
    }
}
