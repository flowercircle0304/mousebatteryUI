using HidSharp;

namespace MouseBatteryTray.Providers;

/// <summary>
/// Pulsar's wireless gaming mice (Xlite/X2 family, Tenz Signature, etc). Matches by VendorId alone —
/// OpenMouse's own detection is "collection-based" too (any device under this VID whose collection
/// has exactly one input and one output report, both report id 8), not per-model PID, per
/// github.com/OpenMouse-Project/mouse-protocol's src/drivers/pulsar/pulsar-hid.ts. Does NOT cover the
/// separate Pulsar X3 (XS-1 feature-report protocol) or the "Pulsar 4K Wireless Receiver" (which
/// enumerates under the shared Teevolution/VGN vendor id instead). No Pulsar hardware was available
/// to verify this here.
///
/// Wire protocol: output+input report id 8, 17 bytes total (1 report-id byte + 16-byte packet).
/// Request: byte[1]=command, byte[5]=parameter count, byte[6..]=parameters, byte[16]=checksum where
/// checksum = (0x55 - (byte[0]+...+byte[15])) &amp; 0xFF (byte[0], the report id, is included in the
/// sum — same style of checksum as several other brands in this codebase). Response: byte[1] echoes
/// the command, byte[2]=status (0=accepted), byte[6..]=response data.
///
/// Reading the battery level (or anything else) requires first telling the receiver to enter "host
/// control" with a deviceOnline(true) command (byte[1]=3, byte[6]=1) — this is also where a sleeping
/// mouse shows up: the receiver replies with byte[6]=0 ("offline") until the mouse is moved or
/// clicked, matching the same real-hardware sleep behavior already confirmed for other brands in this
/// project. deviceOnline(false) is sent afterward on a best-effort basis to leave the receiver in its
/// normal state.
/// </summary>
public sealed class PulsarProvider : IMouseBatteryProvider
{
    public string Id { get; }
    public string DisplayName { get; }

    private const int VendorId = 0x3710;
    private const int ReportLength = 17; // 1 report-id byte + 16-byte packet
    private const byte ReportId = 0x08;
    private const byte CommandDeviceOnline = 0x03;
    private const byte CommandBatteryLevel = 0x04;

    public PulsarProvider(string id = "pulsar", string displayName = "Pulsar")
    {
        Id = id;
        DisplayName = displayName;
    }

    public bool OwnsVendorProduct(int vendorId, int productId) => vendorId == VendorId;

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
                        int percent = Math.Clamp((int)response[6], 0, 100);
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

        /// <summary>Enters or exits the receiver's "host control" mode — required before any read,
        /// and where a sleeping mouse shows up as a repeated "offline" reply.</summary>
        private bool SetDeviceOnline(bool enabled)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                var request = BuildPacket(CommandDeviceOnline);
                request[6] = (byte)(enabled ? 1 : 0);
                FinishChecksum(request);

                var response = Exchange(request);
                if (response is null || response[2] != 0) return false;
                if (response[10] == 1) { Thread.Sleep(10); continue; } // receiver still busy — retry
                return !enabled || response[6] == 1; // enabling requires the mouse to confirm online
            }
            return false;
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
