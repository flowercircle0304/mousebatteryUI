using HidSharp;

namespace MouseBatteryTray.Providers;

/// <summary>
/// SPRIME PM1 wireless gaming mouse. Unlike every other provider here, this wasn't reverse
/// engineered — it's read straight from SPRIME's own official web configurator
/// (https://www.sprime.pro/), a WebHID-based tool. Its JS bundle
/// (assets/customization-*.js, function named `h` in the minified source) contains the exact
/// SetFeature/GetFeature battery-query sequence in cleartext: a first-party protocol reference,
/// not a guess. <b>UNVERIFIED</b> against real hardware from within this app specifically.
///
/// Wire protocol: Feature report id 5, 32 bytes total on the wire (1 report-id byte + a 31-byte
/// payload — WebHID's sendFeatureReport/receiveFeatureReport strip the id byte, so offsets below
/// are already adjusted +1 from what the vendor's own JS uses). Request: byte[1]=0x15 ("get
/// status"), byte[4]=0x01, rest zero; the vendor's own tool waits ~90ms after sending before
/// reading the response back on the same report id. Response payload:
///   byte[10]=battery% (0-100), byte[11]=charging flag, byte[12]=full-charge flag, byte[13]=online flag.
/// </summary>
public sealed class SprimePM1Provider : IMouseBatteryProvider
{
    public string Id => "sprime-pm1";
    public string DisplayName { get; }

    private const int VendorId = 0x1915;
    private const int ProductId = 0xAC1C;

    // This collection's declared max Feature report length (it multiplexes several report ids —
    // battery status is just the smallest/simplest of them); matched on directly since it's the
    // one that actually opens normally for this device (see HidDiagnostics output).
    private const int CollectionFeatureLength = 704;

    private const byte ReportId = 5;
    private const int FrameLength = 32; // 1 report-id byte + 31-byte payload, matching the vendor tool's Uint8Array(31)
    private const byte CommandGetStatus = 0x15;

    public SprimePM1Provider(string displayName = "SPRIME PM1")
    {
        DisplayName = displayName;
    }

    public bool OwnsVendorProduct(int vendorId, int productId) =>
        vendorId == VendorId && productId == ProductId;

    public IBatteryDeviceSession? TryOpen(IReadOnlyList<HidDevice> collections)
    {
        var target = collections.FirstOrDefault(d => d.GetMaxFeatureReportLength() == CollectionFeatureLength);
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
        }

        public BatteryReading? GetLatest()
        {
            lock (_lock)
            {
                try
                {
                    var request = new byte[FrameLength];
                    request[0] = ReportId;
                    request[1] = CommandGetStatus;
                    request[4] = 0x01;
                    _stream.SetFeature(request);

                    Thread.Sleep(90); // matches the vendor tool's own pacing between the request and the read

                    var response = new byte[FrameLength];
                    _stream.GetFeature(response);

                    int percent = Math.Clamp((int)response[10], 0, 100);
                    bool charging = response[11] != 0;
                    return new BatteryReading(percent, charging, null);
                }
                catch
                {
                    return null;
                }
            }
        }

        public void Dispose() => _stream.Dispose();
    }
}
