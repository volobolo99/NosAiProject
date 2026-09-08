using System;
using System.Linq;
using NosAi.Core.WorldModel;
using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;
using NosAi.Runtime.WorldModel.Fusion;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The wire states level and experience in <c>lev</c>, and the decoder has always read them,
/// but nothing carried them into the World Model, so the progression goal could never be
/// scored on anything. These tests pin the route, and that an unstated progression adds no
/// pool at all: a pool whose current and maximum are both Unknown would let a reader believe
/// progress had been observed when nobody looked.
/// </summary>
public sealed class ProgressionProjectionTests
{
    private static readonly DateTime At = DateTime.UnixEpoch;

    [Fact]
    public void ProgressionNeverStated_AddsNoExperiencePool()
    {
        WorldModelSnapshot snapshot = Project(WithProgression(
            ClassifiedValue<PlayerProgression>.Unknown("no_progression_observed")));

        Assert.Null(Pool(snapshot, ResourceKind.Experience));
    }

    [Fact]
    public void StatedProgression_BecomesAnExperiencePool()
    {
        var progression = new PlayerProgression(12, 450, 1000, 3, 20, 100);

        WorldModelSnapshot snapshot = Project(WithProgression(
            ClassifiedValue<PlayerProgression>.Live(progression, At)));

        Resource? pool = Pool(snapshot, ResourceKind.Experience);
        Assert.NotNull(pool);
        Assert.True(pool!.Current.HasValue);
        Assert.Equal(450d, pool!.Current.Value);
        Assert.Equal(1000d, pool!.Maximum.Value);
    }

    [Fact]
    public void HealthAndManaPoolsSurviveTheAddition()
    {
        var progression = new PlayerProgression(12, 450, 1000, 3, 20, 100);

        WorldModelSnapshot snapshot = Project(WithProgression(
            ClassifiedValue<PlayerProgression>.Live(progression, At)));

        Assert.NotNull(Pool(snapshot, ResourceKind.Health));
        Assert.NotNull(Pool(snapshot, ResourceKind.Mana));
    }

    private static Resource? Pool(WorldModelSnapshot snapshot, ResourceKind kind) =>
        snapshot.Player.Status.Resources.FirstOrDefault(resource => resource.Kind == kind);

    private static WorldModelSnapshot Project(GameplayObservation observation) =>
        GameplayObservationProjector.Project(observation, new EntityId("player-1"), version: 1, At);

    private static GameplayObservation WithProgression(ClassifiedValue<PlayerProgression> progression) =>
        new(
            ClassifiedValue<int>.Derived(100, At),
            ClassifiedValue<int>.Derived(100, At),
            ClassifiedValue<int>.Derived(50, At),
            ClassifiedValue<int>.Derived(50, At),
            ClassifiedValue<bool>.Derived(false, At),
            ClassifiedValue<bool>.Derived(false, At),
            ClassifiedValue<int>.Derived(0, At),
            At)
        {
            Progression = progression,
        };
}
