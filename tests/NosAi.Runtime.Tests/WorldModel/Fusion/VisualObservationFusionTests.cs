using NosAi.Core.WorldModel;
using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception;
using NosAi.Runtime.WorldModel.Fusion;
using Xunit;

namespace NosAi.Runtime.Tests.WorldModel.Fusion;

public sealed class VisualObservationFusionTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly EntityId PlayerId = new("player-1");

    private static WorldModelSnapshot NetworkOnlySnapshot(ClassifiedValue<int> hp, ClassifiedValue<int> maxHp)
    {
        GameplayObservation observation = GameplayObservation.Unobserved("reason", Now) with { Hp = hp, MaxHp = maxHp };
        return GameplayObservationProjector.Project(observation, PlayerId, version: 1, Now);
    }

    private static VisualObservation VisualWithHp(ClassifiedValue<int> current, ClassifiedValue<int> maximum)
    {
        VisualObservation baseline = VisualObservation.Unobserved("no_reading", Now);
        var hp = new ScreenVitalPair(current, maximum, Confidence: 0.9, FailureReason: null);
        return baseline with { Vitals = baseline.Vitals with { Hp = hp } };
    }

    [Fact]
    public void NetworkLive_BeatsScreenDerived_EvenWhenBothPresent()
    {
        WorldModelSnapshot snapshot = NetworkOnlySnapshot(ClassifiedValue<int>.Live(80, Now), ClassifiedValue<int>.Live(100, Now));
        VisualObservation visual = VisualWithHp(ClassifiedValue<int>.Derived(50, Now), ClassifiedValue<int>.Derived(100, Now));

        WorldModelSnapshot fused = VisualObservationFusion.FuseVitals(snapshot, visual, Now);

        Resource health = Assert.Single(fused.Player.Status.Resources, r => r.Kind == ResourceKind.Health);
        Assert.Equal(80d, health.Current.Value);
    }

    [Fact]
    public void NetworkUnknown_ScreenDerivedWins_AsTheOnlyUsableCandidate()
    {
        WorldModelSnapshot snapshot = NetworkOnlySnapshot(ClassifiedValue<int>.Unknown("player_vitals_not_seen_yet"), ClassifiedValue<int>.Unknown("player_vitals_not_seen_yet"));
        VisualObservation visual = VisualWithHp(ClassifiedValue<int>.Derived(50, Now), ClassifiedValue<int>.Derived(100, Now));

        WorldModelSnapshot fused = VisualObservationFusion.FuseVitals(snapshot, visual, Now);

        Resource health = Assert.Single(fused.Player.Status.Resources, r => r.Kind == ResourceKind.Health);
        Assert.True(health.Current.HasValue);
        Assert.Equal(50d, health.Current.Value);
        Assert.Equal(NosAi.Core.WorldModel.DataSourceKind.Derived, health.Current.Source);
    }

    [Fact]
    public void NetworkCached_ScreenDerivedWins_BecauseDerivedOutranksCached()
    {
        // A stale-but-republished network reading (CACHED) ranks below a
        // fresh screen-derived one -- this is the concrete case that makes
        // fusing a second channel worth doing at all.
        WorldModelSnapshot snapshot = NetworkOnlySnapshot(ClassifiedValue<int>.Cached(70, Now - TimeSpan.FromSeconds(3)), ClassifiedValue<int>.Cached(100, Now - TimeSpan.FromSeconds(3)));
        VisualObservation visual = VisualWithHp(ClassifiedValue<int>.Derived(65, Now), ClassifiedValue<int>.Derived(100, Now));

        WorldModelSnapshot fused = VisualObservationFusion.FuseVitals(snapshot, visual, Now);

        Resource health = Assert.Single(fused.Player.Status.Resources, r => r.Kind == ResourceKind.Health);
        Assert.Equal(65d, health.Current.Value);
    }

    [Fact]
    public void BothChannelsUnknown_ResultIsUnknown_NeverAFabricatedZero()
    {
        WorldModelSnapshot snapshot = NetworkOnlySnapshot(ClassifiedValue<int>.Unknown("reason"), ClassifiedValue<int>.Unknown("reason"));
        VisualObservation visual = VisualObservation.Unobserved("reason", Now);

        WorldModelSnapshot fused = VisualObservationFusion.FuseVitals(snapshot, visual, Now);

        Resource health = Assert.Single(fused.Player.Status.Resources, r => r.Kind == ResourceKind.Health);
        Assert.False(health.Current.HasValue);
    }

    [Fact]
    public void FusionStartingFromAnEmptySnapshot_StillAddsHealthAndMana()
    {
        WorldModelSnapshot empty = WorldModelSnapshot.Unknown("bootstrap");
        VisualObservation visual = VisualWithHp(ClassifiedValue<int>.Derived(10, Now), ClassifiedValue<int>.Derived(20, Now));

        WorldModelSnapshot fused = VisualObservationFusion.FuseVitals(empty, visual, Now);

        Assert.Equal(2, fused.Player.Status.Resources.Count);
        Resource health = Assert.Single(fused.Player.Status.Resources, r => r.Kind == ResourceKind.Health);
        Assert.Equal(10d, health.Current.Value);
    }

    [Fact]
    public void FusionOnlyTouchesResources_EverythingElseOnPlayerIsUnchanged()
    {
        WorldModelSnapshot snapshot = NetworkOnlySnapshot(ClassifiedValue<int>.Live(80, Now), ClassifiedValue<int>.Live(100, Now)) with
        {
            Player = NetworkOnlySnapshot(ClassifiedValue<int>.Live(80, Now), ClassifiedValue<int>.Live(100, Now)).Player with
            {
                Position = WorldFact<WorldPosition>.Live(new WorldPosition(3, 4), 1.0, Now)
            }
        };
        VisualObservation visual = VisualObservation.Unobserved("reason", Now);

        WorldModelSnapshot fused = VisualObservationFusion.FuseVitals(snapshot, visual, Now);

        Assert.Equal(snapshot.Player.Position, fused.Player.Position);
        Assert.Equal(snapshot.Player.Id, fused.Player.Id);
        Assert.Equal(snapshot.Map, fused.Map);
    }

    [Fact]
    public void FusionIsDeterministic_SameInputsProduceEqualOutput()
    {
        WorldModelSnapshot snapshot = NetworkOnlySnapshot(ClassifiedValue<int>.Live(80, Now), ClassifiedValue<int>.Live(100, Now));
        VisualObservation visual = VisualWithHp(ClassifiedValue<int>.Derived(50, Now), ClassifiedValue<int>.Derived(100, Now));

        WorldModelSnapshot a = VisualObservationFusion.FuseVitals(snapshot, visual, Now);
        WorldModelSnapshot b = VisualObservationFusion.FuseVitals(snapshot, visual, Now);

        Assert.Equal(a, b);
    }
}
