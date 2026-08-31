namespace MouseBatteryTray.Providers;

public sealed record BatteryReading(int Percent, bool? Charging, int? VoltageMillivolts)
{
    public static BatteryReading OfPercent(int percent) => new(percent, null, null);
}
