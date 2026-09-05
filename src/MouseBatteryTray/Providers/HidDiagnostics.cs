using System.Text;
using HidSharp;

namespace MouseBatteryTray.Providers;

/// <summary>
/// Produces a human-shareable text dump of every connected HID device and how this app's
/// providers currently see them — for a user to send back when "it doesn't work" and the
/// maintainer has no way to test against their exact hardware. This is exactly the kind of
/// information (VID/PID, report lengths per collection) that this whole project's own device
/// support was originally reverse-engineered from; making it a built-in, one-click feature turns
/// that from a one-off investigation into a repeatable troubleshooting tool.
/// </summary>
public static class HidDiagnostics
{
    public static string BuildReport(AppSettings settings)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Mouse Battery Tray — 診断情報 / Diagnostics");
        sb.AppendLine($"生成日時 / Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine(new string('=', 60));

        sb.AppendLine();
        sb.AppendLine("## 接続中のHIDデバイス一覧 / Connected HID devices");
        sb.AppendLine("(このアプリが対応を試みる可能性のある、すべてのHID機器のコレクション単位の一覧です)");
        sb.AppendLine();

        var groups = DeviceList.Local.GetHidDevices()
            .GroupBy(d => (d.VendorID, d.ProductID))
            .OrderBy(g => g.Key.VendorID).ThenBy(g => g.Key.ProductID);

        foreach (var group in groups)
        {
            string name = "";
            try { name = group.First().GetProductName(); } catch { }
            sb.AppendLine($"VID_{group.Key.VendorID:X4}&PID_{group.Key.ProductID:X4}  \"{name}\"");
            foreach (var d in group)
            {
                string openResult = TryOpenRaw(d);
                sb.AppendLine($"    In={d.GetMaxInputReportLength(),-4} Out={d.GetMaxOutputReportLength(),-4} Feat={d.GetMaxFeatureReportLength(),-4} Open=[{openResult}]  {d.DevicePath}");
            }
        }

        sb.AppendLine();
        sb.AppendLine(new string('=', 60));
        sb.AppendLine("## 関連しそうな常駐プロセス / Possibly-related running processes");
        sb.AppendLine("(見つかった場合、これらのソフトがデバイスを排他的に掴んでいて読み取りを妨げていることがあります。可能なら終了してから再試行してください)");
        sb.AppendLine();

        string[] keywords = { "razer", "synapse", "logi", "ghub", "g hub", "atk", "furycube", "corsair", "icue", "steelseries", "roccat", "sprime", "pm1", "inzone", "sony" };
        var suspicious = System.Diagnostics.Process.GetProcesses()
            .Where(p => keywords.Any(k => p.ProcessName.Contains(k, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.ProcessName)
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        sb.AppendLine(suspicious.Count == 0 ? "(該当なし)" : string.Join(", ", suspicious));

        sb.AppendLine();
        sb.AppendLine(new string('=', 60));
        sb.AppendLine("## 登録済みプロバイダーの動作状況 / Registered provider status");
        sb.AppendLine("(現在このアプリに設定されている各マウス用ドライバが、実際に開けるかどうかの結果です)");
        sb.AppendLine();

        foreach (var provider in ProviderRegistry.BuildAll(settings))
        {
            sb.AppendLine($"- {provider.DisplayName} (id={provider.Id})");
            var matchingGroups = groups.Where(g => provider.OwnsVendorProduct(g.Key.VendorID, g.Key.ProductID)).ToList();
            if (matchingGroups.Count == 0)
            {
                sb.AppendLine("    → 一致するVID/PIDのデバイスが見つかりません (VID/PIDが違うか、未接続の可能性)");
                continue;
            }
            foreach (var g in matchingGroups)
            {
                using var session = provider.TryOpen(g.ToList());
                if (session is null)
                {
                    sb.AppendLine($"    → VID_{g.Key.VendorID:X4}&PID_{g.Key.ProductID:X4}: 一致するコレクションが見つからないか、オープンに失敗しました");
                    continue;
                }
                // Give the session a moment before giving up: a passive-push provider's background
                // listener thread hasn't necessarily received its first spontaneous report yet the
                // instant TryOpen returns (its doc comment says "every few seconds" for FURYCUBE),
                // and a request/response provider's own internal retries take real wall-clock time
                // too (e.g. waiting out a sleeping wireless mouse) — checking GetLatest() exactly
                // once, immediately, used to read as "broken" for a device that just hadn't answered
                // yet.
                BatteryReading? reading = session.GetLatest();
                for (int attempt = 0; reading is null && attempt < 3; attempt++)
                {
                    Thread.Sleep(2000);
                    reading = session.GetLatest();
                }
                sb.AppendLine(reading is null
                    ? $"    → VID_{g.Key.VendorID:X4}&PID_{g.Key.ProductID:X4}: オープンには成功しましたが、バッテリー値を取得できませんでした"
                    : $"    → VID_{g.Key.VendorID:X4}&PID_{g.Key.ProductID:X4}: {reading.Percent}% (取得成功)");

                // The exception a provider's own GetLatest() hits is swallowed by design (so one
                // bad read doesn't disrupt the whole poll cycle) — for a device whose protocol was
                // ported from a vendor source rather than reverse-engineered from real hardware
                // here, that's exactly the detail needed to tell "wrong offset" apart from "wrong
                // request shape" apart from "device rejected it outright".
                // Shown regardless of success — a "successful" read can still be wrong (e.g. an
                // offset landing on a genuinely-zero byte reads as a plausible-looking 0%), and the
                // raw bytes are what let that be told apart from an actually-empty response.
                if (provider is SprimePM1Provider)
                    sb.AppendLine($"        デバッグ（生の送受信バイト列）: {SprimePM1Provider.DebugRawExchange(g.ToList())}");
            }
        }

        return sb.ToString();
    }

    /// <summary>Attempts to actually open (and immediately close) one collection, first the normal
    /// HidSharp way and then via the reduced-access fallback in <see cref="RawHidFeatureIo"/> (the
    /// same one <see cref="RazerProvider"/> relies on for collections Windows protects from
    /// read/write access, e.g. a mouse's primary usage). Reporting both separately is what makes a
    /// "can't open" report distinguishable between a protocol mismatch, a competing-reader problem
    /// like the one seen with ATK HUB, and this reduced-access case.</summary>
    private static string TryOpenRaw(HidDevice device)
    {
        try
        {
            using var stream = device.Open();
            return "OK";
        }
        catch (Exception ex)
        {
            var handle = RawHidFeatureIo.Open(device.DevicePath);
            if (handle is not null)
            {
                handle.Dispose();
                return $"{ex.GetType().Name}: {ex.Message} (ただしFeature専用の縮小アクセスでは開けます / but opens via reduced-access Feature-only fallback)";
            }
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }
}
