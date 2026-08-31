namespace MouseBatteryTray.Providers;

public interface IBatteryDeviceSession : IDisposable
{
    string DeviceLabel { get; }

    /// <summary>
    /// Returns the most recent battery reading. Implementations decide internally whether this
    /// actively queries the device (request/response protocols) or returns a cached value updated
    /// by a background listener (push/telemetry protocols). May return null if no reading is
    /// available yet or the device stopped responding.
    /// </summary>
    BatteryReading? GetLatest();
}
