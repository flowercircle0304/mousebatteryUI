using HidSharp;
using Microsoft.Win32.SafeHandles;

namespace MouseBatteryTray.Providers;

/// <summary>
/// moddoMOUSE (moddo.io). Reverse engineered by the OpenMouse project
/// (github.com/OpenMouse-Project/mouse-protocol, src/moddo/index.ts + src/drivers/moddo/hid.ts) from
/// the vendor's own "moddoHUB-Web" tool. No moddoMOUSE hardware was available to verify this here.
///
/// Unlike most other providers here, the target collection is identified by its HID usage (vendor
/// page 0xFF, usage 0x01, or legacy usage 0x02) rather than by a specific Feature report length,
/// since OpenMouse's own detection does the same and this device's numbered feature reports (config
/// 0x02, firmware 0x03, battery 0x04) may not share one fixed collection-wide length the way this
/// codebase's length-based matching elsewhere assumes. The collection's own reported max Feature
/// length is used for the actual buffer size instead of a hardcoded number.
///
/// Battery (feature report id 4) needs no prior SetFeature — GetFeature alone returns the current
/// reading. Response payload (byte[1] is the first payload byte, right after the report-id byte at
/// byte[0]): byte[1]=remaining percent (0-100 direct; ignore if &gt;100 — that's a "not ready" state),
/// byte[3]=charger status (0x44=charging, 0x45=discharging, 0x46=fully charged, 0x47=fully
/// discharged), byte[4]=charger/cell-presence flags, bit 0 = a battery cell is actually present
/// (clear for a wired-only connection with no cell to report).
/// </summary>
public sealed class ModdoMouseProvider : IMouseBatteryProvider
{
    public string Id { get; }
    public string DisplayName { get; }

    private const int VendorId = 0x2FE3;
    private const int UsagePageVendor = 0xFF;
    private const byte BatteryReportId = 0x04;
    private const byte StatusCharging = 0x44;
    private const byte StatusFullyCharged = 0x46;

    public ModdoMouseProvider(string id = "moddo-mouse", string displayName = "moddoMOUSE")
    {
        Id = id;
        DisplayName = displayName;
    }

    public bool OwnsVendorProduct(int vendorId, int productId) => vendorId == VendorId;

    public IBatteryDeviceSession? TryOpen(IReadOnlyList<HidDevice> collections)
    {
        var target = collections.FirstOrDefault(HasVendorConfigUsage);
        if (target is null) return null;

        int featLen = target.GetMaxFeatureReportLength();
        if (featLen < 5) return null; // too small to ever hold the battery report

        var handle = RawHidFeatureIo.Open(target.DevicePath);
        if (handle is null) return null;

        return new Session(DisplayName, handle, featLen);
    }

    private static bool HasVendorConfigUsage(HidDevice device)
    {
        try
        {
            var descriptor = device.GetReportDescriptor();
            foreach (var item in descriptor.DeviceItems)
            {
                foreach (uint usage in item.Usages.GetAllValues())
                {
                    int usagePage = (int)(usage >> 16);
                    int usageId = (int)(usage & 0xFFFF);
                    if (usagePage == UsagePageVendor && (usageId == 0x01 || usageId == 0x02)) return true;
                }
            }
        }
        catch (Exception)
        {
            // Some collections' descriptors can't be parsed (permissions, malformed data) — treat
            // that the same as "not a match" rather than letting it take the whole scan down.
        }
        return false;
    }

    private sealed class Session : IBatteryDeviceSession
    {
        private readonly SafeFileHandle _handle;
        private readonly int _featLen;
        private readonly object _lock = new();

        public string DeviceLabel { get; }

        public Session(string label, SafeFileHandle handle, int featLen)
        {
            DeviceLabel = label;
            _handle = handle;
            _featLen = featLen;
        }

        public BatteryReading? GetLatest()
        {
            lock (_lock)
            {
                var response = new byte[_featLen];
                response[0] = BatteryReportId;
                if (!RawHidFeatureIo.GetFeature(_handle, response)) return null;
                if (response.Length < 5) return null;

                int chargerFlags = response[4];
                if ((chargerFlags & 0x01) == 0) return null; // no battery cell present (wired)

                int percent = response[1];
                if (percent > 100) return null; // sleeping/not-ready sentinel

                byte status = response[3];
                bool charging = status == StatusCharging || status == StatusFullyCharged;
                return new BatteryReading(percent, charging, null);
            }
        }

        public void Dispose() => _handle.Dispose();
    }
}
