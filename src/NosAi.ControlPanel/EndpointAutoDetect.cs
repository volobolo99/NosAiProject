using System;

namespace NosAi.ControlPanel;

/// <summary>
/// Where the current observation-endpoint text comes from. The automatism may
/// only write over an empty box or over a value it detected itself: hand-typed
/// and settings-loaded values are protected provenances.
/// </summary>
internal enum EndpointOrigin
{
    None,
    Detected,
    TypedByOperator,
    FromSettings,
}

/// <summary>
/// Pure decision logic for the self-detecting observation endpoint. Decides
/// whether to detect and what the provenance line must say; it never touches
/// WPF, the network or the disk.
/// </summary>
internal static class EndpointAutoDetect
{
    /// <summary>
    /// True only when the client process is known, the current text is not a
    /// hand-written value, and either the box is empty or the process id is no
    /// longer the one the current value was detected for. False in every other
    /// case, including "same process, endpoint already present", so that the
    /// detection does not restart on every clock tick.
    /// </summary>
    public static bool ShouldDetect(
        bool clientPidKnown,
        int? currentPid,
        int? lastDetectedForPid,
        string currentText,
        EndpointOrigin origin)
    {
        if (!clientPidKnown || origin == EndpointOrigin.TypedByOperator)
            return false;

        if (!string.IsNullOrWhiteSpace(currentText))
        {
            if (!currentPid.HasValue
                || !lastDetectedForPid.HasValue
                || currentPid.Value == lastDetectedForPid.Value)
                return false;
        }

        return true;
    }

    /// <summary>
    /// The line that tells the operator where the endpoint value comes from and
    /// since when. Never returns an empty line: an absent client with a reason
    /// reports the reason instead of staying silent.
    /// </summary>
    public static string DescribeOrigin(
        EndpointOrigin origin,
        string endpoint,
        DateTime? whenUtc,
        DateTime nowUtc,
        string? clientFailureReason)
    {
        switch (origin)
        {
            case EndpointOrigin.None when !string.IsNullOrWhiteSpace(clientFailureReason):
                return $"Nessun endpoint rilevato · {clientFailureReason.Trim()}";
            case EndpointOrigin.None:
                return "Nessun endpoint noto.";
            case EndpointOrigin.Detected:
                string age = DescribeAge(whenUtc ?? nowUtc, nowUtc);
                return string.IsNullOrWhiteSpace(endpoint)
                    ? $"Rilevato dal client · {age}"
                    : $"Rilevato dal client: {endpoint.Trim()} · {age}";
            case EndpointOrigin.TypedByOperator:
                return "Scritto a mano · l'automatismo non lo tocca";
            case EndpointOrigin.FromSettings:
                return "Dalle impostazioni salvate";
            default:
                return "Nessun endpoint noto.";
        }
    }

    /// <summary>
    /// What an edit to the box means for the provenance. An emptied box loses
    /// any protection; a changed non-blank value that is not the automatism's
    /// own write can only have been typed by the operator; a change that leaves
    /// the text identical keeps its origin.
    /// </summary>
    public static EndpointOrigin OriginAfterEdit(EndpointOrigin previous, string oldText, string newText)
    {
        if (string.IsNullOrWhiteSpace(newText))
            return EndpointOrigin.None;

        if (string.Equals(oldText, newText, StringComparison.Ordinal))
            return previous;

        return EndpointOrigin.TypedByOperator;
    }

    /// <summary>Seconds under a minute, then minutes, then hours.</summary>
    private static string DescribeAge(DateTime whenUtc, DateTime nowUtc)
    {
        TimeSpan age = nowUtc - whenUtc;
        if (age < TimeSpan.Zero)
            age = TimeSpan.Zero;

        if (age.TotalSeconds < 60)
        {
            int seconds = (int)Math.Floor(age.TotalSeconds);
            return seconds == 1 ? "1 secondo fa" : $"{seconds} secondi fa";
        }

        if (age.TotalMinutes < 60)
        {
            int minutes = (int)Math.Floor(age.TotalMinutes);
            return minutes == 1 ? "1 minuto fa" : $"{minutes} minuti fa";
        }

        int hours = (int)Math.Floor(age.TotalHours);
        return hours == 1 ? "1 ora fa" : $"{hours} ore fa";
    }
}
