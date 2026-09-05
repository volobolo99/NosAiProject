using NosAi.Core.Navigation;

namespace NosAi.Runtime.Gate1;

/// <summary>
/// Canonical dashboard representation of navigation evidence.
/// This type contains only evidence already produced by the runtime navigation layer.
/// It never synthesizes a path, position, movement or replan result.
/// </summary>
public sealed record Gate1NavigationView(
    ClassifiedValue<float> StartX,
    ClassifiedValue<float> StartY,
    ClassifiedValue<float> GoalX,
    ClassifiedValue<float> GoalY,
    ClassifiedValue<int> PathPointCount,
    ClassifiedValue<int> ExpandedNodes,
    ClassifiedValue<bool> PathFound,
    ClassifiedValue<bool> ReplanRequired,
    ClassifiedValue<DateTime?> ObservedAtUtc,
    ClassifiedValue<double> Confidence,
    ClassifiedValue<string> Provenance,
    ClassifiedValue<string?> Reason)
{
    public const string UnknownReason = "navigation_observation_not_available";

    public object ToWire() => new
    {
        start = new { x = StartX.ToWire(), y = StartY.ToWire() },
        goal = new { x = GoalX.ToWire(), y = GoalY.ToWire() },
        pathPointCount = PathPointCount.ToWire(),
        expandedNodes = ExpandedNodes.ToWire(),
        pathFound = PathFound.ToWire(),
        replanRequired = ReplanRequired.ToWire(),
        observedAtUtc = ObservedAtUtc.ToWire(),
        confidence = Confidence.ToWire(),
        provenance = Provenance.ToWire(),
        reason = Reason.ToWire()
    };

    public static Gate1NavigationView Unknown(string? reason = null)
    {
        string actualReason = string.IsNullOrWhiteSpace(reason) ? UnknownReason : reason;
        return new(
            ClassifiedValue<float>.Unknown(actualReason),
            ClassifiedValue<float>.Unknown(actualReason),
            ClassifiedValue<float>.Unknown(actualReason),
            ClassifiedValue<float>.Unknown(actualReason),
            ClassifiedValue<int>.Unknown(actualReason),
            ClassifiedValue<int>.Unknown(actualReason),
            ClassifiedValue<bool>.Unknown(actualReason),
            ClassifiedValue<bool>.Unknown(actualReason),
            ClassifiedValue<DateTime?>.Unknown(actualReason),
            ClassifiedValue<double>.Unknown(actualReason),
            ClassifiedValue<string>.Unknown(actualReason),
            ClassifiedValue<string?>.Unknown(actualReason));
    }

    public static Gate1NavigationView From(NavigationObservation observation)
    {
        DateTime observedAt = observation.ObservedAtUtc;
        return new(
            ClassifiedValue<float>.Derived(observation.Start.X, observedAt),
            ClassifiedValue<float>.Derived(observation.Start.Y, observedAt),
            ClassifiedValue<float>.Derived(observation.Goal.X, observedAt),
            ClassifiedValue<float>.Derived(observation.Goal.Y, observedAt),
            ClassifiedValue<int>.Derived(observation.PathPointCount, observedAt),
            ClassifiedValue<int>.Derived(observation.ExpandedNodes, observedAt),
            ClassifiedValue<bool>.Derived(observation.PathFound, observedAt),
            ClassifiedValue<bool>.Derived(observation.ReplanRequired, observedAt),
            ClassifiedValue<DateTime?>.Derived(observedAt, observedAt),
            ClassifiedValue<double>.Derived(observation.Confidence, observedAt),
            ClassifiedValue<string>.Derived(observation.Provenance, observedAt),
            observation.Reason is null
                ? ClassifiedValue<string?>.Unknown("no_navigation_reason")
                : ClassifiedValue<string?>.Derived(observation.Reason, observedAt));
    }
}
