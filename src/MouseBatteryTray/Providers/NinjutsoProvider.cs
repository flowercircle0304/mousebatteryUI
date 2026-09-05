using HidSharp;
using Microsoft.Win32.SafeHandles;

namespace MouseBatteryTray.Providers;

/// <summary>
/// Ninjutso's "current"-generation wireless mice (Sora V3, TEN / TEN AIR) — the NinjaForce WebHID
/// panel's own protocol, per OpenMouse's reverse engineering (github.com/OpenMouse-Project/mouse-protocol,
/// src/ninjutso/index.ts + src/drivers/ninjutso/hid.ts). Does NOT cover the older "legacy" Sora V2
/// (VID 0x1915, a much larger checksummed settings blob) — only this "current" family (VID 0x093A).
/// OpenMouse's own comment: "Packet layouts are public and deterministic; hardware verification is
/// still pending" — same caveat applies here; no Ninjutso hardware was available during development.
///
/// Wire protocol: Feature report id 6, 15-byte payload (16 bytes total including the report-id byte).
/// Request: byte[1]=command, byte[2]=0, byte[3]=0, byte[4]=1, byte[5]=0, byte[6]=argument count (0
/// for battery), byte[7]=profile (0 for battery — only certain settings commands carry a profile
/// number), remaining bytes 0. Commands used here: batteryPercent=18, batteryCharging=17.
///
/// Response: byte[2] echoes the command byte (reject the reply otherwise — OpenMouse's own decoder
/// does the same), byte[9] holds the first response data byte, which is the battery percent (0-100
/// direct) for the batteryPercent command and the charging flag (1=charging) for batteryCharging.
/// OpenMouse's own client resends the request on every retry attempt rather than re-reading a stale
/// GetFeature result, which this mirrors.
/// </summary>
public sealed class NinjutsoProvider : IMouseBatteryProvider
{
    public string Id { get; }
    public string DisplayName { get; }

    private const int VendorId = 0x093A;
    private const int FeatLen = 16;
    private const byte ReportId = 6;
    private const byte BatteryPercentCommand = 18;
    private const byte BatteryChargingCommand = 17;

    private static readonly int[] DefaultProductIds = { 0xE010, 0xE020, 0xEA01, 0xEB01, 0xEB02 };

    private readonly IReadOnlySet<int> _productIds;

    public NinjutsoProvider(string id = "ninjutso", string displayName = "Ninjutso Sora V3 / TEN",
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
            int? percent = Query(handle, BatteryPercentCommand);
            if (percent is null) return null;

            int? charging = Query(handle, BatteryChargingCommand);
            return new BatteryReading(Math.Clamp(percent.Value, 0, 100), charging == 1, null);
        }

        private static int? Query(SafeFileHandle handle, byte command)
        {
            var request = new byte[FeatLen];
            request[0] = ReportId;
            request[1] = command;
            request[4] = 1;

            for (int attempt = 0; attempt < 4; attempt++)
            {
                if (!RawHidFeatureIo.SetFeature(handle, request)) return null;
                Thread.Sleep(attempt == 0 ? 30 : 60);

                var response = new byte[FeatLen];
                if (RawHidFeatureIo.GetFeature(handle, response) && response[2] == command)
                    return response[9];
            }
            return null;
        }

        public void Dispose()
        {
            foreach (var handle in _handles) handle.Dispose();
        }
    }
}
