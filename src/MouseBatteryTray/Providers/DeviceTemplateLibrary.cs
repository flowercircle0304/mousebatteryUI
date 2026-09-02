using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MouseBatteryTray.Providers;

/// <summary>One entry in the community device-template library (see <see cref="DeviceTemplateLibrary"/>).</summary>
public sealed class DeviceTemplate
{
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";

    /// <summary>"logitech-hidpp", "razer", or "sony-inzone-buds" today — matches <see cref="DiscoveredDeviceSpec.Kind"/>.</summary>
    public string Kind { get; set; } = "";
    public int VendorId { get; set; }
    public int ProductId { get; set; }
    public int RazerTransactionId { get; set; } = 0x1F;

    /// <summary>Other product ids that are the same physical mouse under a different USB identity —
    /// e.g. many Razer mice switch to a distinct wired-mode PID the moment the charging cable is
    /// plugged in, so without this here the device looks like it disappeared while charging.</summary>
    public List<int> AdditionalProductIds { get; set; } = new();

    /// <summary>Whether this entry has actually been confirmed against real hardware by someone.
    /// Everything shipped with the app starts false — see the notes on each provider class.</summary>
    public bool Verified { get; set; }
    public string Notes { get; set; } = "";
}

/// <summary>
/// A small, growing library of known (VID/PID/protocol) device configurations that use a
/// documented, generalizable protocol (Logitech HID++, Razer's feature-report protocol, Sony
/// INZONE Buds' passive push) — as opposed to the per-device wizard, which is for protocols nobody
/// has documented yet. Not limited to mice — the underlying discovery/provider architecture only
/// cares about "USB 2.4GHz receiver exposing HID reports", not what kind of peripheral it is.
///
/// Hosted at templates/devices.json in this project's repo so it can grow via community PRs
/// without needing an app update; fetched at runtime with a small bundled fallback for offline use
/// or before the remote file exists. None of this touches the wizard's fully-offline, no-network
/// discovery path — this is purely an opt-in convenience for well-known protocols.
/// </summary>
public static class DeviceTemplateLibrary
{
    private const string RemoteUrl = "https://raw.githubusercontent.com/flowercircle0304/mousebatteryUI/main/templates/devices.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<IReadOnlyList<DeviceTemplate>> LoadAsync(CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MouseBatteryTray-TemplateFetch");
            var json = await http.GetStringAsync(RemoteUrl, ct);
            var file = JsonSerializer.Deserialize<TemplateFile>(json, JsonOptions);
            if (file?.Devices is { Count: > 0 } devices) return devices;
        }
        catch
        {
            // Offline, rate-limited, or the file doesn't exist yet — fall back below.
        }
        return Bundled;
    }

    private sealed class TemplateFile
    {
        public List<DeviceTemplate> Devices { get; set; } = new();
    }

    /// <summary>Bundled so the template picker still works offline, or on the very first run before
    /// the remote file has propagated. Keep this in sync with templates/devices.json in the repo
    /// root when adding entries there.</summary>
    private static readonly DeviceTemplate[] Bundled =
    {
        new()
        {
            Manufacturer = "Logitech",
            Model = "HID++ 2.0 対応レシーバー全般 (Unifying / LIGHTSPEED / Bolt)",
            Kind = "logitech-hidpp",
            VendorId = 0x046D,
            ProductId = 0,
            Verified = true,
            Notes = "公開仕様（Solaar / logitray プロジェクト）を基に実装。実機で動作確認済み。レシーバーの型番を問わず、HID++2.0対応マウス全般に対応を試みます。",
        },
        new()
        {
            Manufacturer = "Razer",
            Model = "Viper V3 Pro",
            Kind = "razer",
            VendorId = 0x1532,
            ProductId = 0x00C1,
            AdditionalProductIds = new List<int> { 0x00C0 }, // wired-mode PID (used while the charging cable is plugged in)
            RazerTransactionId = 0x1F,
            Verified = true,
            Notes = "openrazerカーネルドライバの仕様を基に実装。実機で動作確認済み（無線・有線どちらのPIDも登録、充電中の切り替えに対応）。",
        },
        new()
        {
            Manufacturer = "Razer",
            Model = "DeathAdder V3 Pro",
            Kind = "razer",
            VendorId = 0x1532,
            ProductId = 0x00B7,
            AdditionalProductIds = new List<int> { 0x00B6 },
            RazerTransactionId = 0x1F,
            Verified = false,
            Notes = "openrazerカーネルドライバの仕様を基に実装。実機未検証（無線・有線どちらのPIDも登録済み）。",
        },
        new()
        {
            Manufacturer = "Razer",
            Model = "Basilisk V3 Pro",
            Kind = "razer",
            VendorId = 0x1532,
            ProductId = 0x00AB,
            AdditionalProductIds = new List<int> { 0x00AA },
            RazerTransactionId = 0x1F,
            Verified = false,
            Notes = "openrazerカーネルドライバの仕様を基に実装。実機未検証（無線・有線どちらのPIDも登録済み）。",
        },
        new()
        {
            Manufacturer = "Razer",
            Model = "Viper Ultimate",
            Kind = "razer",
            VendorId = 0x1532,
            ProductId = 0x007B,
            AdditionalProductIds = new List<int> { 0x007A },
            RazerTransactionId = 0xFF,
            Verified = false,
            Notes = "openrazerカーネルドライバの仕様を基に実装。実機未検証（無線・有線どちらのPIDも登録済み）。",
        },
        new()
        {
            Manufacturer = "Razer",
            Model = "DeathAdder V2 Pro",
            Kind = "razer",
            VendorId = 0x1532,
            ProductId = 0x007D,
            AdditionalProductIds = new List<int> { 0x007C },
            RazerTransactionId = 0x3F,
            Verified = false,
            Notes = "openrazerカーネルドライバの仕様を基に実装。実機未検証（無線・有線どちらのPIDも登録済み）。",
        },
        new()
        {
            Manufacturer = "Sony",
            Model = "INZONE Buds (WF-G700N)",
            Kind = "sony-inzone-buds",
            VendorId = 0x054C,
            ProductId = 0x0EC2,
            Verified = false,
            Notes = "マウスではなくワイヤレスイヤホンですが、同じUSB 2.4GHzドングル方式のため対応。HeadsetControlプロジェクト（github.com/Sapd/HeadsetControl）の実装を基に移植。実機未検証。",
        },
    };
}
