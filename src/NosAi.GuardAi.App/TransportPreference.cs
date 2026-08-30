namespace NosAi.GuardAi.App;

/// <summary>How the app reaches the runtime.</summary>
public enum GuardTransport
{
    /// <summary>USB cable. The app dials loopback and <c>adb reverse</c> carries it to the PC.</summary>
    Usb,

    /// <summary>Wi-Fi. The runtime is found on the LAN by discovery; no address is typed.</summary>
    WiFi
}

/// <summary>
/// The operator's transport choice, remembered between launches.
/// </summary>
/// <remarks>
/// Only the choice is stored. No address, no port and no key: an address the
/// operator once used is stale the moment the network changes, and a remembered
/// one that no longer resolves is worse than discovering the right one each time.
/// </remarks>
public static class TransportPreference
{
    private const string Key = "guard.transport";

    /// <summary>USB by default: it is the path that works before any network is set up.</summary>
    public const GuardTransport Default = GuardTransport.Usb;

    public static GuardTransport Load()
    {
        try
        {
            var stored = Preferences.Default.Get(Key, Default.ToString());
            return Enum.TryParse<GuardTransport>(stored, ignoreCase: true, out var parsed) ? parsed : Default;
        }
        catch (Exception)
        {
            // Preferences can be unavailable very early in startup. A default is
            // always better than refusing to show the screen.
            return Default;
        }
    }

    public static void Save(GuardTransport transport)
    {
        try
        {
            Preferences.Default.Set(Key, transport.ToString());
        }
        catch (Exception)
        {
            // Not persisting a preference is a lost convenience, never a reason to
            // interrupt a session the operator is trying to open.
        }
    }
}
