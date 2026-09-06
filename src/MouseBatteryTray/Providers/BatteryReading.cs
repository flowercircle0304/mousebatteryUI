namespace MouseBatteryTray.Providers;

/// <summary><paramref name="SubReadings"/> is for a device that's really more than one physical
/// battery reported as one entry (e.g. a pair of earbuds) — <paramref name="Percent"/> stays the
/// single worst-of-the-set value everything else (sorting, notifications, the mini gauge) already
/// keys off, while the popup additionally renders each sub-reading's own label and percent when
/// this is set. <paramref name="Dpi"/>/<paramref name="PollingRateHz"/> are optional device-config
/// info (not battery data at all) that a handful of providers with a request/response config
/// channel already open for battery can read essentially for free — null for every provider that
/// doesn't populate them, which the popup treats as "nothing extra to show".</summary>
public sealed record BatteryReading(int Percent, bool? Charging, int? VoltageMillivolts, IReadOnlyList<(string Label, int Percent)>? SubReadings = null, int? Dpi = null, int? PollingRateHz = null)
{
    public static BatteryReading OfPercent(int percent) => new(percent, null, null);
}
