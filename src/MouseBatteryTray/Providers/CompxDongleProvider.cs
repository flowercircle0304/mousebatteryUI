using HidSharp;

namespace MouseBatteryTray.Providers;

/// <summary>
/// Generic provider for wireless dongles built on the "COMPX" reference design (a firmware/protocol
/// shared by many rebadged Chinese 4K/8K-polling gaming mouse dongles). Reverse engineered from ATK
/// HUB's bundled renderer JS (class names T$t/f0t/Z$t) and verified against a real "ATK 8K Dongle
/// Light Version" (VID 0x373B / PID 0x1031): battery% matched ATK HUB's own display exactly.
///
/// Wire protocol:
///   Output report, ReportId=<paramref name="outputReportId"/> (default 8), 16-byte body:
///     [0]=commandId (4=GetBatteryLevel) [1]=status [2-3]=eepromAddress [4]=dataValidLen
///     [5..14]=payload(10) [15]=checksum = (85 - (ReportId + sum(body[0..14]))) &amp; 0xFF
///   Input report mirrors the same layout; response body[0] echoes commandId, and for
///   GetBatteryLevel: body[5]=battery% (0-100), body[6]=charging flag, body[7-8]=voltage_mV (big-endian).
///
/// To add a new COMPX-family dongle: find its VID/PID with HidProbe, confirm it exposes a HID
/// collection with 17-byte input AND output reports, then add one entry to
/// <see cref="ProviderRegistry"/> — no new code needed unless it deviates from this protocol.
/// </summary>
public sealed class CompxDongleProvider : IMouseBatteryProvider
{
    public string Id { get; }
    public string DisplayName { get; }

    private readonly int _vendorId;
    private readonly IReadOnlySet<int> _productIds;
    private readonly byte _outputReportId;
    private readonly byte _commandId;

    public CompxDongleProvider(
        string id,
        string displayName,
        int vendorId,
        IEnumerable<int> productIds,
        byte outputReportId = 8,
        byte getBatteryCommandId = 4)
    {
        Id = id;
        DisplayName = displayName;
        _vendorId = vendorId;
        _productIds = productIds.ToHashSet();
        _outputReportId = outputReportId;
        _commandId = getBatteryCommandId;
    }

    public bool OwnsVendorProduct(int vendorId, int productId) =>
        vendorId == _vendorId && _productIds.Contains(productId);

    public IBatteryDeviceSession? TryOpen(IReadOnlyList<HidDevice> collections)
    {
        var target = collections.FirstOrDefault(d =>
            d.GetMaxOutputReportLength() == 17 && d.GetMaxInputReportLength() == 17);

        if (target is null) return null;
        if (!target.TryOpen(out var stream)) return null;

        return new Session(DisplayName, stream, _outputReportId, _commandId);
    }

    private sealed class Session : IBatteryDeviceSession
    {
        private readonly HidStream _stream;
        private readonly byte _outputReportId;
        private readonly byte _commandId;
        private readonly object _lock = new();

        public string DeviceLabel { get; }

        public Session(string label, HidStream stream, byte outputReportId, byte commandId)
        {
            DeviceLabel = label;
            _stream = stream;
            _outputReportId = outputReportId;
            _commandId = commandId;
            _stream.ReadTimeout = 2000;
            _stream.WriteTimeout = 1000;
        }

        public BatteryReading? GetLatest()
        {
            lock (_lock)
            {
                try
                {
                    var outBuf = new byte[17];
                    outBuf[0] = _outputReportId;
                    outBuf[1] = _commandId;
                    int sum = _outputReportId + outBuf[1];
                    outBuf[16] = unchecked((byte)(85 - sum));

                    _stream.Write(outBuf);

                    var inBuf = new byte[17];
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        int n = _stream.Read(inBuf);
                        if (n >= 10 && inBuf[1] == _commandId)
                        {
                            int percent = Math.Clamp((int)inBuf[6], 0, 100);
                            bool charging = inBuf[7] != 0;
                            int voltageMv = (inBuf[8] << 8) | inBuf[9];
                            return new BatteryReading(percent, charging, voltageMv);
                        }
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
