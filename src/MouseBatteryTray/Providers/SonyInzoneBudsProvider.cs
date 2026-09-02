using HidSharp;

namespace MouseBatteryTray.Providers;

/// <summary>
/// Sony INZONE Buds (WF-G700N) wireless gaming earbuds. Not a mouse, but they connect over the same
/// shape of device this app already supports — a 2.4GHz USB-C dongle, no Bluetooth required — so
/// the same passive-listen architecture applies even though the device itself isn't a mouse.
///
/// Implemented from and cross-checked against the HeadsetControl project
/// (github.com/Sapd/HeadsetControl, lib/devices/sony_inzone_buds.hpp) — an actively maintained,
/// widely-used open-source headset battery/control utility. <b>UNVERIFIED</b> against real hardware
/// in this project specifically.
///
/// Wire protocol: the dongle pushes unsolicited 64-byte Input reports of several different kinds;
/// only the one identified by byte[1]=0x12, byte[2]=0x04 carries battery data. Within that report:
/// byte[14]=right earbud % (0-100 direct), byte[16]=left earbud %, byte[18]=charging case %
/// (case not currently surfaced). <see cref="BatteryReading.Percent"/> is the lower of the two
/// earbuds — matching HeadsetControl's own choice, since that's the one that actually limits how
/// much longer they can be used — while <see cref="BatteryReading.SubReadings"/> carries both L and
/// R individually so the popup can show them side by side.
/// </summary>
public sealed class SonyInzoneBudsProvider : IMouseBatteryProvider
{
    public string Id { get; }
    public string DisplayName { get; }

    private const int VendorId = 0x054C;
    private const int ProductId = 0x0EC2;
    private const int ReportLength = 64;
    private const byte BatteryReportType = 0x12;
    private const byte BatteryReportSubtype = 0x04;
    private const int RightEarbudOffset = 14;
    private const int LeftEarbudOffset = 16;

    public SonyInzoneBudsProvider(string id = "sony-inzone-buds", string displayName = "Sony INZONE Buds")
    {
        Id = id;
        DisplayName = displayName;
    }

    public bool OwnsVendorProduct(int vendorId, int productId) =>
        vendorId == VendorId && productId == ProductId;

    public IBatteryDeviceSession? TryOpen(IReadOnlyList<HidDevice> collections)
    {
        var target = collections.FirstOrDefault(d => d.GetMaxInputReportLength() == ReportLength);
        if (target is null) return null;
        if (!target.TryOpen(out var stream)) return null;

        return new Session(DisplayName, stream);
    }

    /// <summary>
    /// Unlike every other provider here, this one can't just read a few times per poll tick and
    /// give up — HeadsetControl's own budget for the same report is up to 45 attempts at a 5-second
    /// timeout each (up to ~4 minutes worst case), which means this battery report is genuinely
    /// infrequent, not "usually there within a couple of seconds" like FURYCUBE's push or ATK's
    /// command/response. A bounded per-poll read (what this originally did) can go many poll cycles
    /// — potentially forever — without ever landing inside the report's actual push interval.
    ///
    /// So instead: a dedicated background thread blocks on <c>HidStream.Read</c> continuously for as
    /// long as the session is open, and caches the last qualifying report whenever one arrives.
    /// <see cref="GetLatest"/> just returns that cache immediately — decoupling "how long until the
    /// device happens to push a battery report" from DeviceManager's poll cadence entirely.
    /// </summary>
    private sealed class Session : IBatteryDeviceSession
    {
        private readonly HidStream _stream;
        private readonly Thread _listenThread;
        private readonly object _lock = new();
        private volatile bool _disposed;
        private BatteryReading? _lastReading;

        public string DeviceLabel { get; }

        public Session(string label, HidStream stream)
        {
            DeviceLabel = label;
            _stream = stream;
            // Short enough that Dispose()'s _disposed flag is noticed promptly, long enough that
            // the loop isn't churning through pointless timeout exceptions while it waits.
            _stream.ReadTimeout = 3000;

            _listenThread = new Thread(ListenLoop) { IsBackground = true, Name = "SonyInzoneBudsListener" };
            _listenThread.Start();
        }

        private void ListenLoop()
        {
            var buf = new byte[ReportLength];
            while (!_disposed)
            {
                int n;
                try
                {
                    n = _stream.Read(buf);
                }
                catch (TimeoutException)
                {
                    continue;
                }
                catch
                {
                    return; // stream closed/disposed out from under us — nothing left to listen to
                }

                if (n > LeftEarbudOffset && buf[1] == BatteryReportType && buf[2] == BatteryReportSubtype)
                {
                    int right = Math.Clamp((int)buf[RightEarbudOffset], 0, 100);
                    int left = Math.Clamp((int)buf[LeftEarbudOffset], 0, 100);
                    int worst = Math.Min(left, right);
                    var reading = new BatteryReading(worst, null, null, new[] { ("L", left), ("R", right) });
                    lock (_lock) { _lastReading = reading; }
                }
            }
        }

        public BatteryReading? GetLatest()
        {
            lock (_lock) return _lastReading;
        }

        public void Dispose()
        {
            _disposed = true;
            _stream.Dispose();
            _listenThread.Join(TimeSpan.FromSeconds(2));
        }
    }
}
