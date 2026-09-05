using HidSharp;
using Microsoft.Win32.SafeHandles;

namespace MouseBatteryTray.Providers;

/// <summary>
/// Endgame Gear's WE-series wireless mice (OP1we, XM2we, and siblings sharing the same firmware
/// platform). Matches by VendorId alone (like <see cref="LogitechHidPpProvider"/>) rather than a
/// specific model PID, since OpenMouse's own WebHID filters for this family
/// (github.com/OpenMouse-Project/mouse-protocol, src/drivers/endgame/egg-we-control.ts) are
/// usage-page-based, not PID-based — any WE-series mouse under this VID answers the same way.
///
/// This is the SAME EEPROM-register command scheme ATK's own dongles reuse (see
/// <see cref="CompxDongleProvider"/>'s doc comment: "ATK shares the Endgame Gear WE framing"), but
/// carried over genuine HID Feature reports here rather than ATK's output/input reports — confirmed
/// by cross-reading both OpenMouse modules (src/endgame-gear/wireless.ts and
/// src/drivers/atk/hid.ts) side by side.
///
/// Wire protocol: Feature report id 0x08, 17 bytes total (1 report-id byte + 16-byte payload).
/// Request: byte[1]=0x04 (GetPower command), rest of the payload zero, byte[16]=checksum where
/// checksum = (0x55 - (byte[0]+...+byte[15])) &amp; 0xFF (report id included in the sum, matching
/// `weReportChecksum` in OpenMouse's source). Response: byte[1] echoes the command byte, byte[6]
/// holds the battery percent (0-100 direct) — same DATA_OFFSET=5-into-the-payload used by every
/// other WE-framed read (EEPROM reads, firmware version) in OpenMouse's source. Not hardware
/// verified here; no Endgame Gear mouse was available during development.
/// </summary>
public sealed class EndgameGearWeProvider : IMouseBatteryProvider
{
    public string Id { get; }
    public string DisplayName { get; }

    private const int VendorId = 0x3367;
    private const int FeatLen = 17;
    private const byte ReportId = 0x08;
    private const byte GetPowerCommand = 0x04;

    public EndgameGearWeProvider(string id = "endgame-gear-we", string displayName = "Endgame Gear OP1we")
    {
        Id = id;
        DisplayName = displayName;
    }

    public bool OwnsVendorProduct(int vendorId, int productId) => vendorId == VendorId;

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
            var request = new byte[FeatLen];
            request[0] = ReportId;
            request[1] = GetPowerCommand;
            int sum = 0;
            for (int i = 0; i < FeatLen - 1; i++) sum += request[i];
            request[FeatLen - 1] = unchecked((byte)(0x55 - sum));

            if (!RawHidFeatureIo.SetFeature(handle, request)) return null;

            Thread.Sleep(50);

            for (int attempt = 0; attempt < 10; attempt++)
            {
                var response = new byte[FeatLen];
                if (RawHidFeatureIo.GetFeature(handle, response) && response[1] == GetPowerCommand)
                {
                    int percent = Math.Clamp((int)response[6], 0, 100);
                    return new BatteryReading(percent, null, null);
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
