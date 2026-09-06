using HidSharp;
using Microsoft.Win32.SafeHandles;

namespace MouseBatteryTray.Providers;

/// <summary>
/// The "compx" page-command protocol — named after OpenMouse's own module for it
/// (github.com/OpenMouse-Project/mouse-protocol, src/compx/codec.ts, documented there as "Shared
/// page-command framing used by WLMouse and Lamzu receivers"). Despite the class name (kept for the
/// model this was actually built and verified against — WLMouse's Strider), this same wire protocol
/// and this same provider class, via <see cref="ProviderRegistry"/>'s "wlmouse-strider" Kind, covers:
///  - WLMouse's other 2.4GHz-dongle models (Beast G, Huan, Beast Miao, Beast Mini/Pro, Beast X/Pro,
///    Ying, Sword X, Beast Max — VID 0x36A7).
///  - Lamzu's receivers, and rebadges of them sold under other names — e.g. CRDRAKO's KO-ONE (VID
///    0x373E, per OpenMouse's own vendors.ts mapping both "lamzu" and "attackshark" to that VID, and
///    openmouse.app/supported listing Maya X / KO-ONE as "Supported" via the "Lamzu/CompX driver").
/// Each model is registered as its own template entry with its own VendorId/ProductId pair but
/// otherwise identical Kind/behavior. Only the Strider's PID pair has actually been confirmed on real
/// hardware; the rest are unverified (added from OpenMouse's own PID catalog, not tested here).
///
/// Like SPRIME PM1, this wasn't reverse engineered from a packet capture — it's read straight from
/// WLMouse's own official web hub (https://gm.wlmouse.gg/), whose JS bundle contains a `getBatPer()`
/// function in cleartext, and its caller showing exactly how the two returned bytes are interpreted
/// (charging flag, then percent). <b>Confirmed working against real hardware</b> (Strider) connected
/// to this machine during development.
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
///
/// This same framing is actually a general "page/command" channel, not something battery-specific —
/// confirmed against the OpenMouse project's own from-source WLMouse driver (src/drivers/wlmouse/
/// hid.ts), which documents the full command set behind the vendor's config UI. Its documented byte
/// layout is shifted one byte earlier than what's used here (e.g. its command byte sits at index 5,
/// this provider's at index 6) — OpenMouse's own code carries a "try index N, then N+1" fallback for
/// exactly this kind of one-byte skew across firmware revisions, and the Strider hardware this was
/// verified against always answers at the later offset, matching every fixed byte position already
/// documented above. That match across two independently-derived implementations is what makes
/// reusing the same request framing for DPI/polling-rate reads (see <see cref="TryReadDeviceInfo"/>)
/// trustworthy rather than a guess: target=mouse(2)/page=device(0)/command=0x85 for the active
/// profile, target=mouse/page=profile(1)/command=0x81 for the DPI stage table, 0x82 for which stage
/// is active, and 0x80 for the polling rate (decoded via OpenMouse's own published encoding table).
/// These are read once per poll, after the battery query already confirmed the mouse is awake, and
/// any failure here is swallowed — it only ever adds information, never breaks the battery reading.
/// </summary>
public sealed class WlMouseStriderProvider : IMouseBatteryProvider
{
    public string Id { get; }
    public string DisplayName { get; }

    private const int DefaultVendorId = 0x36A7; // WLMouse
    private const int FeatLen = 65;
    private const byte StatusReady = 0xA1;

    // "compx" page/command targets, pages and commands (see OpenMouse's src/drivers/wlmouse/hid.ts).
    // Target "mouse" is 0x02 for almost every brand under this Kind, but CRDRAKO's KO-ONE is a
    // documented exception (OpenMouse's own vendors.ts gives it `mouseTarget: 0x00` instead) — a
    // per-brand override rather than a hardcoded constant, passed in at construction.
    private const byte DefaultMouseTarget = 0x02;
    private const byte PageDevice = 0x00;
    private const byte PageProfile = 0x01;
    private const byte CmdActiveProfile = 0x85;
    private const byte CmdDpiStages = 0x81;
    private const byte CmdActiveStage = 0x82;
    private const byte CmdPollingRate = 0x80;
    private const byte DpiStageMax = 6;

    // Encoded byte -> Hz, straight from OpenMouse's WLMOUSE_POLLING_RATES table.
    private static readonly Dictionary<byte, int> PollingRates = new()
    {
        [0x08] = 125, [0x04] = 250, [0x02] = 500, [0x01] = 1000,
        [0x10] = 1000, [0x20] = 2000, [0x40] = 4000, [0x80] = 8000,
    };

    // The mouse enumerates under PID 0xA872 via its 2.4GHz dongle receiver and under 0xA873 when
    // connected directly (cable/BT) — both were seen simultaneously on real hardware during
    // development, so both are matched by default (same pattern as RazerProvider's wired/wireless
    // PID pairs). Only meaningful when no explicit productIds are passed (i.e. for the Strider
    // itself); every other model/brand always passes its own list explicitly.
    private static readonly int[] DefaultProductIds = { 0xA872, 0xA873 };

    private readonly int _vendorId;
    private readonly IReadOnlySet<int> _productIds;
    private readonly byte _mouseTarget;

