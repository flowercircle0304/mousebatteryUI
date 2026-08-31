namespace MouseBatteryTray.Providers;

/// <summary>
/// This app ships with no mice pre-configured — <see cref="BuiltIn"/> is intentionally empty so
/// the distributed .exe doesn't hardcode (or reveal) any specific maintainer's hardware. Every
/// mouse, including the ones this project was originally reverse-engineered against, is added the
/// same way: via the in-app "新しいマウスを追加" wizard (Settings), which saves it to your own
/// local settings.json as a <see cref="DiscoveredDeviceSpec"/> — see <see cref="BuildAll"/>.
///
/// How to add a new mouse:
///
///  1. Try the wizard first — it passively listens for a pushed battery report, and can also
///     probe for the COMPX request/response protocol, purely by matching raw bytes against a
///     percentage you type in. No code, no AI, works offline.
///  2. If the wizard can't find it, the protocol needs manual investigation: plug in the receiver
///     and run HidProbe (src/HidProbe) to find its VID/PID and enumerate its HID collections.
///     Check whether the vendor's own app is Electron-based (resources/app.asar can be extracted)
///     or native (may need a USBPcap capture while the vendor app reads the battery).
///  3a. Another COMPX-family dongle (17-byte in/out reports) — the wizard's active probe covers
///      this already; <see cref="CompxDongleProvider"/> is also usable directly if you're adding
///      support in code instead (e.g. for a fork with its own pre-configured defaults).
///  3b. A receiver that pushes an unsolicited fixed-size report with the battery byte at a fixed
///      offset — same, via <see cref="PassivePushHidProvider"/>.
///  3c. Anything else — implement <see cref="IMouseBatteryProvider"/> in this folder.
/// </summary>
public static class ProviderRegistry
{
    public static readonly IReadOnlyList<IMouseBatteryProvider> BuiltIn = Array.Empty<IMouseBatteryProvider>();

    /// <summary>Built-in providers plus anything the "add a mouse" wizard has discovered and saved.</summary>
    public static IReadOnlyList<IMouseBatteryProvider> BuildAll(AppSettings settings)
    {
        var list = new List<IMouseBatteryProvider>(BuiltIn);
        foreach (var d in settings.DiscoveredDevices)
        {
            IMouseBatteryProvider? provider = d.Kind switch
            {
                "passive-push" => new PassivePushHidProvider(d.Id, d.DisplayName, d.VendorId, d.ProductId, d.ReportLength, d.BatteryByteOffset),
                "compx" => new CompxDongleProvider(d.Id, d.DisplayName, d.VendorId, new[] { d.ProductId }, (byte)d.OutputReportId, (byte)d.CommandId),
                _ => null,
            };
            if (provider is not null) list.Add(provider);
        }
        return list;
    }
}
