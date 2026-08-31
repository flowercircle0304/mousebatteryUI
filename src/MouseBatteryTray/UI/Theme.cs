using System.Drawing;

namespace MouseBatteryTray.UI;

internal static class Theme
{
    // Base HUD palette — dark navy canvas with cyan/violet neon accents.
    public static readonly Color Background = Color.FromArgb(255, 8, 12, 20);
    public static readonly Color PanelBackground = Color.FromArgb(255, 13, 19, 30);
    public static readonly Color CardBackground = Color.FromArgb(255, 17, 25, 38);
    public static readonly Color Border = Color.FromArgb(255, 34, 48, 66);

    public static readonly Color TextPrimary = Color.FromArgb(255, 226, 240, 255);
    public static readonly Color TextMuted = Color.FromArgb(255, 116, 134, 160);

    public static readonly Color AccentCyan = Color.FromArgb(255, 34, 211, 238);
    public static readonly Color AccentViolet = Color.FromArgb(255, 167, 139, 250);

    public static readonly Color LevelHigh = Color.FromArgb(255, 57, 255, 176);
    public static readonly Color LevelMid = Color.FromArgb(255, 255, 176, 32);
    public static readonly Color LevelLow = Color.FromArgb(255, 255, 59, 107);
    public static readonly Color LevelUnknown = Color.FromArgb(255, 108, 122, 140);

    public static Color LevelColor(int? percent) => percent switch
    {
        null => LevelUnknown,
        <= 15 => LevelLow,
        <= 35 => LevelMid,
        _ => LevelHigh,
    };
}
