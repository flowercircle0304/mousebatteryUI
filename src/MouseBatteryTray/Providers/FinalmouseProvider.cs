using HidSharp;

namespace MouseBatteryTray.Providers;

/// <summary>
/// Finalmouse's UltralightX (Starlight-12 / ULX) wireless dongle. Reverse engineered by the OpenMouse
/// project (github.com/OpenMouse-Project/mouse-protocol, src/finalmouse/index.ts +
/// src/drivers/finalmouse/hid.ts) from the vendor's own "xpanel" WebHID tool. No Finalmouse hardware
/// was available to verify this here.
///
/// Wire protocol (buffer indices below include the leading report-id byte, this codebase's usual
/// convention): output report id 4 ("main"), 64 bytes total — byte[1]=2+payloadLength,
/// byte[2]=0x80|command, byte[3]=payloadLength, byte[4..]=payload. Sending command 96 ("wake all")
/// makes the mouse push back a short burst of separate input reports on report id 5 ("main input"),
/// each framed as byte[1]=innerLength, byte[2]=command, byte[3]=payloadLength, byte[4..]=payload.
/// Command 5's 2-byte little-endian payload is the battery voltage in millivolts, converted to a
/// percentage via the vendor's own piecewise-linear voltage curve (xpanel's
/// `finalmouseBatteryPercent`); command 37's 1-byte payload is the charging flag.
/// </summary>
public sealed class FinalmouseProvider : IMouseBatteryProvider
{
    public string Id { get; }
    public string DisplayName { get; }

    private const int VendorId = 0x361D;
    private const int ProductId = 0x0100;
    private const int ReportLength = 64; // 1 report-id byte + 63-byte body
    private const byte MainOutputReportId = 4;
    private const byte MainInputReportId = 5;
    private const byte WakeAllCommand = 96;
    private const byte BatteryVoltageCommand = 5;
    private const byte ChargingStateCommand = 37;

    public FinalmouseProvider(string id = "finalmouse-ulx", string displayName = "Finalmouse UltralightX")
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
            _stream.ReadTimeout = 200;
            _stream.WriteTimeout = 1000;
        }

        public BatteryReading? GetLatest()
        {
            lock (_lock)
            {
                try
                {
                    var request = new byte[ReportLength];
                    request[0] = MainOutputReportId;
                    request[1] = 2; // 2 + payloadLength(0)
                    request[2] = unchecked((byte)(0x80 | WakeAllCommand));
                    request[3] = 0; // payloadLength
                    _stream.Write(request);
                }
                catch (Exception)
                {
                    return null;
                }

                int? millivolts = null;
                bool? charging = null;

                for (int attempt = 0; attempt < 15 && (millivolts is null); attempt++)
                {
                    var response = new byte[ReportLength];
                    int n;
                    try
                    {
                        n = _stream.Read(response);
                    }
                    catch (Exception)
                    {
                        continue; // this attempt timed out or failed — the burst may still be arriving
                    }

                    if (n < 5 || response[0] != MainInputReportId) continue;

                    byte command = response[2];
                    byte payloadLength = response[3];
                    if (command == BatteryVoltageCommand && payloadLength >= 2 && n >= 6)
                    {
                        millivolts = response[4] | (response[5] << 8);
                    }
                    else if (command == ChargingStateCommand && payloadLength >= 1 && n >= 5)
                    {
                        charging = response[4] == 1;
                    }
                }

                if (millivolts is null) return null;
                int percent = DecodeBatteryPercent(millivolts.Value);
                return new BatteryReading(percent, charging, millivolts);
            }
        }

        /// <summary>Finalmouse's own ULX battery-voltage calibration curve, from xpanel's source.</summary>
        private static int DecodeBatteryPercent(int millivolts)
        {
            if (millivolts <= 0) return 0;
            double voltage = millivolts / 1000.0;
            double[] voltages = { 3, 3.62, 3.66, 3.74, 3.88, 4.17, 4.38 };
            double[] percentages = { 0.2, 5, 10, 25, 50, 75, 100 };

            if (voltage >= voltages[^1]) return 100;
            if (voltage <= voltages[0]) return 0;

            for (int i = 0; i < voltages.Length - 1; i++)
            {
                double low = voltages[i], high = voltages[i + 1];
                if (voltage < low || voltage > high) continue;
                double fraction = (voltage - low) / (high - low);
                return (int)Math.Round(percentages[i] + (percentages[i + 1] - percentages[i]) * fraction);
            }
            return 0;
        }

        public void Dispose() => _stream.Dispose();
    }
}
