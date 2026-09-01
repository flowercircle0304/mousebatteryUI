using HidSharp;

namespace MouseBatteryTray.Providers;

/// <summary>
/// Logitech HID++ 2.0 — covers essentially all modern Logitech wireless mice through their
/// Unifying / LIGHTSPEED / Bolt receivers. Matched generically by vendor id alone: HID++ devices
/// self-describe their supported features (including which battery feature they expose), so no
/// per-model product id is needed the way COMPX or Razer require.
///
/// <b>UNVERIFIED</b> — implemented from the public HID++ 2.0 protocol, cross-referenced against
/// two independent open-source reimplementations (github.com/Ithilias/logitray and the Solaar
/// project's docs/features.md), but never tested against real Logitech hardware. Treat with the
/// same caution as any freshly-written driver: it should be safe (battery reads are pure queries,
/// no writes to device configuration), but "should work" isn't "confirmed to work".
///
/// Wire protocol: HID++ 2.0 "long" report (ReportId=0x11, 20 bytes: reportId + deviceIndex +
/// featureIndex + (functionId&lt;&lt;4|softwareId) + 3 param bytes + padding). Devices are addressed
/// by a per-receiver deviceIndex (1-6); we probe each looking for one of the three battery
/// features (0x1000 BATTERY_LEVEL, 0x1001 BATTERY_VOLTAGE, 0x1004 UNIFIED_BATTERY, checked in that
/// order) via IRoot.getFeature (feature 0x0000, function 0x00), then read whichever is found.
/// </summary>
public sealed class LogitechHidPpProvider : IMouseBatteryProvider
{
    public string Id => "logitech-hidpp";
    public string DisplayName { get; }

    public LogitechHidPpProvider(string displayName = "Logitech (HID++ 2.0)")
    {
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Logitech (HID++ 2.0)" : displayName;
    }

    private const int LogitechVendorId = 0x046D;
    private const byte LongReportId = 0x11;
    private const byte SwId = 0x0A;
    private static readonly ushort[] BatteryFeatureIds = { 0x1000, 0x1001, 0x1004 };

    public bool OwnsVendorProduct(int vendorId, int productId) => vendorId == LogitechVendorId;

    public IBatteryDeviceSession? TryOpen(IReadOnlyList<HidDevice> collections)
    {
        // The HID++ 2.0 "long" vendor collection: 20-byte in/out reports (usage page 0xFF00,
        // usage 0x0002 on a real receiver — matched here by report length, consistent with how
        // the other providers in this project identify their collection).
        var target = collections.FirstOrDefault(d =>
            d.GetMaxOutputReportLength() == 20 && d.GetMaxInputReportLength() == 20);

        if (target is null) return null;
        if (!target.TryOpen(out var stream)) return null;

        return new Session(DisplayName, stream);
    }

    private sealed class Session : IBatteryDeviceSession
    {
        private readonly HidStream _stream;
        private readonly object _lock = new();
        private (byte DeviceIndex, ushort FeatureId, byte FeatureIndex)? _resolved;

        public string DeviceLabel { get; }

        public Session(string label, HidStream stream)
        {
            DeviceLabel = label;
            _stream = stream;
            _stream.ReadTimeout = 700;
            _stream.WriteTimeout = 500;
        }

        public BatteryReading? GetLatest()
        {
            lock (_lock)
            {
                try
                {
                    if (_resolved is { } cached)
                    {
                        var reading = ReadBattery(cached.DeviceIndex, cached.FeatureId, cached.FeatureIndex);
                        if (reading is not null) return reading;
                        _resolved = null; // stale — re-resolve next call
                    }

                    if (!TryResolve(out var resolved)) return null;
                    _resolved = resolved;
                    return ReadBattery(resolved.DeviceIndex, resolved.FeatureId, resolved.FeatureIndex);
                }
                catch
                {
                    _resolved = null;
                    return null;
                }
            }
        }

        private bool TryResolve(out (byte DeviceIndex, ushort FeatureId, byte FeatureIndex) resolved)
        {
            for (byte deviceIndex = 1; deviceIndex <= 6; deviceIndex++)
            {
                foreach (ushort featureId in BatteryFeatureIds)
                {
                    var reply = SendReceive(deviceIndex, 0x00, 0x00, new byte[] { (byte)(featureId >> 8), (byte)featureId, 0x00 });
                    if (reply is null) continue;

                    byte featureIndex = reply[4];
                    if (featureIndex != 0)
                    {
                        resolved = (deviceIndex, featureId, featureIndex);
                        return true;
                    }
                }
            }
            resolved = default;
            return false;
        }

