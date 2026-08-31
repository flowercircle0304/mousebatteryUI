using HidSharp;

namespace MouseBatteryTray.Providers;

/// <summary>
/// Generic provider for receivers that periodically push an unsolicited fixed-size input report
/// containing the battery percentage at a fixed byte offset — no request needed. Verified against
/// a real FURYCUBE F1 receiver (VID 0x1D57 / PID 0xFA60): a 5-byte report arrives every few
/// seconds with battery% at byte[4], confirmed both by matching FURYCUBE HUB's own display and by
/// a USBPcap capture of the receiver's interrupt endpoint.
///
/// To add a new receiver that behaves this way: find its VID/PID and the HID collection's exact
/// input report length with HidProbe, confirm (e.g. via a USB capture, or just by polling and
/// comparing against the vendor app's displayed %) which byte holds the 0-100 value, then add one
/// entry to <see cref="ProviderRegistry"/> — no new code needed.
/// </summary>
public sealed class PassivePushHidProvider : IMouseBatteryProvider
{
    public string Id { get; }
    public string DisplayName { get; }

    private readonly int _vendorId;
    private readonly int _productId;
    private readonly int _reportLength;
    private readonly int _batteryByteOffset;

    public PassivePushHidProvider(
        string id,
        string displayName,
        int vendorId,
        int productId,
        int reportLength,
        int batteryByteOffset)
    {
        Id = id;
        DisplayName = displayName;
        _vendorId = vendorId;
        _productId = productId;
        _reportLength = reportLength;
        _batteryByteOffset = batteryByteOffset;
    }

    public bool OwnsVendorProduct(int vendorId, int productId) =>
        vendorId == _vendorId && productId == _productId;

    public IBatteryDeviceSession? TryOpen(IReadOnlyList<HidDevice> collections)
    {
        var target = collections.FirstOrDefault(d =>
            d.GetMaxInputReportLength() == _reportLength && d.GetMaxOutputReportLength() == 0 && d.GetMaxFeatureReportLength() == 0);

        if (target is null) return null;
        if (!target.TryOpen(out var stream)) return null;

        return new Session(DisplayName, stream, _reportLength, _batteryByteOffset);
    }

    private sealed class Session : IBatteryDeviceSession
    {
        private readonly HidStream _stream;
        private readonly int _reportLength;
        private readonly int _batteryByteOffset;
        private readonly Thread _readerThread;
        private readonly CancellationTokenSource _cts = new();
        private volatile BatteryReading? _latest;

        public string DeviceLabel { get; }

        public Session(string label, HidStream stream, int reportLength, int batteryByteOffset)
        {
            DeviceLabel = label;
            _stream = stream;
            _reportLength = reportLength;
            _batteryByteOffset = batteryByteOffset;
            _stream.ReadTimeout = 8000;

            _readerThread = new Thread(ReadLoop) { IsBackground = true, Name = $"PassivePush[{label}]" };
            _readerThread.Start();
        }

        private void ReadLoop()
        {
            var buf = new byte[_reportLength];
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    int n = _stream.Read(buf);
                    if (n > _batteryByteOffset)
                    {
                        int percent = Math.Clamp((int)buf[_batteryByteOffset], 0, 100);
                        _latest = BatteryReading.OfPercent(percent);
                    }
                }
                catch (TimeoutException)
                {
                    // No push report arrived within the window; keep the last known reading.
                }
                catch (Exception)
                {
                    if (_cts.IsCancellationRequested) return;
                    Thread.Sleep(1000);
                }
            }
        }

        public BatteryReading? GetLatest() => _latest;

        public void Dispose()
        {
            _cts.Cancel();
            try { _stream.Dispose(); } catch { }
            _readerThread.Join(500);
        }
    }
}
