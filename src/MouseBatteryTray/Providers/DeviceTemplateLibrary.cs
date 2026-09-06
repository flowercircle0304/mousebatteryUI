using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MouseBatteryTray.Providers;

/// <summary>One entry in the community device-template library (see <see cref="DeviceTemplateLibrary"/>).</summary>
public sealed class DeviceTemplate
{
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";

    /// <summary>"logitech-hidpp", "razer", "sony-inzone-buds", "sprime-pm1", "wlmouse-strider",
    /// "endgame-gear-we", "ninjutso", "finalmouse-ulx", "pulsar", "moddo-mouse", "vgn-f2",
    /// "teevolution", "keychron-nape", or "keychron-m6" today — matches
    /// <see cref="DiscoveredDeviceSpec.Kind"/>.</summary>
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
            RazerTransactionId = 0x3F,
            Verified = false,
            Notes = "openrazerカーネルドライバの仕様を基に実装。実機未検証（無線・有線どちらのPIDも登録済み）。OpenMouseプロジェクトの実機報告によれば、公式ドライバ記載の0xFFではなく0x3Fが正しいトランザクションIDとのことなので修正済み（0x1Fは無応答、0x3Fでファームウェア・DPI・ポーリング・バッテリーが読めたとのこと）。",
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
        // 以下、OpenMouseプロジェクト（github.com/OpenMouse-Project/mouse-protocol、src/razer/devices.ts）
        // が公開しているトランザクションID一覧を基に追加。既存のRazerモデルと同じ標準90バイトプロトコル。
        new()
        {
            Manufacturer = "Razer",
            Model = "DeathAdder Essential",
            Kind = "razer",
            VendorId = 0x1532,
            ProductId = 0x006E,
            AdditionalProductIds = new List<int> { 0x0071, 0x0098 },
            RazerTransactionId = 0x3F,
            Verified = false,
            Notes = "openrazerカーネルドライバの仕様を基に実装。実機未検証（3つのハードウェア版のPIDをすべて登録）。",
        },
        new()
        {
            Manufacturer = "Razer",
            Model = "DeathAdder V4 Pro",
            Kind = "razer",
            VendorId = 0x1532,
            ProductId = 0x00BE,
            AdditionalProductIds = new List<int> { 0x00BF },
            RazerTransactionId = 0x1F,
            Verified = false,
            Notes = "OpenMouseプロジェクトの解析（OpenRazer PR #2508準拠）を基に追加。実機未検証（無線・有線どちらのPIDも登録済み）。",
        },
        new()
        {
            Manufacturer = "Razer",
            Model = "DeathAdder V4 Pro Carbon Fiber Edition",
            Kind = "razer",
            VendorId = 0x1532,
            ProductId = 0x00EF,
            AdditionalProductIds = new List<int> { 0x00F0 },
            RazerTransactionId = 0x1F,
            Verified = false,
            Notes = "DeathAdder V4 Proと同じ電子基板の別色SKU（OpenMouseプロジェクトの解析）。実機未検証。",
        },
        new()
        {
            Manufacturer = "Razer",
            Model = "Viper V3 HyperSpeed",
            Kind = "razer",
            VendorId = 0x1532,
            ProductId = 0x00B8,
            RazerTransactionId = 0x1F,
            Verified = false,
            Notes = "OpenMouseプロジェクトの解析を基に追加。実機未検証。",
        },
        new()
        {
            Manufacturer = "Razer",
            Model = "Viper V3 Pro SE",
            Kind = "razer",
            VendorId = 0x1532,
            ProductId = 0x00DE,
            AdditionalProductIds = new List<int> { 0x00DF },
            RazerTransactionId = 0x1F,
            Verified = false,
            Notes = "OpenMouseプロジェクトの解析（OpenRazer PR #2818準拠）を基に追加。実機未検証（無線・有線どちらのPIDも登録済み）。",
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
        new()
        {
            Manufacturer = "SPRIME",
            Model = "PM1",
            Kind = "sprime-pm1",
            VendorId = 0x1915,
            ProductId = 0xAC1C,
            Verified = false,
            Notes = "SPRIME公式のWeb設定ツール（sprime.pro、WebHID使用）のJSソースに書かれていたコマンドをそのまま移植。ベンダー公式ソース由来ですが、このアプリでの実機検証はまだ済んでいません。",
        },
        new()
        {
            Manufacturer = "WLMouse",
            Model = "Strider",
            Kind = "wlmouse-strider",
            VendorId = 0x36A7,
            ProductId = 0xA872,
            AdditionalProductIds = new List<int> { 0xA873 }, // 有線/BT直結時のPID
            Verified = true,
            Notes = "WLMouse公式のWebハブ（gm.wlmouse.gg、WebHID使用）のJSソースに書かれていたコマンドをそのまま移植。実機で動作確認済み。2.4GHzレシーバー経由（PID 0xA872）と有線/BT直結時（PID 0xA873）の両方を登録（このアプリでは有線/BT直結時のコレクションから取得できています）。",
        },
        // 以下、Striderと同じプロトコル（WLMouseの共通ドングル方式）を使うと見られる兄弟機種。
        // オープンソースのOpenMouseプロジェクト（github.com/OpenMouse-Project/mouse-protocol）が
        // 公開しているVID/PID一覧を基に追加。プロトコル自体はStriderで実機確認済みのものと同一と
        // 見られるが、各機種の実機ではまだ検証できていないため Verified=false。
        new()
        {
            Manufacturer = "WLMouse",
            Model = "Beast G",
            Kind = "wlmouse-strider",
            VendorId = 0x36A7,
            ProductId = 0xA860,
            AdditionalProductIds = new List<int> { 0xA861 },
            Verified = false,
            Notes = "WLMouse Strider（実機検証済み）と同じプロトコルを使うと見られる兄弟機種。OpenMouseプロジェクトが公開しているPID一覧を基に追加。このアプリでの実機検証はまだ済んでいません。",
        },
        new()
        {
            Manufacturer = "WLMouse",
            Model = "Huan",
            Kind = "wlmouse-strider",
            VendorId = 0x36A7,
            ProductId = 0xA863,
            AdditionalProductIds = new List<int> { 0xA864 },
            Verified = false,
            Notes = "WLMouse Strider（実機検証済み）と同じプロトコルを使うと見られる兄弟機種。OpenMouseプロジェクトが公開しているPID一覧を基に追加。このアプリでの実機検証はまだ済んでいません。",
        },
        new()
        {
            Manufacturer = "WLMouse",
            Model = "Beast Miao",
            Kind = "wlmouse-strider",
            VendorId = 0x36A7,
            ProductId = 0xA866,
            AdditionalProductIds = new List<int> { 0xA867 },
            Verified = false,
            Notes = "WLMouse Strider（実機検証済み）と同じプロトコルを使うと見られる兄弟機種。OpenMouseプロジェクトが公開しているPID一覧を基に追加。このアプリでの実機検証はまだ済んでいません。",
        },
        new()
        {
            Manufacturer = "WLMouse",
            Model = "Beast Mini Pro",
            Kind = "wlmouse-strider",
            VendorId = 0x36A7,
            ProductId = 0xA868,
            AdditionalProductIds = new List<int> { 0xA869 },
            Verified = false,
            Notes = "WLMouse Strider（実機検証済み）と同じプロトコルを使うと見られる兄弟機種。OpenMouseプロジェクトが公開しているPID一覧を基に追加。このアプリでの実機検証はまだ済んでいません。",
        },
        new()
        {
            Manufacturer = "WLMouse",
            Model = "Beast X Pro",
            Kind = "wlmouse-strider",
            VendorId = 0x36A7,
            ProductId = 0xA870,
            AdditionalProductIds = new List<int> { 0xA871 },
            Verified = false,
            Notes = "WLMouse Strider（実機検証済み）と同じプロトコルを使うと見られる兄弟機種。OpenMouseプロジェクトが公開しているPID一覧を基に追加。このアプリでの実機検証はまだ済んでいません。",
        },
        new()
        {
            Manufacturer = "WLMouse",
            Model = "Ying",
            Kind = "wlmouse-strider",
            VendorId = 0x36A7,
            ProductId = 0xA874,
            AdditionalProductIds = new List<int> { 0xA875 },
            Verified = false,
            Notes = "WLMouse Strider（実機検証済み）と同じプロトコルを使うと見られる兄弟機種。OpenMouseプロジェクトが公開しているPID一覧を基に追加。このアプリでの実機検証はまだ済んでいません。",
        },
        new()
        {
            Manufacturer = "WLMouse",
            Model = "Sword X",
            Kind = "wlmouse-strider",
            VendorId = 0x36A7,
            ProductId = 0xA878,
            AdditionalProductIds = new List<int> { 0xA879 },
            Verified = false,
            Notes = "WLMouse Strider（実機検証済み）と同じプロトコルを使うと見られる兄弟機種。OpenMouseプロジェクトが公開しているPID一覧を基に追加。このアプリでの実機検証はまだ済んでいません。",
        },
        new()
        {
            Manufacturer = "WLMouse",
            Model = "Beast Max",
            Kind = "wlmouse-strider",
            VendorId = 0x36A7,
            ProductId = 0xA880,
            AdditionalProductIds = new List<int> { 0xA881 },
            Verified = false,
            Notes = "WLMouse Strider（実機検証済み）と同じプロトコルを使うと見られる兄弟機種。OpenMouseプロジェクトが公開しているPID一覧を基に追加。このアプリでの実機検証はまだ済んでいません。",
        },
        new()
        {
            Manufacturer = "WLMouse",
            Model = "Beast X",
            Kind = "wlmouse-strider",
            VendorId = 0x36A7,
            ProductId = 0xA883,
            AdditionalProductIds = new List<int> { 0xA884 },
            Verified = false,
            Notes = "WLMouse Strider（実機検証済み）と同じプロトコルを使うと見られる兄弟機種。OpenMouseプロジェクトが公開しているPID一覧を基に追加。このアプリでの実機検証はまだ済んでいません。",
        },
        new()
        {
            Manufacturer = "WLMouse",
            Model = "Beast Mini",
            Kind = "wlmouse-strider",
            VendorId = 0x36A7,
            ProductId = 0xA885,
            AdditionalProductIds = new List<int> { 0xA886 },
            Verified = false,
            Notes = "WLMouse Strider（実機検証済み）と同じプロトコルを使うと見られる兄弟機種。OpenMouseプロジェクトが公開しているPID一覧を基に追加。このアプリでの実機検証はまだ済んでいません。",
        },
        // Lamzu / CRDRAKO: OpenMouse's own "compx" module doc explicitly says this exact page-command
        // protocol is "Shared page-command framing used by WLMouse and Lamzu receivers" — same Kind,
        // same provider class, different VendorId (0x373E instead of WLMouse's 0x36A7).
        new()
        {
            Manufacturer = "Lamzu",
            Model = "Maya X",
            Kind = "wlmouse-strider",
            VendorId = 0x373E,
            ProductId = 0x001C,
            AdditionalProductIds = new List<int> { 0x001D, 0x001E },
            Verified = false,
            Notes = "WLMouse Strider（実機検証済み）と共通の\"compx\"プロトコル（OpenMouseプロジェクトが文書化）を使うと見られるLamzu製マウス。実機未検証。",
        },
        new()
        {
            Manufacturer = "CRDRAKO",
            Model = "KO-ONE",
            Kind = "wlmouse-strider",
            VendorId = 0x373E,
            ProductId = 0x006A,
            AdditionalProductIds = new List<int> { 0x006B },
            Verified = false,
            Notes = "WLMouse Strider（実機検証済み）と共通の\"compx\"プロトコル（OpenMouseプロジェクトが文書化）を使うと見られるLamzu系OEMマウス。実機未検証。",
        },
        new()
        {
            Manufacturer = "Attack Shark",
            Model = "R5 Ultra",
            Kind = "wlmouse-strider",
            VendorId = 0x373E,
            ProductId = 0x0047,
            AdditionalProductIds = new List<int> { 0x0046 },
            Verified = false,
            Notes = "OpenMouseプロジェクトによれば、Lamzu/CRDRAKOと同じVendorId・同じ\"compx\"プロトコルのOEM機種（WLMouse Strider実機検証済みとバイト単位で同一）。実機未検証。",
        },
        // Glorious "classic" line (pre-Pixart Model O/D/I): OpenMouse's own reverse-engineering
        // (ported from glorious-ctl's mouse.py, cross-confirmed by an unrelated C# implementation,
        // AwesomeTy18/GloriousBatteryMonitor) shows its battery-read command is byte-for-byte
        // IDENTICAL to WLMouse Strider's — same Feature report id 0, same 65-byte buffer, same
        // request bytes (offset 3=0x02, 4=0x02, 6=0x83), same response layout (offset 1=status,
        // 6=echo, 7=charging, 8=percent). Reuses this same provider class with no new code, only a
        // different VendorId. PID pairs from OpenMouse's own vendors.ts GLORIOUS_CLASSIC_PRODUCTS map.
        new()
        {
            Manufacturer = "Glorious",
            Model = "Model O Wireless",
            Kind = "wlmouse-strider",
            VendorId = 0x258A,
            ProductId = 0x2022,
            AdditionalProductIds = new List<int> { 0x2011 },
            Verified = false,
            Notes = "WLMouse Strider（実機検証済み）と完全に同一のプロトコル（バイト単位で一致）を使用。OpenMouseプロジェクトの解析（glorious-ctl由来、別実装のGloriousBatteryMonitorでも裏付け済み）を基に追加。このアプリでの実機検証はまだ済んでいません。",
        },
        new()
        {
            Manufacturer = "Glorious",
            Model = "Model D Wireless",
            Kind = "wlmouse-strider",
            VendorId = 0x258A,
            ProductId = 0x2023,
            AdditionalProductIds = new List<int> { 0x2012 },
            Verified = false,
            Notes = "WLMouse Strider（実機検証済み）と完全に同一のプロトコル（バイト単位で一致）を使用。OpenMouseプロジェクトの解析（glorious-ctl由来、別実装のGloriousBatteryMonitorでも裏付け済み）を基に追加。このアプリでの実機検証はまだ済んでいません。",
        },
        new()
        {
            Manufacturer = "Glorious",
            Model = "Model O Pro",
            Kind = "wlmouse-strider",
            VendorId = 0x258A,
            ProductId = 0x2027,
            AdditionalProductIds = new List<int> { 0x2015 },
            Verified = false,
            Notes = "WLMouse Strider（実機検証済み）と完全に同一のプロトコル（バイト単位で一致）を使用。OpenMouseプロジェクトの解析（glorious-ctl由来、別実装のGloriousBatteryMonitorでも裏付け済み）を基に追加。このアプリでの実機検証はまだ済んでいません。",
        },
        new()
        {
            Manufacturer = "Glorious",
            Model = "Model O 2 Wireless / O2 Pro Wireless",
            Kind = "wlmouse-strider",
            VendorId = 0x258A,
            ProductId = 0x2033,
            Verified = false,
            Notes = "WLMouse Strider（実機検証済み）と完全に同一のプロトコル（バイト単位で一致）を使用。OpenMouseプロジェクトの解析（glorious-ctl由来、別実装のGloriousBatteryMonitorでも裏付け済み）を基に追加。このアプリでの実機検証はまだ済んでいません。",
        },
        new()
        {
            Manufacturer = "Endgame Gear",
            Model = "OP1we / XM2we シリーズ全般",
            Kind = "endgame-gear-we",
            VendorId = 0x3367,
            ProductId = 0,
            Verified = false,
            Notes = "OpenMouseプロジェクトの解析を基に実装（同プロジェクトのWebHIDフィルタもPID非依存・usage page依存のため、型番を問わず対応を試みます）。ATKダングルと共通のコマンド体系（Endgame Gear WEシリーズのEEPROM読み出し方式）をFeatureレポート経由で使用。このアプリでの実機検証はまだ済んでいません。",
        },
        // Ninjutso "current"-generation protocol (NinjaForce's own WebHID panel), per OpenMouse's
        // reverse engineering. OpenMouse's own comment notes hardware verification is still pending
        // even in their implementation; same caveat applies here.
        new()
        {
            Manufacturer = "Ninjutso",
            Model = "Sora V3",
            Kind = "ninjutso",
            VendorId = 0x093A,
            ProductId = 0xE010,
            AdditionalProductIds = new List<int> { 0xEB02 },
            Verified = false,
            Notes = "OpenMouseプロジェクトの解析（NinjaForce公式Webパネルのプロトコル）を基に追加。OpenMouse側でも実機検証はまだ済んでいないとのことです。このアプリでも実機未検証。",
        },
        new()
        {
            Manufacturer = "Ninjutso",
            Model = "TEN / TEN AIR",
            Kind = "ninjutso",
            VendorId = 0x093A,
            ProductId = 0xE020,
            AdditionalProductIds = new List<int> { 0xEA01, 0xEB01 },
            Verified = false,
            Notes = "OpenMouseプロジェクトの解析（NinjaForce公式Webパネルのプロトコル）を基に追加。OpenMouse側でも実機検証はまだ済んでいないとのことです。このアプリでも実機未検証。",
        },
        new()
        {
            Manufacturer = "Finalmouse",
            Model = "UltralightX (Starlight-12 / ULX)",
            Kind = "finalmouse-ulx",
            VendorId = 0x361D,
            ProductId = 0x0100,
            Verified = false,
            Notes = "OpenMouseプロジェクトの解析（公式WebHIDツール\"xpanel\"由来）を基に追加。このアプリでの実機検証はまだ済んでいません。",
        },
        new()
        {
            Manufacturer = "Pulsar",
            Model = "Xlite / X2 シリーズ全般",
            Kind = "pulsar",
            VendorId = 0x3710,
            ProductId = 0,
            Verified = false,
            Notes = "OpenMouseプロジェクトの解析を基に実装（型番非依存・コレクション形状で判定するため、型番を問わず対応を試みます）。X3（別プロトコル）とVGN型番を共有するPulsar 4Kレシーバーは対象外。このアプリでの実機検証はまだ済んでいません。",
        },
        new()
        {
            Manufacturer = "moddoMOUSE",
            Model = "moddoMOUSE",
            Kind = "moddo-mouse",
            VendorId = 0x2FE3,
            ProductId = 0,
            Verified = false,
            Notes = "OpenMouseプロジェクトの解析（公式Webツール\"moddoHUB-Web\"由来）を基に実装。このアプリでの実機検証はまだ済んでいません。",
        },
        new()
        {
            Manufacturer = "VGN",
            Model = "Dragonfly F2 Master+",
            Kind = "vgn-f2",
            VendorId = 0x3554,
            ProductId = 0xFB56,
            AdditionalProductIds = new List<int> { 0xFB57 },
            Verified = false,
            Notes = "OpenMouseプロジェクトの解析を基に実装。このアプリでの実機検証はまだ済んでいません。",
        },
        new()
        {
            Manufacturer = "Teevolution",
            Model = "Terra Pro",
            Kind = "teevolution",
            VendorId = 0x3554,
            ProductId = 0xF520,
            AdditionalProductIds = new List<int> { 0xF523, 0xF5BB, 0xF522 },
            Verified = false,
            Notes = "OpenMouseプロジェクトの解析を基に実装（Pulsarとほぼ同一のコマンド体系）。このアプリでの実機検証はまだ済んでいません。",
        },
        new()
        {
            Manufacturer = "Keychron",
            Model = "Nape Pro",
            Kind = "keychron-nape",
            VendorId = 0x3434,
            ProductId = 0x0440,
            Verified = false,
            Notes = "OpenMouseプロジェクトの解析（VIAベースのraw HIDプロトコル）を基に実装。共有レシーバー（Link-KM）経由での接続は対象外。このアプリでの実機検証はまだ済んでいません。",
        },
        new()
        {
            Manufacturer = "Keychron",
            Model = "M6",
            Kind = "keychron-m6",
            VendorId = 0x3434,
            ProductId = 0xD060,
            AdditionalProductIds = new List<int> { 0xD029 },
            Verified = false,
            Notes = "OpenMouseプロジェクトの解析を基に実装（Nape Proとは別のプロトコル）。有線PIDと、Link-KMレシーバー経由の無線PIDの両方を登録。このアプリでの実機検証はまだ済んでいません。",
        },
    };
}
