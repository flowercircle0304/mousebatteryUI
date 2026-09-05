using HidSharp;

namespace MouseBatteryTray.Providers;

/// <summary>
/// Generic provider for wireless dongles built on the "COMPX" reference design (a firmware/protocol
/// shared by many rebadged Chinese 4K/8K-polling gaming mouse dongles). Reverse engineered from ATK
/// HUB's bundled renderer JS (class names T$t/f0t/Z$t) and verified against a real "ATK 8K Dongle
/// Light Version" (VID 0x373B / PID 0x1031): battery% matched ATK HUB's own display exactly.
///
/// Wire protocol:
///   Output report, ReportId=<paramref name="outputReportId"/> (default 8), body up to
///   <paramref name="reportLength"/>-2 bytes: [0]=commandId (4=GetBatteryLevel) [1]=status
///   [2-3]=eepromAddress [4]=dataValidLen [5..]=payload, last byte=checksum =
///   (85 - (ReportId + sum(all preceding body bytes))) &amp; 0xFF.
///   Input report mirrors the same layout; response body[0] echoes commandId, and battery% /
///   charging flag / voltage_mV (big-endian) live at <paramref name="percentByteOffset"/> and the
///   two offsets after it — that's the ATK dongle's exact byte layout, kept only as a *default*:
///   report length and the percent offset are otherwise fully configurable, since the "add a mouse"
///   wizard's active probe (<see cref="DeviceDiscovery.TryActiveCompxMatch"/>) discovers both per
///   device rather than assuming every COMPX-family dongle is byte-for-byte identical to ATK's.
///
/// To add a new COMPX-family dongle: try the wizard's active probe first — it now scans any
/// symmetric-length in/out collection and finds the real percent offset itself. Only hand-write an
/// entry if that fails.
/// </summary>
public sealed class CompxDongleProvider : IMouseBatteryProvider
{
    public string Id { get; }
    public string DisplayName { get; }

    private readonly int _vendorId;
    private readonly IReadOnlySet<int> _productIds;
    private readonly byte _outputReportId;
    private readonly byte _commandId;
    private readonly int _reportLength;
    private readonly int _percentByteOffset;
    private readonly int _chargingByteOffset;
    private readonly int _voltageByteOffset;

    public CompxDongleProvider(
        string id,
        string displayName,
        int vendorId,
        IEnumerable<int> productIds,
        byte outputReportId = 8,
        byte getBatteryCommandId = 4,
        int reportLength = 17,
        int percentByteOffset = 6,
        int? chargingByteOffset = null,
        int? voltageByteOffset = null)
    {
        Id = id;
        DisplayName = displayName;
        _vendorId = vendorId;
        _productIds = productIds.ToHashSet();
        _outputReportId = outputReportId;
        _commandId = getBatteryCommandId;
        _reportLength = reportLength;
        _percentByteOffset = percentByteOffset;
        _chargingByteOffset = chargingByteOffset ?? percentByteOffset + 1;
        _voltageByteOffset = voltageByteOffset ?? percentByteOffset + 2;
    }

    public bool OwnsVendorProduct(int vendorId, int productId) =>
        vendorId == _vendorId && _productIds.Contains(productId);

    public IBatteryDeviceSession? TryOpen(IReadOnlyList<HidDevice> collections)
    {
        var target = collections.FirstOrDefault(d =>
            d.GetMaxOutputReportLength() == _reportLength && d.GetMaxInputReportLength() == _reportLength);

        if (target is null) return null;
        if (!target.TryOpen(out var stream)) return null;

        return new Session(DisplayName, stream, _outputReportId, _commandId, _reportLength,
            _percentByteOffset, _chargingByteOffset, _voltageByteOffset);
    }

    private sealed class Session : IBatteryDeviceSession
    {
        private readonly HidStream _stream;
        private readonly byte _outputReportId;
        private readonly byte _commandId;
        private readonly int _reportLength;
        private readonly int _percentByteOffset;
        private readonly int _chargingByteOffset;
        private readonly int _voltageByteOffset;
        private readonly object _lock = new();

        public string DeviceLabel { get; }

        public Session(string label, HidStream stream, byte outputReportId, byte commandId,
            int reportLength, int percentByteOffset, int chargingByteOffset, int voltageByteOffset)
        {
            DeviceLabel = label;
            _stream = stream;
            _outputReportId = outputReportId;
            _commandId = commandId;
            _reportLength = reportLength;
            _percentByteOffset = percentByteOffset;
            _chargingByteOffset = chargingByteOffset;
            _voltageByteOffset = voltageByteOffset;
            // Short per-attempt timeout since GetLatest() now retries several times itself — the old
            // single 2000ms timeout with a retry loop that didn't actually retry (see GetLatest)
            // meant one asleep-mouse poll could still only cost 2s, but stacking that timeout across
            // several real retries would make an unresponsive device dominate the whole poll cycle.
            _stream.ReadTimeout = 500;
            _stream.WriteTimeout = 1000;
        }

        public BatteryReading? GetLatest()
        {
            lock (_lock)
            {
                // A sleeping wireless mouse simply never answers — confirmed live: the exact same
                // exchange that timed out repeatedly succeeded immediately once the mouse was moved.
                // Read() throws on timeout rather than returning 0, so each attempt needs its own
                // try/catch: wrapping the whole write+read loop in one try/catch (the old code) meant
                // the very first timeout aborted every remaining attempt, silently defeating what
                // looked like a 3-attempt retry. Each attempt resends the request too, since this is
                // an output/input report pair (not a Feature-report poll), so a stale write can't just
                // be re-read.
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        var outBuf = new byte[_reportLength];
                        outBuf[0] = _outputReportId;
                        outBuf[1] = _commandId;
                        int sum = _outputReportId + outBuf[1];
                        outBuf[_reportLength - 1] = unchecked((byte)(85 - sum));

                        _stream.Write(outBuf);

                        var inBuf = new byte[_reportLength];
                        int n = _stream.Read(inBuf);
                        if (n > _percentByteOffset && inBuf[1] == _commandId)
                        {
                            int percent = Math.Clamp((int)inBuf[_percentByteOffset], 0, 100);
                            bool? charging = _chargingByteOffset < n ? inBuf[_chargingByteOffset] != 0 : null;
                            int? voltageMv = _voltageByteOffset + 1 < n
                                ? (inBuf[_voltageByteOffset] << 8) | inBuf[_voltageByteOffset + 1]
                                : null;
                            return new BatteryReading(percent, charging, voltageMv);
                        }
                    }
                    catch (Exception)
                    {
                        // Timed out or otherwise failed on this attempt — try again.
                    }
                }
                return null;
            }
        }

        public void Dispose() => _stream.Dispose();
    }
}
