using HidSharp;
using MouseBatteryTray.Providers;
using Timer = System.Threading.Timer;

namespace MouseBatteryTray;

/// <summary>
/// Owns the lifecycle of connected mouse-battery HID sessions. Scanning and device I/O both run
/// on background timers; callers (the UI) only ever read a cached, thread-safe snapshot via
/// <see cref="GetReadings"/>, so nothing here needs to touch the UI thread.
/// </summary>
public sealed class DeviceManager : IDisposable
{
    public sealed record DeviceStatus(string ProviderId, string Label, BatteryReading? Reading, TimeSpan? EstimatedTimeRemaining);

    private sealed record ActiveDevice(string ProviderId, IBatteryDeviceSession Session);

    private readonly object _lock = new();
    private readonly Dictionary<string, ActiveDevice> _active = new();
    private readonly Dictionary<string, DeviceStatus> _cache = new();
    private readonly Dictionary<string, List<(DateTime Time, int Percent)>> _history = new();
    private volatile AppSettings _settings;

    private readonly Timer _scanTimer;
    private readonly Timer _pollTimer;

    public DeviceManager(AppSettings settings)
    {
        _settings = settings;
        _scanTimer = new Timer(_ => SafeRun(Scan), null, 0, 10_000);
        _pollTimer = new Timer(_ => SafeRun(Poll), null, 2_000, 15_000);
    }

    /// <summary>Call after settings change so a just-disabled device is dropped and a just-enabled one is picked up immediately, instead of waiting for the next scan tick.</summary>
    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        SafeRun(Scan);
    }

    private static void SafeRun(Action action)
    {
        try { action(); } catch { /* background timer callback: never let this crash the app */ }
    }

    private void Scan()
    {
        var settings = _settings;
        var providers = ProviderRegistry.BuildAll(settings);
        var groups = DeviceList.Local.GetHidDevices()
            .GroupBy(d => (d.VendorID, d.ProductID))
            .ToList();

        var seenKeys = new HashSet<string>();

        foreach (var group in groups)
        {
            var provider = providers.FirstOrDefault(p => p.OwnsVendorProduct(group.Key.VendorID, group.Key.ProductID));
            if (provider is null) continue;

            string key = $"{group.Key.VendorID:X4}:{group.Key.ProductID:X4}";

            if (!settings.IsEnabled(provider.Id))
            {
                // Explicitly disabled in settings: make sure it's not (still) open, and don't count
                // it as "seen" so it never lingers in the cache either.
                lock (_lock)
                {
                    if (_active.Remove(key, out var disabledDev)) disabledDev.Session.Dispose();
                    _cache.Remove(key);
                    _history.Remove(key);
                }
                continue;
            }

            seenKeys.Add(key);

            lock (_lock)
            {
                if (_active.ContainsKey(key)) continue;
            }

            var session = provider.TryOpen(group.ToList());
            if (session is null) continue;

            lock (_lock)
            {
                _active[key] = new ActiveDevice(provider.Id, session);
                _cache[key] = new DeviceStatus(provider.Id, session.DeviceLabel, null, null);
            }
        }

        List<string> disappeared;
        lock (_lock)
        {
            disappeared = _active.Keys.Where(k => !seenKeys.Contains(k)).ToList();
        }

        foreach (var key in disappeared)
        {
            lock (_lock)
            {
                if (_active.Remove(key, out var dev)) dev.Session.Dispose();
                _cache.Remove(key);
                _history.Remove(key);
            }
        }
    }

    private void Poll()
    {
        List<(string Key, ActiveDevice Device)> snapshot;
        lock (_lock) snapshot = _active.Select(kv => (kv.Key, kv.Value)).ToList();

        foreach (var (key, dev) in snapshot)
        {
            var reading = dev.Session.GetLatest();
            var estimate = reading is null ? null : UpdateHistoryAndEstimate(key, reading.Percent);
            lock (_lock)
            {
                if (_active.ContainsKey(key))
                    _cache[key] = new DeviceStatus(dev.ProviderId, dev.Session.DeviceLabel, reading, estimate);
            }
        }
    }

    /// <summary>
    /// Tracks battery% over time per device and, once there's enough of a stable downward trend,
    /// estimates how long is left before it hits 0%. Returns null while there isn't enough history
    /// yet or the trend isn't clearly decreasing (e.g. flat, or actually charging).
    /// </summary>
    private TimeSpan? UpdateHistoryAndEstimate(string key, int percent)
    {
        lock (_lock)
        {
            if (!_history.TryGetValue(key, out var samples))
            {
                samples = new List<(DateTime, int)>();
                _history[key] = samples;
            }

            var now = DateTime.UtcNow;
            if (samples.Count == 0 || samples[^1].Percent != percent)
                samples.Add((now, percent));

            samples.RemoveAll(s => now - s.Time > TimeSpan.FromHours(12));
            if (samples.Count > 20) samples.RemoveRange(0, samples.Count - 20);

            if (samples.Count < 2) return null;

            var first = samples[0];
            var last = samples[^1];
            double elapsedHours = (last.Time - first.Time).TotalHours;
            if (elapsedHours < 0.33) return null; // not enough spread yet for a stable slope

            double percentPerHour = (last.Percent - first.Percent) / elapsedHours;
            if (percentPerHour >= -0.05) return null; // flat or increasing (charging) — no meaningful estimate

            double hoursRemaining = last.Percent / -percentPerHour;
            if (hoursRemaining is < 0.1 or > 24 * 30) return null; // sanity guard against wild extrapolation

            return TimeSpan.FromHours(hoursRemaining);
        }
    }

    /// <summary>Sorted lowest-battery-first (most urgent first); devices still awaiting their first reading come last.</summary>
    public IReadOnlyList<DeviceStatus> GetReadings()
    {
        lock (_lock)
        {
            return _cache.Values
                .OrderBy(s => s.Reading?.Percent ?? int.MaxValue)
                .ThenBy(s => s.Label, StringComparer.Ordinal)
                .ToList();
        }
    }

    public void Dispose()
    {
        _scanTimer.Dispose();
        _pollTimer.Dispose();
        lock (_lock)
        {
            foreach (var dev in _active.Values) dev.Session.Dispose();
            _active.Clear();
            _cache.Clear();
            _history.Clear();
        }
    }
}
