namespace NosAi.Core.Navigation;

/// <summary>Deterministically evaluates whether a navigation observation proves movement/replanning.</summary>
public static class NavigationEvidenceEvaluator
{
    public const string MissingObservationReason = "navigation_observation_missing";
    public const string StaleObservationReason = "navigation_observation_stale";
    public const string NoPathReason = "navigation_path_not_found";
    public const string NoMovementOrReplanReason = "navigation_no_movement_or_replan_evidence";

    public static NavigationObservation Evaluate(
        NavigationObservation before,
        NavigationObservation after,
        DateTime nowUtc,
        TimeSpan maxAge)
    {
        if (before.ObservedAtUtc == default || after.ObservedAtUtc == default)
            return NavigationObservation.Unknown(MissingObservationReason, nowUtc);

        if (!before.IsFresh(maxAge, nowUtc) || !after.IsFresh(maxAge, nowUtc))
            return NavigationObservation.Unknown(StaleObservationReason, nowUtc);

        if (!after.PathFound)
            return NavigationObservation.Unknown(NoPathReason, after.ObservedAtUtc);

        bool moved = before.Start != after.Start;
        bool replanned = after.ReplanRequired;
        if (!moved && !replanned)
            return NavigationObservation.Unknown(NoMovementOrReplanReason, after.ObservedAtUtc);

        return after with
        {
            ReplanRequired = replanned,
            Provenance = "Navigation",
            Confidence = Math.Clamp(after.Confidence, 0d, 1d),
            Reason = null
        };
    }
}
