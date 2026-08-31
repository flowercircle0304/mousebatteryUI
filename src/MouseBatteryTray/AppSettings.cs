using System.Text.Json;
using System.Text.Json.Serialization;

namespace MouseBatteryTray;

public sealed class DeviceSetting
{
    public bool Enabled { get; set; } = true;

    /// <summary>Path to the companion app's .exe, or a URL, launched when the device's card is clicked. Empty = no link configured.</summary>
    public string CompanionPath { get; set; } = "";

    /// <summary>
    /// True if the user "deleted" this entry from the settings list. Built-in providers can't
    /// actually be removed from the app, so deleting one just hides its row (and stops monitoring
    /// it) — useful when sharing this app with someone who doesn't own that mouse. Reversible via
    /// "非表示にしたマウスを再表示".
    /// </summary>
    public bool Hidden { get; set; } = false;
}

/// <summary>
/// A mouse/receiver found and configured at runtime by the "add a mouse" wizard (see
/// Providers/DeviceDiscovery.cs), persisted so it survives restarts without a rebuild.
/// </summary>
public sealed class DiscoveredDeviceSpec
{
    /// <summary>"passive-push" or "compx" — see <see cref="Providers.ProviderRegistry.BuildAll"/>.</summary>
    public string Kind { get; set; } = "";
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int VendorId { get; set; }
    public int ProductId { get; set; }

    // passive-push
    public int ReportLength { get; set; }
    public int BatteryByteOffset { get; set; }

    // compx
    public int OutputReportId { get; set; } = 8;
    public int CommandId { get; set; } = 4;
}

public sealed class AppSettings
{
    public Dictionary<string, DeviceSetting> Devices { get; set; } = new();
    public List<DiscoveredDeviceSpec> DiscoveredDevices { get; set; } = new();

    public bool LowBatteryNotificationsEnabled { get; set; } = true;
    public int LowBatteryThreshold { get; set; } = 20;

    /// <summary>When true, the popup stays open (ignores click-away) at <see cref="PopupPinnedX"/>/<see cref="PopupPinnedY"/> instead of near the tray icon.</summary>
    public bool PopupPinned { get; set; } = false;
    public int? PopupPinnedX { get; set; }
    public int? PopupPinnedY { get; set; }

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MouseBatteryTray", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public DeviceSetting GetOrCreate(string providerId)
    {
        if (!Devices.TryGetValue(providerId, out var setting))
        {
            setting = new DeviceSetting();
            Devices[providerId] = setting;
        }
        return setting;
    }

    public bool IsEnabled(string providerId) =>
        !Devices.TryGetValue(providerId, out var s) || (s.Enabled && !s.Hidden); // default: enabled, not hidden

    public bool IsHidden(string providerId) =>
        Devices.TryGetValue(providerId, out var s) && s.Hidden;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded is not null) return loaded;
            }
        }
        catch
        {
            // Corrupt or unreadable settings file: fall back to defaults rather than crash the app.
        }
        return new AppSettings();
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(this, JsonOptions);
        File.WriteAllText(FilePath, json);
    }
}
