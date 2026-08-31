using HidSharp;

namespace MouseBatteryTray.Providers;

/// <summary>
/// A provider knows how to talk to one family of wireless mouse receivers that share the same
/// vendor id / wire protocol. To support a new mouse, add a new provider implementing this
/// interface and register it in <see cref="ProviderRegistry"/> — nothing else needs to change.
/// </summary>
public interface IMouseBatteryProvider
{
    /// <summary>Stable machine-readable key (never shown to the user) used to persist per-device settings.</summary>
    string Id { get; }

    string DisplayName { get; }

    bool OwnsVendorProduct(int vendorId, int productId);

    /// <summary>
    /// <paramref name="collections"/> contains every HID top-level collection exposed by the
    /// receiver (one physical USB receiver is usually split into several HidSharp "devices",
    /// one per collection/interface). The provider inspects them (by report lengths, usage
    /// page/usage, etc.) to find the one it needs and opens a session on it.
    /// Returns null if none of the collections match what this provider expects.
    /// </summary>
    IBatteryDeviceSession? TryOpen(IReadOnlyList<HidDevice> collections);
}
