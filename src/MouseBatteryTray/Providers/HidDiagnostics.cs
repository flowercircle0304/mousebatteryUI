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
                sb.AppendLine($"    In={d.GetMaxInputReportLength(),-4} Out={d.GetMaxOutputReportLength(),-4} Feat={d.GetMaxFeatureReportLength(),-4}  {d.DevicePath}");
            }
        }

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
                var reading = session.GetLatest();
                sb.AppendLine(reading is null
                    ? $"    → VID_{g.Key.VendorID:X4}&PID_{g.Key.ProductID:X4}: オープンには成功しましたが、バッテリー値を取得できませんでした"
                    : $"    → VID_{g.Key.VendorID:X4}&PID_{g.Key.ProductID:X4}: {reading.Percent}% (取得成功)");
            }
        }

        return sb.ToString();
    }
}
