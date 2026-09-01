using HidSharp;
using Microsoft.Win32.SafeHandles;

namespace MouseBatteryTray.Providers;

/// <summary>
/// Generic provider for dongles that keep battery% readable in a plain HID Feature report, with no
/// request/handshake protocol needed — just a periodic HidD_GetFeature. Distinct from
/// <see cref="PassivePushHidProvider"/> (which listens for unsolicited Input reports) and from
/// <see cref="CompxDongleProvider"/> / <see cref="RazerProvider"/> (which need a specific
/// request/response command shape); this is the simplest possible case, and the "add a mouse"
/// wizard's read-only feature-report scan (<see cref="DeviceDiscovery.TryPassiveFeatureMatch"/>)
/// finds it without knowing anything about the device's protocol at all.
///
/// Uses <see cref="RawHidFeatureIo"/> instead of HidSharp's own Open(), since the target collection
/// is very often the mouse's own primary usage — the exact collection Windows blocks from full
/// read/write access (see RawHidFeatureIo's doc comment for why a reduced-access handle still works
/// for Feature reports).
/// </summary>
public sealed class PassiveFeatureHidProvider : IMouseBatteryProvider
{
    public string Id { get; }
    public string DisplayName { get; }

    private readonly int _vendorId;
    private readonly int _productId;
    private readonly int _reportLength;
    private readonly int _batteryByteOffset;

    public PassiveFeatureHidProvider(string id, string displayName, int vendorId, int productId, int reportLength, int batteryByteOffset)
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
        var target = collections.FirstOrDefault(d => d.GetMaxFeatureReportLength() == _reportLength);
        if (target is null) return null;

        var handle = RawHidFeatureIo.Open(target.DevicePath);
        if (handle is null) return null;

        return new Session(DisplayName, handle, _reportLength, _batteryByteOffset);
    }

    private sealed class Session : IBatteryDeviceSession
    {
        private readonly SafeFileHandle _handle;
        private readonly int _reportLength;
        private readonly int _batteryByteOffset;
        private readonly object _lock = new();

        public string DeviceLabel { get; }

        public Session(string label, SafeFileHandle handle, int reportLength, int batteryByteOffset)
        {
            DeviceLabel = label;
            _handle = handle;
            _reportLength = reportLength;
            _batteryByteOffset = batteryByteOffset;
        }

        public BatteryReading? GetLatest()
        {
            lock (_lock)
            {
                var buf = new byte[_reportLength];
                if (!RawHidFeatureIo.GetFeature(_handle, buf)) return null;
                int percent = Math.Clamp((int)buf[_batteryByteOffset], 0, 100);
                return BatteryReading.OfPercent(percent);
            }
        }

        public void Dispose() => _handle.Dispose();
    }
}
