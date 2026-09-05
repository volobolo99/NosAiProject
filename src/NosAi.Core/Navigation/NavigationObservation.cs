namespace NosAi.Core.Navigation;

/// <summary>Evidence produced by navigation planning and verification.</summary>
public readonly record struct NavigationObservation(
    NavigationPoint Start,
    NavigationPoint Goal,
    int PathPointCount,
    int ExpandedNodes,
    bool PathFound,
    bool ReplanRequired,
    DateTime ObservedAtUtc,
    double Confidence,
    string Provenance,
    string? Reason)
{
    public bool IsFresh(TimeSpan maxAge, DateTime nowUtc)
        => nowUtc - ObservedAtUtc <= maxAge;

    public static NavigationObservation Unknown(string reason, DateTime? observedAtUtc = null)
        => new(
            default,
            default,
            0,
            0,
            false,
            false,
            observedAtUtc ?? DateTime.UtcNow,
            0,
            "Unknown",
            reason);
}
