using System.Net;
using NosAi.LiveIntegration;

namespace NosAi.ControlPanel;

/// <summary>
/// Fills the observation endpoint from the OS connection table, or refuses to
/// guess when the client has zero or several remote sessions.
/// </summary>
internal static class ObserveGameDetector
{
    /// <summary>
    /// True only when exactly one remote session is identifiable. Zero or several
    /// leave <paramref name="endpoint"/> unset: picking one conversation out of a
    /// crowd would invent which one is the game.
    /// </summary>
    public static bool TrySuggest(ClientNetworkObservation observation, out string endpoint, out string status)
    {
        ArgumentNullException.ThrowIfNull(observation);
        endpoint = "";

        if (!observation.Observed)
        {
            status = $"UNKNOWN · {observation.FailureReason ?? "network_not_observed"}. Casella invariata.";
            return false;
        }

        int remotes = observation.RemoteSessions.Count;
        if (observation.Primary is { } primary)
        {
            endpoint = Format(primary.Remote);
            status = $"Endpoint rilevato: {endpoint} [{primary.State}].";
            return true;
        }

        status = remotes == 0
            ? "0 sessioni remote: casella invariata. Non c'è un server da osservare."
            : $"{remotes} sessioni remote: casella invariata. Sceglierne una sarebbe indovinare quale conversazione è il gioco.";
        return false;
    }

    private static string Format(IPEndPoint remote) => remote.ToString();
}
