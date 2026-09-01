using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MouseBatteryTray.Providers;

/// <summary>One entry in the community device-template library (see <see cref="DeviceTemplateLibrary"/>).</summary>
public sealed class DeviceTemplate
{
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";

    /// <summary>"logitech-hidpp" or "razer" today — matches <see cref="DiscoveredDeviceSpec.Kind"/>.</summary>
    public string Kind { get; set; } = "";
    public int VendorId { get; set; }
    public int ProductId { get; set; }
    public int RazerTransactionId { get; set; } = 0x1F;

    /// <summary>Whether this entry has actually been confirmed against real hardware by someone.
    /// Everything shipped with the app starts false — see the notes on each provider class.</summary>
    public bool Verified { get; set; }
    public string Notes { get; set; } = "";
}

/// <summary>
/// A small, growing library of known (VID/PID/protocol) configurations for mice that use a
/// documented, generalizable protocol (Logitech HID++, Razer's feature-report protocol) — as
/// opposed to the per-device wizard, which is for protocols nobody has documented yet.
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
            Model = "Viper V3 Pro (Wireless)",
            Kind = "razer",
            VendorId = 0x1532,
            ProductId = 0x00C1,
            RazerTransactionId = 0x1F,
            Verified = false,
            Notes = "openrazerカーネルドライバの仕様を基に実装。実機未検証。",
        },
        new()
        {
            Manufacturer = "Razer",
            Model = "Viper V3 Pro (Wired)",
            Kind = "razer",
            VendorId = 0x1532,
            ProductId = 0x00C0,
            RazerTransactionId = 0x1F,
            Verified = false,
            Notes = "openrazerカーネルドライバの仕様を基に実装。実機未検証。",
        },
        new()
        {
            Manufacturer = "Razer",
            Model = "DeathAdder V3 Pro (Wireless)",
            Kind = "razer",
            VendorId = 0x1532,
            ProductId = 0x00B7,
            RazerTransactionId = 0x1F,
            Verified = false,
            Notes = "openrazerカーネルドライバの仕様を基に実装。実機未検証。",
        },
        new()
        {
            Manufacturer = "Razer",
            Model = "Basilisk V3 Pro (Wireless)",
            Kind = "razer",
            VendorId = 0x1532,
            ProductId = 0x00AB,
            RazerTransactionId = 0x1F,
            Verified = false,
            Notes = "openrazerカーネルドライバの仕様を基に実装。実機未検証。",
        },
        new()
        {
            Manufacturer = "Razer",
            Model = "Viper Ultimate (Wireless)",
            Kind = "razer",
            VendorId = 0x1532,
            ProductId = 0x007B,
            RazerTransactionId = 0xFF,
            Verified = false,
            Notes = "openrazerカーネルドライバの仕様を基に実装。実機未検証。",
        },
        new()
        {
            Manufacturer = "Razer",
            Model = "DeathAdder V2 Pro (Wireless)",
            Kind = "razer",
            VendorId = 0x1532,
            ProductId = 0x007D,
            RazerTransactionId = 0x3F,
            Verified = false,
            Notes = "openrazerカーネルドライバの仕様を基に実装。実機未検証。",
        },
    };
}
