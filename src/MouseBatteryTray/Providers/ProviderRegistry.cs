namespace MouseBatteryTray.Providers;

/// <summary>
/// How to add a new mouse:
///
///  1. Try the in-app "新しいマウスを追加" wizard (Settings) first — it passively listens for a
///     pushed battery report, and can also probe for the COMPX request/response protocol, purely
///     by matching raw bytes against a percentage you type in. No code, no AI, works offline.
///  2. If the wizard can't find it, the protocol needs manual investigation: plug in the receiver
///     and run HidProbe (src/HidProbe) to find its VID/PID and enumerate its HID collections.
///     Check whether the vendor's own app is Electron-based (resources/app.asar can be extracted)
///     or native (may need a USBPcap capture while the vendor app reads the battery).
///  3a. Another COMPX-family dongle (17-byte in/out reports) — add a <see cref="CompxDongleProvider"/>
///      entry to <see cref="BuiltIn"/> below.
///  3b. A receiver that pushes an unsolicited fixed-size report with the battery byte at a fixed
///      offset (like FURYCUBE) — add a <see cref="PassivePushHidProvider"/> entry below.
///  3c. Anything else — implement <see cref="IMouseBatteryProvider"/> in this folder and add an
///      instance here. Nothing else in the app needs to change.
/// </summary>
public static class ProviderRegistry
{
    public static readonly IReadOnlyList<IMouseBatteryProvider> BuiltIn = new IMouseBatteryProvider[]
    {
        new CompxDongleProvider(
            id: "atk-compx",
            displayName: "ATK 8K Dongle (COMPX)",
            vendorId: 0x373B,
            // 4145 (0x1031) "ATK 8K Dongle Light Version" is confirmed on real hardware. The rest
            // come from ATK HUB's own device table (same COMPX controller class) and are
            // believed-compatible but unverified — safe to try since GetBatteryLevel is read-only.
            productIds: new[] { 4145, 4122, 4123, 4160, 4378, 4379, 4441, 4446, 4519, 4521, 4552, 4557, 4558 }),

        new PassivePushHidProvider(
            id: "furycube-f1",
            displayName: "FURYCUBE F1",
            vendorId: 0x1D57,
            productId: 0xFA60,
            reportLength: 5,
            batteryByteOffset: 4),
    };

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
