using HidSharp;
using Microsoft.Win32.SafeHandles;

namespace MouseBatteryTray.Providers;

/// <summary>
/// WLMouse Strider wireless gaming mouse. Like SPRIME PM1, this wasn't reverse engineered from a
/// packet capture — it's read straight from WLMouse's own official web hub
/// (https://gm.wlmouse.gg/), whose JS bundle contains a `getBatPer()` function in cleartext, and its
/// caller showing exactly how the two returned bytes are interpreted (charging flag, then percent).
/// <b>Confirmed working against real hardware</b> connected to this machine during development.
///
/// Wire protocol: Feature report id 0, 65 bytes total (1 report-id byte + 64-byte payload, matching
/// this collection's declared Feat length exactly — no multiplexed larger report the way PM1's
/// collection had). Request: byte[3]=0x02, byte[4]=0x02, byte[6]=0x83, rest zero.
///
/// The device's first GetFeature response after a fresh SetFeature is very often a "not ready yet"
/// placeholder (status byte 0xA0) rather than real data (0xA1) — confirmed via a live capture where
/// a mouse that had been idle/asleep stayed at 0xA0 across 30 retries until physically moved, after
/// which the very next read came back 0xA1 immediately. The vendor's own web hub handles this with
/// up to 30 retries of GetFeature alone (no re-sending SetFeature) at a 30ms pace; this uses a
/// smaller budget suited to a background poll loop that just tries again next cycle instead.
///
/// Response payload: byte[1]=status (0xA1 once ready), byte[4]/byte[6] echo the request's byte[3]/
/// byte[6], byte[7]=charging flag (1=charging), byte[8]=battery% (0-100 direct).
/// </summary>
public sealed class WlMouseStriderProvider : IMouseBatteryProvider
{
    public string Id { get; }
    public string DisplayName { get; }

    private const int VendorId = 0x36A7;
    private const int FeatLen = 65;
    private const byte StatusReady = 0xA1;

    // The mouse enumerates under PID 0xA872 via its 2.4GHz dongle receiver and under 0xA873 when
    // connected directly (cable/BT) — both were seen simultaneously on real hardware during
    // development, so both are matched by default (same pattern as RazerProvider's wired/wireless
    // PID pairs).
    private static readonly int[] DefaultProductIds = { 0xA872, 0xA873 };

    private readonly IReadOnlySet<int> _productIds;

    public WlMouseStriderProvider(string id = "wlmouse-strider", string displayName = "WLMouse Strider", IEnumerable<int>? productIds = null)
    {
        Id = id;
        DisplayName = displayName;
        _productIds = (productIds ?? DefaultProductIds).ToHashSet();
    }

    public bool OwnsVendorProduct(int vendorId, int productId) =>
        vendorId == VendorId && _productIds.Contains(productId);

    public IBatteryDeviceSession? TryOpen(IReadOnlyList<HidDevice> collections)
    {
        // The receiver PID and the wired/BT-direct PID can both be present at once (e.g. while a
        // charging cable is plugged in), each exposing its own Feat=65 collection, but on real
        // hardware only one of them actually answers the battery query — the other opens fine and
        // just never responds. Open every candidate and let the session probe them at read time
        // rather than guessing here, since which one is "live" can vary machine to machine.
        var handles = collections
            .Where(d => d.GetMaxFeatureReportLength() == FeatLen)
            .Select(d => RawHidFeatureIo.Open(d.DevicePath))
            .Where(h => h is not null)
            .Select(h => h!)
            .ToList();

        return handles.Count == 0 ? null : new Session(DisplayName, handles);
    }

    private sealed class Session : IBatteryDeviceSession
    {
        private readonly List<SafeFileHandle> _handles;
        private readonly object _lock = new();
        private int _lastWorkingIndex;

        public string DeviceLabel { get; }

        public Session(string label, List<SafeFileHandle> handles)
        {
            DeviceLabel = label;
            _handles = handles;
        }

        public BatteryReading? GetLatest()
        {
            lock (_lock)
            {
                // Try the handle that answered last time first, so a mouse that's plainly working
                // doesn't pay the cost of probing a dead collection on every single poll.
                for (int offset = 0; offset < _handles.Count; offset++)
                {
                    int index = (_lastWorkingIndex + offset) % _handles.Count;
                    var reading = TryRead(_handles[index]);
                    if (reading is not null)
                    {
                        _lastWorkingIndex = index;
                        return reading;
                    }
                }
                return null; // mouse likely asleep on every candidate — the next poll cycle tries again
            }
        }

        private static BatteryReading? TryRead(SafeFileHandle handle)
        {
            var request = new byte[FeatLen];
            request[3] = 0x02;
            request[4] = 0x02;
            request[6] = 0x83;
            if (!RawHidFeatureIo.SetFeature(handle, request)) return null;

            Thread.Sleep(100);

            for (int attempt = 0; attempt < 10; attempt++)
            {
                var response = new byte[FeatLen];
                // response[6] echoes the request's own command byte (0x83) — checking it rejects a
                // stale/mismatched reply left over from a different command on the same collection
                // (e.g. one issued by the vendor's own web hub concurrently), which would otherwise
                // read as a spuriously low or garbage battery percentage despite status byte 0xA1.
                if (RawHidFeatureIo.GetFeature(handle, response) && response[1] == StatusReady && response[6] == 0x83)
                {
                    bool charging = response[7] == 1;
                    int percent = Math.Clamp((int)response[8], 0, 100);
                    return new BatteryReading(percent, charging, null);
                }
                Thread.Sleep(30);
            }
            return null;
        }

        public void Dispose()
        {
            foreach (var handle in _handles) handle.Dispose();
        }
    }
}
