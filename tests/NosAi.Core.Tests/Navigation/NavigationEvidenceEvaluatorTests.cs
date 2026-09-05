using NosAi.Core.Navigation;

namespace NosAi.Core.Tests.Navigation;

public sealed class NavigationEvidenceEvaluatorTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void MissingObservation_RemainsUnknown()
    {
        NavigationObservation before = NavigationObservation.Unknown("before_missing", Now);
        NavigationObservation after = NavigationObservation.Unknown("after_missing", Now);

        NavigationObservation result = NavigationEvidenceEvaluator.Evaluate(
            before, after, Now, TimeSpan.FromSeconds(1));

        Assert.False(result.PathFound);
        Assert.Equal(NavigationEvidenceEvaluator.MissingObservationReason, result.Reason);
        Assert.Equal("Unknown", result.Provenance);
    }

    [Fact]
    public void StaleObservation_RemainsUnknown()
    {
        NavigationObservation before = Observation(0, 0, pathFound: true, observedAt: Now.AddSeconds(-2));
        NavigationObservation after = Observation(1, 0, pathFound: true, observedAt: Now);

        NavigationObservation result = NavigationEvidenceEvaluator.Evaluate(
            before, after, Now, TimeSpan.FromSeconds(1));

        Assert.False(result.PathFound);
        Assert.Equal(NavigationEvidenceEvaluator.StaleObservationReason, result.Reason);
    }

    [Fact]
    public void PathWithoutMovementOrReplan_IsNotEvidenceOfExecution()
    {
        NavigationObservation before = Observation(0, 0, pathFound: true, observedAt: Now);
        NavigationObservation after = Observation(0, 0, pathFound: true, observedAt: Now.AddMilliseconds(100));

        NavigationObservation result = NavigationEvidenceEvaluator.Evaluate(
            before, after, Now.AddMilliseconds(100), TimeSpan.FromSeconds(1));

        Assert.False(result.PathFound);
        Assert.Equal(NavigationEvidenceEvaluator.NoMovementOrReplanReason, result.Reason);
    }

    [Fact]
    public void MovementWithPath_ProducesNavigationEvidence()
    {
        NavigationObservation before = Observation(0, 0, pathFound: true, observedAt: Now);
        NavigationObservation after = Observation(1, 0, pathFound: true, observedAt: Now.AddMilliseconds(100));

        NavigationObservation result = NavigationEvidenceEvaluator.Evaluate(
            before, after, Now.AddMilliseconds(100), TimeSpan.FromSeconds(1));

        Assert.True(result.PathFound);
        Assert.Equal("Navigation", result.Provenance);
        Assert.Null(result.Reason);
    }

    private static NavigationObservation Observation(
        float x,
        float y,
        bool pathFound,
        DateTime observedAt) => new(
            new NavigationPoint(x, y),
            new NavigationPoint(x + 5, y + 5),
            PathPointCount: pathFound ? 2 : 0,
            ExpandedNodes: pathFound ? 3 : 0,
            PathFound: pathFound,
            ReplanRequired: false,
            ObservedAtUtc: observedAt,
            Confidence: 0.9,
            Provenance: "Navigation",
            Reason: pathFound ? null : NavigationEvidenceEvaluator.NoPathReason);
}
