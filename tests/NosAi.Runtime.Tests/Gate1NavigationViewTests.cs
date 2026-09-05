using NosAi.Core.Navigation;
using NosAi.Runtime.Gate1;
using Xunit;

namespace NosAi.Runtime.Tests;

public sealed class Gate1NavigationViewTests
{
    [Fact]
    public void UnknownView_IsFailClosed()
    {
        var view = Gate1NavigationView.Unknown();

        Assert.False(view.PathFound.Value);
        Assert.Equal(0, view.Confidence.Value);
        Assert.Equal("Unknown", view.PathFound.Classification.ToString());
        Assert.Equal("navigation_observation_not_available", view.PathFound.Reason);
    }

    [Fact]
    public void FromObservation_PreservesEvidenceAndProvenance()
    {
        DateTime observedAt = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);
        var observation = new NavigationObservation(
            new NavigationPoint(10, 20),
            new NavigationPoint(30, 40),
            PathPointCount: 7,
            ExpandedNodes: 18,
            PathFound: true,
            ReplanRequired: true,
            ObservedAtUtc: observedAt,
            Confidence: 0.91,
            Provenance: "Network",
            Reason: "target_changed");

        var view = Gate1NavigationView.From(observation);

        Assert.Equal(10, view.StartX.Value);
        Assert.Equal(40, view.GoalY.Value);
        Assert.Equal(7, view.PathPointCount.Value);
        Assert.Equal(18, view.ExpandedNodes.Value);
        Assert.True(view.PathFound.Value);
        Assert.True(view.ReplanRequired.Value);
        Assert.Equal(0.91, view.Confidence.Value, 3);
        Assert.Equal("Network", view.Provenance.Value);
        Assert.Equal("target_changed", view.Reason.Value);
        Assert.Equal(observedAt, view.ObservedAtUtc.Value);
    }
}
