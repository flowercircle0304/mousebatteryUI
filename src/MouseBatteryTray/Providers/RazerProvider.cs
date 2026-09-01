using HidSharp;
using Microsoft.Win32.SafeHandles;

namespace MouseBatteryTray.Providers;

/// <summary>
/// Razer's HID feature-report control protocol (used by Synapse and the open-source OpenRazer
/// driver). Unlike Logitech's HID++, Razer's per-model quirk is a fixed "transaction id" byte that
/// must match what that exact model expects, so — like COMPX — this needs one entry per product id
/// rather than matching the whole vendor generically.
///
/// Implemented from and cross-checked against two independent sources: the openrazer kernel driver
/// (github.com/openrazer/openrazer, razermouse_driver.c / razerchromacommon.c — the actual command
/// bytes and the "Viper V3 Pro" transaction id come straight from there) and a separate community
/// reimplementation (github.com/xzeldon/razer-battery-report). Confirmed working against a real
/// Viper V3 Pro.
///
/// Wire protocol: a 90-byte command structure sent via HID SetFeature / GetFeature (report id
/// 0x00, so 91 bytes on the wire): status, transaction_id, remaining_packets(2, BE),
/// protocol_type, data_size, command_class, command_id, arguments(80), crc, reserved. crc is the
/// XOR of bytes [2..88). Battery level is command_class=0x07/command_id=0x80 (result in
/// arguments[1], scale 0-255 → 0-100); charging status is command_class=0x07/command_id=0x84
/// (arguments[1] != 0).
/// </summary>
public sealed class RazerProvider : IMouseBatteryProvider
{
    public string Id { get; }
    public string DisplayName { get; }

    private const int RazerVendorId = 0x1532;
    private readonly IReadOnlySet<int> _productIds;
    private readonly byte _transactionId;

    /// <summary>Wireless-PID ↔ wired-PID pairs for known models (from openrazer's device header).
    /// Many Razer mice re-enumerate under the wired PID the instant the charging cable is plugged
    /// in, so a device registered under only one of the pair looks like it vanished while charging.
    /// Used to auto-widen matching below even for entries saved before this pairing existed
    /// (<see cref="AppSettings.DiscoveredDeviceSpec.AdditionalProductIds"/> is the deliberate,
    /// per-entry version of the same idea — this is just a safety net for what it doesn't cover).</summary>
    private static readonly IReadOnlyDictionary<int, int> KnownWiredCounterpart = new Dictionary<int, int>
    {
        { 0x00C1, 0x00C0 }, { 0x00C0, 0x00C1 }, // Viper V3 Pro
        { 0x00B7, 0x00B6 }, { 0x00B6, 0x00B7 }, // DeathAdder V3 Pro
        { 0x00AB, 0x00AA }, { 0x00AA, 0x00AB }, // Basilisk V3 Pro
        { 0x007B, 0x007A }, { 0x007A, 0x007B }, // Viper Ultimate
        { 0x007D, 0x007C }, { 0x007C, 0x007D }, // DeathAdder V2 Pro
    };

    public RazerProvider(string id, string displayName, IEnumerable<int> productIds, byte transactionId)
    {
        Id = id;
        DisplayName = displayName;

        var ids = productIds.ToHashSet();
        foreach (var pid in ids.ToList())
        {
            if (KnownWiredCounterpart.TryGetValue(pid, out var counterpart)) ids.Add(counterpart);
        }
        _productIds = ids;
        _transactionId = transactionId;
    }

    public bool OwnsVendorProduct(int vendorId, int productId) =>
        vendorId == RazerVendorId && _productIds.Contains(productId);

