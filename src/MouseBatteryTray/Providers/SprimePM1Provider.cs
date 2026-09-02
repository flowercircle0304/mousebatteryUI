using HidSharp;

namespace MouseBatteryTray.Providers;

/// <summary>
/// SPRIME PM1 wireless gaming mouse. Unlike every other provider here, this wasn't reverse
/// engineered — it's read straight from SPRIME's own official web configurator
/// (https://www.sprime.pro/), a WebHID-based tool. Its JS bundle
/// (assets/customization-*.js, function named `h` in the minified source) contains the exact
/// SetFeature/GetFeature battery-query sequence in cleartext: a first-party protocol reference,
/// not a guess. <b>UNVERIFIED</b> against real hardware from within this app specifically.
///
/// Wire protocol: Feature report id 5. The vendor's own WebHID call only sends/reads a 31-byte
/// payload (32 bytes total with the report id) — WebHID is lenient about buffer size, but Win32's
/// HidD_SetFeature/HidD_GetFeature are not: a real capture showed SetFeature accepting that 32-byte
/// buffer fine while GetFeature failed outright, which is the classic symptom of a Windows HID
/// driver expecting a buffer sized to the whole collection's declared max feature length rather
/// than just this one report id's own (smaller) size. So both directions here use a buffer sized to
/// the full collection length instead, zero-padded past the meaningful bytes. Request: byte[1]=0x15
/// ("get status"), byte[4]=0x01, rest zero; the vendor's own tool waits ~90ms after sending before
/// reading the response back on the same report id. Response payload:
///   byte[10]=battery% (0-100), byte[11]=charging flag, byte[12]=full-charge flag, byte[13]=online flag.
/// </summary>
public sealed class SprimePM1Provider : IMouseBatteryProvider
{
    public string Id { get; }
    public string DisplayName { get; }

    private const int VendorId = 0x1915;
    private const int ProductId = 0xAC1C;

    // This collection's declared max Feature report length (it multiplexes several report ids —
    // battery status is just the smallest/simplest of them); matched on directly since it's the
    // one that actually opens normally for this device (see HidDiagnostics output).
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

    /// <summary>Diagnostics-only: same request/response exchange as <see cref="Session.GetLatest"/>,
    /// but reports exactly what happened (which step failed, or the raw response bytes) instead of
    /// collapsing everything to null — for when "opens fine but never gets a reading" isn't enough
    /// information to tell a wrong offset apart from a wrong request shape.</summary>
    public static string DebugRawExchange(IReadOnlyList<HidDevice> collections)
    {
        var target = collections.FirstOrDefault(d => d.GetMaxFeatureReportLength() == CollectionFeatureLength);
        if (target is null) return $"Feat={CollectionFeatureLength}のコレクションが見つかりません";

        // The collection's overall max (704) is just the largest of possibly several distinct
        // Feature report ids it multiplexes — report id 5's own declared length could be anything.
        // Guessing that length (32? 704? something else?) is how the last two attempts both failed;
        // reading it straight from the parsed report descriptor removes the guesswork entirely.
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

        if (!target.TryOpen(out var stream)) return $"Featureレポート一覧: [{reportList}] / コレクションのオープンに失敗しました";

        using (stream)
        {
            var request = new byte[CollectionFeatureLength];
            request[0] = ReportId;
            request[1] = CommandGetStatus;
            request[4] = 0x01;

            try
            {
                stream.SetFeature(request);
            }
            catch (Exception ex)
            {
                return $"Featureレポート一覧: [{reportList}] / 送信: {BitConverter.ToString(request, 0, 16)}... ({request.Length}バイト) / SetFeatureで例外: {ex.GetType().Name}: {ex.Message}";
            }

            Thread.Sleep(90);

            var response = new byte[CollectionFeatureLength];
            try
            {
                stream.GetFeature(response);
            }
            catch (Exception ex)
            {
                return $"Featureレポート一覧: [{reportList}] / 送信: {BitConverter.ToString(request, 0, 16)}... ({request.Length}バイト) / GetFeatureで例外: {ex.GetType().Name}: {ex.Message}";
            }

            return $"Featureレポート一覧: [{reportList}] / 送信: {BitConverter.ToString(request, 0, 16)}... / 受信: {BitConverter.ToString(response, 0, 16)}...（先頭16バイト、全{response.Length}バイト）";
        }
    }

    public IBatteryDeviceSession? TryOpen(IReadOnlyList<HidDevice> collections)
    {
        var target = collections.FirstOrDefault(d => d.GetMaxFeatureReportLength() == CollectionFeatureLength);
        if (target is null) return null;
        if (!target.TryOpen(out var stream)) return null;

        return new Session(DisplayName, stream);
    }

    private sealed class Session : IBatteryDeviceSession
    {
        private readonly HidStream _stream;
        private readonly object _lock = new();

        public string DeviceLabel { get; }

        public Session(string label, HidStream stream)
        {
            DeviceLabel = label;
            _stream = stream;
        }

        public BatteryReading? GetLatest()
        {
            lock (_lock)
            {
                try
                {
                    var request = new byte[CollectionFeatureLength];
                    request[0] = ReportId;
                    request[1] = CommandGetStatus;
                    request[4] = 0x01;
                    _stream.SetFeature(request);

                    Thread.Sleep(90); // matches the vendor tool's own pacing between the request and the read

                    var response = new byte[CollectionFeatureLength];
                    _stream.GetFeature(response);

                    int percent = Math.Clamp((int)response[10], 0, 100);
                    bool charging = response[11] != 0;
                    return new BatteryReading(percent, charging, null);
                }
                catch
                {
                    return null;
                }
            }
        }

        public void Dispose() => _stream.Dispose();
    }
}