    public WlMouseStriderProvider(string id = "wlmouse-strider", string displayName = "WLMouse Strider",
        IEnumerable<int>? productIds = null, int vendorId = DefaultVendorId, byte mouseTarget = DefaultMouseTarget)
    {
        Id = id;
        DisplayName = displayName;
        _vendorId = vendorId;
        _productIds = (productIds ?? DefaultProductIds).ToHashSet();
        _mouseTarget = mouseTarget;
    }

    public bool OwnsVendorProduct(int vendorId, int productId) =>
        vendorId == _vendorId && _productIds.Contains(productId);

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

        return handles.Count == 0 ? null : new Session(DisplayName, handles, _mouseTarget);
    }

    private sealed class Session : IBatteryDeviceSession
    {
        private readonly List<SafeFileHandle> _handles;
        private readonly object _lock = new();
        private readonly byte _mouseTarget;
        private int _lastWorkingIndex;

        public string DeviceLabel { get; }

        public Session(string label, List<SafeFileHandle> handles, byte mouseTarget)
        {
            DeviceLabel = label;
            _handles = handles;
            _mouseTarget = mouseTarget;
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
                        var (dpi, pollingRateHz) = TryReadDeviceInfo(_handles[index]);
                        return reading with { Dpi = dpi, PollingRateHz = pollingRateHz };
                    }
                }
                return null; // mouse likely asleep on every candidate — the next poll cycle tries again
            }
        }

        private BatteryReading? TryRead(SafeFileHandle handle)
        {
            var request = new byte[FeatLen];
            request[3] = _mouseTarget;
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

        /// <summary>Reads the mouse's current DPI (active stage's X value) and polling rate, using
        /// the same page/command channel as the battery query above — see the class doc comment for
        /// where these command ids come from. Called only right after a successful battery read, so
        /// the mouse is already known to be awake; any failure (unsupported firmware, a dropped
        /// reply) is swallowed and just means this poll shows no extra info, not a broken battery
        /// reading.</summary>
        private (int? Dpi, int? PollingRateHz) TryReadDeviceInfo(SafeFileHandle handle)
        {
            try
            {
                var profilePayload = TryExchange(handle, _mouseTarget, 0x01, PageDevice, CmdActiveProfile, null);
                byte profile = profilePayload is { Length: > 0 } p ? Math.Max((byte)1, p[0]) : (byte)1;

                var stages = TryExchange(handle, _mouseTarget, 0x0A, PageProfile, CmdDpiStages, new[] { profile, DpiStageMax });
                var activeStage = TryExchange(handle, _mouseTarget, 0x02, PageProfile, CmdActiveStage, new[] { profile });
                var pollingRate = TryExchange(handle, _mouseTarget, 0x02, PageProfile, CmdPollingRate, new[] { profile });

                int? dpi = null;
                if (stages is { Length: >= 2 } && stages[1] > 0)
                {
                    int count = stages[1];
                    int activeIndex = activeStage is { Length: >= 2 }
                        ? Math.Clamp(activeStage[1] - 1, 0, count - 1)
                        : 0;
                    int offset = 2 + activeIndex * 4;
                    if (offset + 1 < stages.Length)
                    {
                        int x = (stages[offset] << 8) | stages[offset + 1];
                        if (x > 0) dpi = x;
                    }
                }

                int? pollingRateHz = pollingRate is { Length: >= 2 } && PollingRates.TryGetValue(pollingRate[1], out int hz)
                    ? hz
                    : null;

                return (dpi, pollingRateHz);
            }
            catch (Exception)
            {
                return (null, null);
            }
        }

        /// <summary>One request/response round trip on the compx page/command channel — the general
        /// form of the battery query in <see cref="TryRead"/>, parameterized over target/page/command
        /// instead of hardcoding battery's own values. Returns the response payload (starting right
        /// after the command byte) once the mouse answers "ready" for this exact page+command, or
        /// null if it never does within a modest retry budget (already-awake mice answer almost
        /// immediately, so this doesn't need battery's own asleep-mouse-sized budget).</summary>
        private static byte[]? TryExchange(SafeFileHandle handle, byte target, byte length, byte page, byte command, byte[]? args)
        {
            var request = new byte[FeatLen];
            request[3] = target;
            request[4] = length;
            request[5] = page;
            request[6] = command;
            if (args is not null)
                for (int i = 0; i < args.Length && 7 + i < FeatLen; i++) request[7 + i] = args[i];

            if (!RawHidFeatureIo.SetFeature(handle, request)) return null;

            for (int attempt = 0; attempt < 5; attempt++)
            {
                Thread.Sleep(20);
                var response = new byte[FeatLen];
                if (RawHidFeatureIo.GetFeature(handle, response)
                    && response[1] == StatusReady
                    && response[5] == page
                    && response[6] == command)
                {
                    int payloadLen = Math.Clamp((int)response[4], 0, FeatLen - 7);
                    var payload = new byte[payloadLen];
                    Array.Copy(response, 7, payload, 0, payloadLen);
                    return payload;
                }
            }
            return null;
        }

        public void Dispose()
        {
            foreach (var handle in _handles) handle.Dispose();
        }
    }
}