    public IBatteryDeviceSession? TryOpen(IReadOnlyList<HidDevice> collections)
    {
        // Razer reuses the primary mouse collection's Feature-report channel for control commands
        // rather than a dedicated vendor interface; matched here by the 91-byte (1 report-id + 90
        // payload) feature report length.
        var target = collections.FirstOrDefault(d => d.GetMaxFeatureReportLength() == 91);
        if (target is null) return null;

        // This collection is also the mouse's primary "Mouse" usage, which Windows protects from
        // full read/write opens (anti-keylogger). HidSharp's Open()/TryOpen() only tries the full
        // access request and gives up when Windows refuses it. Feature reports don't need that
        // access though, so we go around HidSharp here and open the device path directly, with the
        // same reduced-access fallback hidapi's Windows backend uses. See RawHidFeatureIo.
        var handle = RawHidFeatureIo.Open(target.DevicePath);
        if (handle is null) return null;

        return new Session(DisplayName, handle, _transactionId);
    }

    private sealed class Session : IBatteryDeviceSession
    {
        private const byte StatusNewCommand = 0x00;
        private const byte StatusBusy = 0x01;
        private const byte StatusSuccessful = 0x02;
        private const byte StatusNoResponse = 0x04;

        private readonly SafeFileHandle _handle;
        private readonly byte _transactionId;
        private readonly object _lock = new();

        public string DeviceLabel { get; }

        public Session(string label, SafeFileHandle handle, byte transactionId)
        {
            DeviceLabel = label;
            _handle = handle;
            _transactionId = transactionId;
        }

        public BatteryReading? GetLatest()
        {
            lock (_lock)
            {
                try
                {
                    var levelArgs = SendCommand(0x07, 0x80, 0x02);
                    if (levelArgs is null) return null;
                    int percent = Math.Clamp((int)Math.Round(levelArgs[1] / 255.0 * 100.0), 0, 100);

                    var chargeArgs = SendCommand(0x07, 0x84, 0x02);
                    bool? charging = chargeArgs is null ? null : chargeArgs[1] != 0;

                    return new BatteryReading(percent, charging, null);
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <summary>Sends one Razer control command and returns its 80-byte arguments array, or
        /// null after retries/on an unsupported-command reply.</summary>
        private byte[]? SendCommand(byte commandClass, byte commandId, byte dataSize)
        {
            var payload = new byte[90];
            payload[0] = StatusNewCommand;
            payload[1] = _transactionId;
            // payload[2..4) remaining_packets = 0
            payload[4] = 0x00; // protocol_type
            payload[5] = dataSize;
            payload[6] = commandClass;
            payload[7] = commandId;
            payload[88] = Crc(payload);
            // payload[89] reserved = 0

            var request = new byte[91];
            request[0] = 0x00; // report id
            Array.Copy(payload, 0, request, 1, 90);

            for (int attempt = 0; attempt < 5; attempt++)
            {
                if (!RawHidFeatureIo.SetFeature(_handle, request))
                {
                    return null;
                }
                Thread.Sleep(60);

                var response = new byte[91];
                response[0] = 0x00;
                if (!RawHidFeatureIo.GetFeature(_handle, response))
                {
                    return null;
                }

                byte status = response[1];
                byte respCommandClass = response[7];
                byte respCommandId = response[8];
                byte respCrc = response[89];

                bool matches = respCommandClass == commandClass && respCommandId == commandId;
                bool crcOk = Crc(response, offset: 1) == respCrc;

                if (status == StatusSuccessful && matches && crcOk)
                {
                    var args = new byte[80];
                    Array.Copy(response, 9, args, 0, 80);
                    return args;
                }
                if (status is StatusBusy or StatusNoResponse)
                {
                    Thread.Sleep(300);
                    continue;
                }
                return null; // not supported / failure / mismatched reply
            }
            return null;
        }

        /// <summary>XOR of payload bytes [2..88) — matches openrazer's <c>calculate_crc</c>.
        /// <paramref name="offset"/> lets this run over a 91-byte wire buffer (report id at [0])
        /// by shifting the window to [2+offset..88+offset).</summary>
        private static byte Crc(byte[] buf, int offset = 0)
        {
            byte crc = 0;
            for (int i = 2 + offset; i < 88 + offset; i++) crc ^= buf[i];
            return crc;
        }

        public void Dispose() => _handle.Dispose();
    }
}
