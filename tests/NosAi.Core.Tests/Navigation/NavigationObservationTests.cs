using NosAi.Core.Navigation;

namespace NosAi.Core.Tests.Navigation;

public sealed class NavigationObservationTests
{
    [Fact]
    public void UnknownObservation_IsExplicitlyUnknown()
    {
        var observation = NavigationObservation.Unknown("navigation_world_not_observed");

        Assert.False(observation.PathFound);
        Assert.Equal(0, observation.Confidence);
        Assert.Equal("Unknown", observation.Provenance);
        Assert.Equal("navigation_world_not_observed", observation.Reason);
    }

    [Fact]
    public void Observation_IsFreshWithinConfiguredAge()
    {
        var now = DateTime.UtcNow;
        var observation = new NavigationObservation(
            new NavigationPoint(1, 2),
            new NavigationPoint(4, 5),
            4,
            8,
            true,
            false,
            now.AddMilliseconds(-100),
            0.9,
            "Screen",
            null);

        Assert.True(observation.IsFresh(TimeSpan.FromMilliseconds(250), now));
        Assert.False(observation.IsFresh(TimeSpan.FromMilliseconds(50), now));
    }
}
