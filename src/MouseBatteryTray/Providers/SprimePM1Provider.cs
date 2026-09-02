using HidSharp;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace MouseBatteryTray.Providers;

/// <summary>
/// SPRIME PM1 wireless gaming mouse. Unlike every other provider here, this wasn't reverse
/// engineered — it's read straight from SPRIME's own official web configurator
/// (https://www.sprime.pro/), a WebHID-based tool. Its JS bundle
/// (assets/customization-*.js, function named `h` in the minified source) contains the exact
/// SetFeature/GetFeature battery-query sequence in cleartext: a first-party protocol reference,
/// not a guess. <b>UNVERIFIED</b> against real hardware from within this app specifically — a real
/// capture showed GetFeature consistently failing outright (IOException) even once the buffer
/// length was confirmed correct against the parsed report descriptor, so this now goes through
/// <see cref="RawHidFeatureIo"/> (the same reduced-access P/Invoke path built for Razer) instead of
/// HidSharp's own stream, on the chance this collection has a similar access-rights quirk despite
/// opening successfully at the file level.
///
/// Wire protocol: Feature report id 5, confirmed 704 bytes via the parsed HID report descriptor.
/// Request: byte[0]=report id, byte[1]=0x15 ("get status"), byte[4]=0x01, rest zero; the vendor's
/// own tool waits ~90ms after sending before reading the response back on the same report id.
/// Response payload: byte[10]=battery% (0-100), byte[11]=charging flag, byte[12]=full-charge flag,
/// byte[13]=online flag.
/// </summary>
public sealed class SprimePM1Provider : IMouseBatteryProvider
{
    public string Id { get; }
    public string DisplayName { get; }

    private const int VendorId = 0x1915;
    private const int ProductId = 0xAC1C;

    // This collection's declared max Feature report length (it multiplexes several report ids —
    // battery status is just the smallest/simplest of them); matched on directly since it's the
    // one that actually opens normally for this device (see HidDiagnostics output). Confirmed via
    // the parsed report descriptor that report id 5 itself also declares this same 704-byte length.
    private const int CollectionFeatureLength = 704;

    private const byte ReportId = 5;
    private const byte CommandGetStatus = 0x15;

    public SprimePM1Provider(string id = "sprime-pm1", string displayName = "SPRIME PM1")
    {
        Id = id;
        DisplayName = displayName;
    }

    public bool OwnsVendorProduct(int vendorId, int productId) =>
        vendorId == VendorId && productId == ProductId;

    private static byte[] BuildRequest()
    {
        var request = new byte[CollectionFeatureLength];
        request[0] = ReportId;
        request[1] = CommandGetStatus;
        request[4] = 0x01;
        return request;
    }

    /// <summary>Diagnostics-only: same request/response exchange as <see cref="Session.GetLatest"/>,
    /// but reports exactly what happened (which step failed, or the raw response bytes) instead of
    /// collapsing everything to null — for when "opens fine but never gets a reading" isn't enough
    /// information to tell a wrong offset apart from a wrong request shape.</summary>
    public static string DebugRawExchange(IReadOnlyList<HidDevice> collections)
    {
        var target = collections.FirstOrDefault(d => d.GetMaxFeatureReportLength() == CollectionFeatureLength);
        if (target is null) return $"Feat={CollectionFeatureLength}のコレクションが見つかりません";

        string reportList;
        try
        {
            var descriptor = target.GetReportDescriptor();
            reportList = string.Join(", ", descriptor.FeatureReports.Select(r => $"id={r.ReportID} len={r.Length}"));
            if (string.IsNullOrEmpty(reportList)) reportList = "(Featureレポートなし)";
        }
        catch (Exception ex)
        {
            reportList = $"取得失敗: {ex.GetType().Name}: {ex.Message}";
        }

        var handle = RawHidFeatureIo.Open(target.DevicePath);
        if (handle is null) return $"Featureレポート一覧: [{reportList}] / コレクションのオープンに失敗しました（縮小アクセスでも不可）";

        using (handle)
        {
            var request = BuildRequest();
            bool setOk = RawHidFeatureIo.SetFeature(handle, request);
            if (!setOk)
            {
                return $"Featureレポート一覧: [{reportList}] / 送信: {BitConverter.ToString(request, 0, 16)}... ({request.Length}バイト) / SetFeature失敗（Win32エラー {Marshal.GetLastWin32Error()}）";
            }

            Thread.Sleep(90);

            var response = new byte[CollectionFeatureLength];
            response[0] = ReportId;
            bool getOk = RawHidFeatureIo.GetFeature(handle, response);
            if (!getOk)
            {
                return $"Featureレポート一覧: [{reportList}] / 送信: {BitConverter.ToString(request, 0, 16)}... ({request.Length}バイト) / GetFeature失敗（Win32エラー {Marshal.GetLastWin32Error()}）";
            }

            return $"Featureレポート一覧: [{reportList}] / 送信: {BitConverter.ToString(request, 0, 16)}... / 受信: {BitConverter.ToString(response, 0, 16)}...（先頭16バイト、全{response.Length}バイト）";
        }
    }

    public IBatteryDeviceSession? TryOpen(IReadOnlyList<HidDevice> collections)
    {
        var target = collections.FirstOrDefault(d => d.GetMaxFeatureReportLength() == CollectionFeatureLength);
        if (target is null) return null;

        var handle = RawHidFeatureIo.Open(target.DevicePath);
        if (handle is null) return null;

        return new Session(DisplayName, handle);
    }

    private sealed class Session : IBatteryDeviceSession
    {
        private readonly SafeFileHandle _handle;
        private readonly object _lock = new();

        public string DeviceLabel { get; }

        public Session(string label, SafeFileHandle handle)
        {
            DeviceLabel = label;
            _handle = handle;
        }

        public BatteryReading? GetLatest()
        {
            lock (_lock)
            {
                var request = BuildRequest();
                if (!RawHidFeatureIo.SetFeature(_handle, request)) return null;

                Thread.Sleep(90); // matches the vendor tool's own pacing between the request and the read

                var response = new byte[CollectionFeatureLength];
                response[0] = ReportId;
                if (!RawHidFeatureIo.GetFeature(_handle, response)) return null;

                int percent = Math.Clamp((int)response[10], 0, 100);
                bool charging = response[11] != 0;
                return new BatteryReading(percent, charging, null);
            }
        }

        public void Dispose() => _handle.Dispose();
    }
}