        private BatteryReading? ReadBattery(byte deviceIndex, ushort featureId, byte featureIndex)
        {
            // 0x1004 (UNIFIED_BATTERY) reads via function 0x01 ("getStatus"); the older 0x1000/0x1001
            // features both use function 0x00 for their primary getter.
            byte functionId = (byte)(featureId == 0x1004 ? 0x01 : 0x00);
            var reply = SendReceive(deviceIndex, featureIndex, functionId, new byte[] { 0, 0, 0 });
            if (reply is null) return null;

            switch (featureId)
            {
                case 0x1000:
                case 0x1004:
                {
                    int percent = Math.Clamp((int)reply[4], 0, 100);
                    byte status = reply[6];
                    bool charging = status is 1 or 2 or 4; // 1=recharging 2=final stage 3=full 4=slow charge
                    return new BatteryReading(percent, charging, null);
                }
                case 0x1001:
                {
                    int mv = (reply[4] << 8) | reply[5];
                    byte flags = reply[6];
                    bool charging = (flags & 0x80) != 0 && (flags & 0x07) != 2;
                    return new BatteryReading(VoltageToPercent(mv), charging, mv);
                }
                default:
                    return null;
            }
        }

        /// <summary>Sends one HID++ 2.0 long request and returns the matching 7-byte-equivalent
        /// header of the reply (as the first 7 bytes of the 20-byte read buffer), or null on
        /// timeout/error/mismatch after a small number of retries.</summary>
        private byte[]? SendReceive(byte deviceIndex, byte featureIndex, byte functionId, byte[] parms)
        {
            var outBuf = new byte[20];
            outBuf[0] = LongReportId;
            outBuf[1] = deviceIndex;
            outBuf[2] = featureIndex;
            outBuf[3] = (byte)((functionId << 4) | SwId);
            outBuf[4] = parms[0];
            outBuf[5] = parms[1];
            outBuf[6] = parms[2];

            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    _stream.Write(outBuf);
                }
                catch
                {
                    return null;
                }

                for (int reads = 0; reads < 4; reads++)
                {
                    byte[] inBuf;
                    try
                    {
                        inBuf = new byte[20];
                        int n = _stream.Read(inBuf);
                        if (n < 7) continue;
                    }
                    catch (TimeoutException)
                    {
                        break; // retry the write
                    }
                    catch
                    {
                        return null;
                    }

                    if (inBuf[2] == 0xFF) return null; // HID++ 2.0 error reply
                    if (inBuf[1] == deviceIndex && inBuf[2] == featureIndex && (inBuf[3] & 0x0F) == SwId)
                        return inBuf;
                    // Otherwise: an unrelated notification/event interleaved — keep reading.
                }
            }
            return null;
        }

        public void Dispose() => _stream.Dispose();

        /// <summary>mV → % lookup table (index 0 = 100%, index 99 = 1%), values from the
        /// LGSTrayBattery/Solaar projects' shared discharge curve.</summary>
        private static readonly ushort[] VoltageLut =
        {
            4186, 4156, 4143, 4133, 4122, 4113, 4103, 4094, 4086, 4075, 4067, 4059, 4051, 4043, 4035, 4027,
            4019, 4011, 4003, 3997, 3989, 3983, 3976, 3969, 3961, 3955, 3949, 3942, 3935, 3929, 3922, 3916,
            3909, 3902, 3896, 3890, 3883, 3877, 3870, 3865, 3859, 3853, 3848, 3842, 3837, 3833, 3828, 3824,
            3819, 3815, 3811, 3808, 3804, 3800, 3797, 3793, 3790, 3787, 3784, 3781, 3778, 3775, 3772, 3770,
            3767, 3764, 3762, 3759, 3757, 3754, 3751, 3748, 3744, 3741, 3737, 3734, 3730, 3726, 3724, 3720,
            3717, 3714, 3710, 3706, 3702, 3697, 3693, 3688, 3683, 3677, 3671, 3666, 3662, 3658, 3654, 3646,
            3633, 3612, 3579, 3537,
        };

        private static int VoltageToPercent(int mv)
        {
            for (int i = 0; i < VoltageLut.Length; i++)
            {
                if (mv > VoltageLut[i]) return 100 - i;
            }
            return 0;
        }
    }
}
