using NosAi.LiveIntegration;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Navigation;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Perception.Fusion;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// AP-01 sensor fusion: precedence, disagreement, freshness and UNKNOWN
/// preservation across Network, Memory, Screen and Local.
/// </summary>
public sealed class SensorFusionTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Empty_channels_stay_unknown_and_never_become_zero_or_false()
    {
        FusedWorldObservation fused = DeterministicSensorFusion.Fuse(SensorObservationSet.Empty, Now);

        Assert.False(fused.Hp.HasValue);
        Assert.Equal(DeterministicSensorFusion.SensorNotObservedReason, fused.Hp.Value.FailureReason);
        Assert.False(fused.HasTarget.HasValue);
        Assert.False(fused.PlayerPosition.HasValue);
        Assert.False(fused.Entities.HasValue);
        Assert.False(fused.NavigationPathFound.HasValue);
        Assert.Empty(fused.Disagreements);
        Assert.Equal(0, fused.Hp.Value.Value);
        Assert.False(fused.HasTarget.Value.Value);
    }

    [Fact]
    public void Network_live_vitals_are_published_as_live()
    {
        FusedWorldObservation fused = DeterministicSensorFusion.Fuse(
            SensorNormalizer.Bundle(NetworkVitals(7218, 7305, 1362), null, null, null),
            Now);

        Assert.True(fused.Hp.HasValue);
        Assert.Equal(7218, fused.Hp.Value.Value);
        Assert.Equal(DataSourceKind.Live, fused.Hp.Value.Source);
        Assert.Equal(SensorKind.Network, fused.Hp.Winner);
        Assert.Equal(Now.AddSeconds(-1), fused.Hp.Value.ObservedAtUtc);
        Assert.True(fused.Hp.IsFresh);
        Assert.Equal(7305, fused.MaxHp.Value.Value);
        Assert.Equal(1362, fused.Mp.Value.Value);
    }

    [Fact]
    public void Screen_vitals_that_agree_with_the_wire_leave_the_wire_as_winner()
    {
        ScreenWorldReading screen = SensorNormalizer.FromScreen(
            DerivedPair(7218, 7305),
            DerivedPair(1362, 1420),
            hasTarget: null,
            projectedPlayerPosition: null,
            lastPlayerAttackAtUtc: null);

        FusedWorldObservation fused = DeterministicSensorFusion.Fuse(
            SensorNormalizer.Bundle(NetworkVitals(7218, 7305, 1362), null, screen, null),
            Now);

        Assert.Equal(7218, fused.Hp.Value.Value);
        Assert.Equal(DataSourceKind.Live, fused.Hp.Value.Source);
        Assert.Equal(SensorKind.Network, fused.Hp.Winner);
        Assert.Empty(fused.Disagreements);
    }

    [Fact]
    public void Disagreement_on_hp_is_unknown_and_is_recorded()
    {
        ScreenWorldReading screen = SensorNormalizer.FromScreen(
            DerivedPair(100, 7305),
            null,
            null,
            null,
            null);

        FusedWorldObservation fused = DeterministicSensorFusion.Fuse(
            SensorNormalizer.Bundle(NetworkVitals(7218, 7305, 1362), null, screen, null),
            Now);

        Assert.False(fused.Hp.HasValue);
        Assert.Equal(DeterministicSensorFusion.SameRankDisagreementReason, fused.Hp.Value.FailureReason);
        Assert.Contains(fused.Disagreements, d =>
            d.Field == FusionField.Hp
            && d.LeftSensor == SensorKind.Network
            && d.RightSensor == SensorKind.Screen
            && d.Reason == DeterministicSensorFusion.SameRankDisagreementReason);
        Assert.Equal(7305, fused.MaxHp.Value.Value);
    }

    [Fact]
    public void A_missing_hp_is_not_fabricated_as_zero()
    {
        GameplayObservation network = GameplayObservation.Unobserved("nothing_decoded", Now);
        FusedWorldObservation fused = DeterministicSensorFusion.Fuse(
            SensorNormalizer.Bundle(network, null, null, null),
            Now);

        Assert.False(fused.Hp.HasValue);
        Assert.Equal("nothing_decoded", fused.Hp.Value.FailureReason);
        WorldModelProjection projection = SensorFusionWorldAdapter.TryToWorldState(fused);
        Assert.False(projection.Succeeded);
        Assert.Equal("nothing_decoded", projection.FailureReason);
    }

    [Fact]
    public void Stale_network_vitals_are_unknown_with_the_published_bound()
    {
        DateTime observed = Now - NetworkGameplayProvider.DefaultMaxVitalsAge - TimeSpan.FromMilliseconds(1);
        FusedWorldObservation fused = DeterministicSensorFusion.Fuse(
            SensorNormalizer.Bundle(NetworkVitals(7218, 7305, 1362, observed), null, null, null),
            Now);

        Assert.False(fused.Hp.HasValue);
        Assert.StartsWith(DeterministicSensorFusion.FusedStalePrefix, fused.Hp.Value.FailureReason);
        Assert.False(fused.Hp.IsFresh);
    }

    [Fact]
    public void Cached_vitals_inside_the_published_bound_stay_usable()
    {
        DateTime observed = Now - TimeSpan.FromSeconds(2);
        GameplayObservation network = new(
            Classify(7218, DataSourceKind.Cached, observed),
            Classify(7305, DataSourceKind.Cached, observed),
            Classify(1362, DataSourceKind.Cached, observed),
            ClassifiedValue<int>.Unknown("max_mp_not_mapped"),
            ClassifiedValue<bool>.Unknown("target_flag_not_mapped"),
            ClassifiedValue<bool>.Unknown("combat_flag_not_mapped"),
            ClassifiedValue<int>.Unknown("no_entities_reported"),
            Now);

        FusedWorldObservation fused = DeterministicSensorFusion.Fuse(
            SensorNormalizer.Bundle(network, null, null, null),
            Now);

        Assert.True(fused.Hp.HasValue);
        Assert.Equal(DataSourceKind.Cached, fused.Hp.Value.Source);
        Assert.Equal(observed, fused.Hp.Value.ObservedAtUtc);
    }

    [Fact]
    public void A_future_stamp_is_rejected_by_name()
    {
        FusedWorldObservation fused = DeterministicSensorFusion.Fuse(
            SensorNormalizer.Bundle(NetworkVitals(7218, 7305, 1362, Now.AddSeconds(2)), null, null, null),
            Now);

        Assert.False(fused.Hp.HasValue);
        Assert.Equal(DeterministicSensorFusion.FutureObservationReason, fused.Hp.Value.FailureReason);
    }

    [Fact]
    public void Simulated_vitals_are_ignored_when_a_live_reading_exists()
    {
        GameplayObservation simulated = new(
            ClassifiedValue<int>.Simulated(1, Now.AddSeconds(-1)),
            ClassifiedValue<int>.Simulated(1, Now.AddSeconds(-1)),
            ClassifiedValue<int>.Simulated(1, Now.AddSeconds(-1)),
            ClassifiedValue<int>.Unknown("max_mp_not_mapped"),
            ClassifiedValue<bool>.Unknown("target_flag_not_mapped"),
            ClassifiedValue<bool>.Unknown("combat_flag_not_mapped"),
            ClassifiedValue<int>.Unknown("no_entities_reported"),
            Now);

        FusedWorldObservation fused = DeterministicSensorFusion.Fuse(
            SensorNormalizer.Bundle(NetworkVitals(7218, 7305, 1362), simulated, null, null),
            Now);

        Assert.Equal(7218, fused.Hp.Value.Value);
        Assert.Equal(DataSourceKind.Live, fused.Hp.Value.Source);
        Assert.Equal(SensorKind.Network, fused.Hp.Winner);
    }

    [Fact]
    public void Only_simulated_vitals_stay_simulated_and_are_not_projected()
    {
        GameplayObservation simulated = new(
            ClassifiedValue<int>.Simulated(800, Now.AddSeconds(-1)),
            ClassifiedValue<int>.Simulated(1000, Now.AddSeconds(-1)),
            ClassifiedValue<int>.Simulated(100, Now.AddSeconds(-1)),
            ClassifiedValue<int>.Unknown("max_mp_not_mapped"),
            ClassifiedValue<bool>.Unknown("target_flag_not_mapped"),
            ClassifiedValue<bool>.Unknown("combat_flag_not_mapped"),
            ClassifiedValue<int>.Unknown("no_entities_reported"),
            Now)
        {
            Entities = ClassifiedValue<IReadOnlyList<SelectableEntity>>.Simulated(
                Array.Empty<SelectableEntity>(), Now.AddSeconds(-1)),
        };

        FusedWorldObservation fused = DeterministicSensorFusion.Fuse(
            SensorNormalizer.Bundle(null, simulated, null, null),
            Now);

        Assert.True(fused.Hp.HasValue);
        Assert.Equal(DataSourceKind.Simulated, fused.Hp.Value.Source);
        WorldModelProjection projection = SensorFusionWorldAdapter.TryToWorldState(fused);
        Assert.False(projection.Succeeded);
        Assert.Equal(SensorFusionWorldAdapter.SimulatedNotProjectedReason, projection.FailureReason);
    }

    [Fact]
    public void Memory_has_target_establishes_the_flag()
    {
        GameplayObservation memory = GameplayObservation.Unobserved("memory_channel", Now) with
        {
            HasTarget = SensorNormalizer.HasTargetFromPointer(
                new TargetPointerReading(new IntPtr(1), new IntPtr(2), null, null),
                Now.AddSeconds(-1)),
        };

        FusedWorldObservation fused = DeterministicSensorFusion.Fuse(
            SensorNormalizer.Bundle(null, memory, null, null),
            Now);

        Assert.True(fused.HasTarget.HasValue);
        Assert.True(fused.HasTarget.Value.Value);
        Assert.Equal(DataSourceKind.Derived, fused.HasTarget.Value.Source);
        Assert.Equal(SensorKind.Memory, fused.HasTarget.Winner);
    }

    [Fact]
    public void Screen_has_target_uses_the_existing_composer()
    {
        ScreenWorldReading screen = SensorNormalizer.FromScreenFrame(
            Calibrated(),
            new TargetFrameObservation(
                new TargetFrameReading(TargetFrameState.Present, null, 0.9, null),
                Now.AddSeconds(-1)),
            lastPlayerAttackAtUtc: null);

        FusedWorldObservation fused = DeterministicSensorFusion.Fuse(
            SensorNormalizer.Bundle(null, null, screen, null),
            Now);

        Assert.True(fused.HasTarget.HasValue);
        Assert.True(fused.HasTarget.Value.Value);
        Assert.Equal(DataSourceKind.Derived, fused.HasTarget.Value.Source);
        Assert.Equal(SensorKind.Screen, fused.HasTarget.Winner);
    }

    [Fact]
    public void Memory_and_screen_that_disagree_on_has_target_are_unknown()
    {
        GameplayObservation memory = GameplayObservation.Unobserved("memory_channel", Now) with
        {
            HasTarget = ClassifiedValue<bool>.Derived(true, Now.AddSeconds(-1)),
        };
        ScreenWorldReading screen = SensorNormalizer.FromScreen(
            null,
            null,
            ClassifiedValue<bool>.Derived(false, Now.AddSeconds(-1)),
            null,
            null);

        FusedWorldObservation fused = DeterministicSensorFusion.Fuse(
            SensorNormalizer.Bundle(null, memory, screen, null),
            Now);

        Assert.False(fused.HasTarget.HasValue);
        Assert.Equal(DeterministicSensorFusion.SameRankDisagreementReason, fused.HasTarget.Value.FailureReason);
        Assert.Contains(fused.Disagreements, d => d.Field == FusionField.HasTarget);
    }

    [Fact]
    public void Network_alone_never_establishes_has_target()
    {
        GameplayObservation network = NetworkVitals(7218, 7305, 1362) with
        {
            HasTarget = ClassifiedValue<bool>.Live(true, Now.AddSeconds(-1)),
        };

        FusedWorldObservation fused = DeterministicSensorFusion.Fuse(
            SensorNormalizer.Bundle(network, null, null, null),
            Now);

        Assert.False(fused.HasTarget.HasValue);
        Assert.Equal(
            DeterministicSensorFusion.NetworkDoesNotEstablishHasTargetReason,
            fused.HasTarget.Value.FailureReason);
    }

    [Fact]
    public void A_hit_after_an_absent_screen_reading_is_the_composer_disagreement()
    {
        DateTime screenAt = Now.AddSeconds(-1);
        ScreenWorldReading screen = SensorNormalizer.FromScreenFrame(
            Calibrated(),
            new TargetFrameObservation(
                new TargetFrameReading(TargetFrameState.Absent, null, 0.9, null),
                screenAt),
            lastPlayerAttackAtUtc: screenAt.AddMilliseconds(200));

        FusedWorldObservation fused = DeterministicSensorFusion.Fuse(
            SensorNormalizer.Bundle(null, null, screen, null),
            Now);

        Assert.False(fused.HasTarget.HasValue);
        Assert.Equal(TargetStateComposer.SourcesDisagreeReason, fused.HasTarget.Value.FailureReason);
    }

    [Fact]
    public void A_ct_newer_than_a_memory_no_is_a_disagreement()
    {
        DateTime noAt = Now.AddSeconds(-2);
        GameplayObservation memory = GameplayObservation.Unobserved("memory_channel", Now) with
        {
            HasTarget = ClassifiedValue<bool>.Derived(false, noAt),
        };
        GameplayObservation network = NetworkVitals(7218, 7305, 1362) with
        {
            SelectedTarget = ClassifiedValue<TargetedEntity>.Live(
                new TargetedEntity(44, 3), Now.AddSeconds(-1)),
        };

        FusedWorldObservation fused = DeterministicSensorFusion.Fuse(
            SensorNormalizer.Bundle(network, memory, null, null),
            Now);

        Assert.False(fused.HasTarget.HasValue);
        Assert.Equal(TargetStateComposer.SourcesDisagreeReason, fused.HasTarget.Value.FailureReason);
    }

    [Fact]
    public void Memory_fills_position_when_the_wire_has_none()
    {
        GameplayObservation memory = GameplayObservation.Unobserved("memory_channel", Now) with
        {
            PlayerPosition = ClassifiedValue<MapPoint>.Live(new MapPoint(12, 34), Now.AddSeconds(-1)),
        };

        FusedWorldObservation fused = DeterministicSensorFusion.Fuse(
            SensorNormalizer.Bundle(NetworkVitals(7218, 7305, 1362), memory, null, null),
            Now);

        Assert.True(fused.PlayerPosition.HasValue);
        Assert.Equal(new MapPoint(12, 34), fused.PlayerPosition.Value.Value);
        Assert.Equal(SensorKind.Memory, fused.PlayerPosition.Winner);
        Assert.Equal(DataSourceKind.Live, fused.PlayerPosition.Value.Source);
    }

    [Fact]
    public void Two_different_positions_are_unknown()
    {
        GameplayObservation memory = GameplayObservation.Unobserved("memory_channel", Now) with
        {
            PlayerPosition = ClassifiedValue<MapPoint>.Live(new MapPoint(1, 1), Now.AddSeconds(-1)),
        };
        ScreenWorldReading screen = SensorNormalizer.FromScreen(
            null,
            null,
            null,
            ClassifiedValue<MapPoint>.Derived(new MapPoint(2, 2), Now.AddSeconds(-1)),
            null);

        FusedWorldObservation fused = DeterministicSensorFusion.Fuse(
            SensorNormalizer.Bundle(null, memory, screen, null),
            Now);

        Assert.False(fused.PlayerPosition.HasValue);
        Assert.Equal(DeterministicSensorFusion.SameRankDisagreementReason, fused.PlayerPosition.Value.FailureReason);
    }

    [Fact]
    public void Memory_map_and_standing_cell_pass_through()
    {
        GameplayObservation memory = GameplayObservation.Unobserved("memory_channel", Now) with
        {
            MapId = ClassifiedValue<int>.Live(42, Now.AddSeconds(-1)),
            StandingCell = ClassifiedValue<MapPoint>.Live(new MapPoint(8, 9), Now.AddSeconds(-1)),
        };

        FusedWorldObservation fused = DeterministicSensorFusion.Fuse(
            SensorNormalizer.Bundle(null, memory, null, null),
            Now);

        Assert.Equal(42, fused.MapId.Value.Value);
        Assert.Equal(new MapPoint(8, 9), fused.StandingCell.Value.Value);
        Assert.Equal(SensorKind.Memory, fused.MapId.Winner);
    }

    [Fact]
    public void Agreeing_entity_lists_keep_the_network_list()
    {
        SelectableEntity mob = new(7, new MapPoint(3, 4), 0.5, Now.AddSeconds(-1), 1100);
        IReadOnlyList<SelectableEntity> list = [mob];
        GameplayObservation network = NetworkVitals(7218, 7305, 1362) with
        {
            Entities = ClassifiedValue<IReadOnlyList<SelectableEntity>>.Live(list, Now.AddSeconds(-1)),
        };
        GameplayObservation memory = GameplayObservation.Unobserved("memory_channel", Now) with
        {
            Entities = ClassifiedValue<IReadOnlyList<SelectableEntity>>.Cached(list, Now.AddSeconds(-1)),
        };

        FusedWorldObservation fused = DeterministicSensorFusion.Fuse(
            SensorNormalizer.Bundle(network, memory, null, null),
            Now);

        Assert.True(fused.Entities.HasValue);
        Assert.Equal(7, fused.Entities.Value.Value[0].EntityId);
        Assert.Equal(SensorKind.Network, fused.Entities.Winner);
        Assert.Equal(DataSourceKind.Live, fused.Entities.Value.Source);
    }

    [Fact]
    public void Different_entity_lists_are_unknown()
    {
        GameplayObservation network = NetworkVitals(7218, 7305, 1362) with
        {
            Entities = ClassifiedValue<IReadOnlyList<SelectableEntity>>.Live(
                [new SelectableEntity(7, new MapPoint(3, 4), 0.5, Now.AddSeconds(-1), 1100)],
                Now.AddSeconds(-1)),
        };
        GameplayObservation memory = GameplayObservation.Unobserved("memory_channel", Now) with
        {
            Entities = ClassifiedValue<IReadOnlyList<SelectableEntity>>.Live(
                [new SelectableEntity(8, new MapPoint(3, 4), 0.5, Now.AddSeconds(-1), 1100)],
                Now.AddSeconds(-1)),
        };

        FusedWorldObservation fused = DeterministicSensorFusion.Fuse(
            SensorNormalizer.Bundle(network, memory, null, null),
            Now);

        Assert.False(fused.Entities.HasValue);
        Assert.Equal(DeterministicSensorFusion.SameRankDisagreementReason, fused.Entities.Value.FailureReason);
    }

    [Fact]
    public void Local_attach_and_navigation_pass_through()
    {
        LocalWorldReading local = SensorNormalizer.FromLocal(
            ClassifiedValue<bool>.Derived(true, Now.AddSeconds(-1)),
            new OccupancyView(Array.Empty<SelectableEntity>(), Now.AddMilliseconds(-200)),
            ClassifiedValue<bool>.Derived(true, Now.AddSeconds(-1)),
            Now);

        FusedWorldObservation fused = DeterministicSensorFusion.Fuse(
            SensorNormalizer.Bundle(null, null, null, local),
            Now);

        Assert.True(fused.ClientAttached.HasValue);
        Assert.True(fused.ClientAttached.Value.Value);
        Assert.True(fused.OccupancyViewFresh.HasValue);
        Assert.True(fused.OccupancyViewFresh.Value.Value);
        Assert.True(fused.NavigationPathFound.HasValue);
        Assert.True(fused.NavigationPathFound.Value.Value);
    }

    [Fact]
    public void Missing_navigation_is_unknown_not_no_path()
    {
        FusedWorldObservation fused = DeterministicSensorFusion.Fuse(SensorObservationSet.Empty, Now);
        Assert.False(fused.NavigationPathFound.HasValue);
        Assert.Equal(DeterministicSensorFusion.SensorNotObservedReason, fused.NavigationPathFound.Value.FailureReason);
        Assert.False(fused.NavigationPathFound.Value.Value);
    }

    [Fact]
    public void Screen_live_vitals_are_refused_rather_than_relabelled()
    {
        var live = new ScreenVitalPair(
            ClassifiedValue<int>.Live(7218, Now.AddSeconds(-1)),
            ClassifiedValue<int>.Live(7305, Now.AddSeconds(-1)),
            0.99,
            null);
        ScreenWorldReading screen = SensorNormalizer.FromScreen(live, null, null, null, null);

        FusedWorldObservation fused = DeterministicSensorFusion.Fuse(
            SensorNormalizer.Bundle(null, null, screen, null),
            Now);

        Assert.False(fused.Hp.HasValue);
        Assert.Equal("screen_vitals_must_not_be_live", fused.Hp.Value.FailureReason);
    }

    [Fact]
    public void ToGameplayObservation_preserves_unknown_reasons()
    {
        FusedWorldObservation fused = DeterministicSensorFusion.Fuse(SensorObservationSet.Empty, Now);
        GameplayObservation observation = SensorFusionWorldAdapter.ToGameplayObservation(fused);

        Assert.False(observation.Hp.HasValue);
        Assert.Equal(DeterministicSensorFusion.SensorNotObservedReason, observation.Hp.FailureReason);
        Assert.False(observation.HasTarget.HasValue);
        Assert.Equal(
            DeterministicSensorFusion.SensorNotObservedReason,
            observation.HasTarget.FailureReason);
        Assert.Equal(Now, observation.ObservedAtUtc);
    }

    [Fact]
    public void TryToWorldState_succeeds_only_when_vitals_and_entities_are_known()
    {
        SelectableEntity mob = new(7, new MapPoint(3, 4), null, Now.AddSeconds(-1), 1100);
        GameplayObservation network = NetworkVitals(7218, 7305, 1362) with
        {
            Entities = ClassifiedValue<IReadOnlyList<SelectableEntity>>.Live([mob], Now.AddSeconds(-1)),
        };

        FusedWorldObservation fused = DeterministicSensorFusion.Fuse(
            SensorNormalizer.Bundle(network, null, null, null),
            Now);
        WorldModelProjection projection = SensorFusionWorldAdapter.TryToWorldState(fused);

        Assert.True(projection.Succeeded);
        Assert.NotNull(projection.State);
        Assert.True(projection.State!.PlayerAlive);
        Assert.Equal(7218 / 7305.0, projection.State.PlayerHpRatio);
        Assert.Single(projection.State.Entities);
        Assert.Equal("7", projection.State.Entities[0].Id);
        Assert.Equal("1100", projection.State.Entities[0].Kind);
        Assert.Null(projection.State.Entities[0].HpRatio);
    }

    [Fact]
    public void TryToWorldState_refuses_an_unknown_entity_list()
    {
        FusedWorldObservation fused = DeterministicSensorFusion.Fuse(
            SensorNormalizer.Bundle(NetworkVitals(7218, 7305, 1362), null, null, null),
            Now);

        WorldModelProjection projection = SensorFusionWorldAdapter.TryToWorldState(fused);
        Assert.False(projection.Succeeded);
        Assert.Equal("not_published_by_provider", projection.FailureReason);
    }

    [Fact]
    public void Two_fuses_of_the_same_inputs_are_identical()
    {
        SensorObservationSet inputs = SensorNormalizer.Bundle(
            NetworkVitals(7218, 7305, 1362),
            GameplayObservation.Unobserved("memory_channel", Now) with
            {
                PlayerPosition = ClassifiedValue<MapPoint>.Live(new MapPoint(1, 2), Now.AddSeconds(-1)),
            },
            null,
            null);

        FusedWorldObservation a = DeterministicSensorFusion.Fuse(inputs, Now);
        FusedWorldObservation b = DeterministicSensorFusion.Fuse(inputs, Now);

        Assert.Equal(a.Hp, b.Hp);
        Assert.Equal(a.PlayerPosition, b.PlayerPosition);
        Assert.Equal(a.HasTarget, b.HasTarget);
        Assert.Equal(a.Disagreements, b.Disagreements);
        Assert.Equal(a.FusedAtUtc, b.FusedAtUtc);
    }

    [Fact]
    public void Fuse_allocations_stay_bounded_across_repeated_calls()
    {
        SensorObservationSet inputs = SensorNormalizer.Bundle(
            NetworkVitals(7218, 7305, 1362),
            null,
            null,
            null);

        for (int i = 0; i < 32; i++)
            DeterministicSensorFusion.Fuse(inputs, Now);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++)
            DeterministicSensorFusion.Fuse(inputs, Now);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < 2_000_000, $"fusion allocated {allocated} bytes over 256 calls");
    }

    private static GameplayObservation NetworkVitals(
        int hp,
        int maxHp,
        int mp,
        DateTime? observedAtUtc = null)
    {
        DateTime at = observedAtUtc ?? Now.AddSeconds(-1);
        return new GameplayObservation(
            Classify(hp, DataSourceKind.Live, at),
            Classify(maxHp, DataSourceKind.Live, at),
            Classify(mp, DataSourceKind.Live, at),
            ClassifiedValue<int>.Unknown("max_mp_not_mapped"),
            ClassifiedValue<bool>.Unknown("target_flag_not_mapped"),
            ClassifiedValue<bool>.Unknown("combat_flag_not_mapped"),
            ClassifiedValue<int>.Unknown("no_entities_reported"),
            Now);
    }

    private static ScreenVitalPair DerivedPair(int current, int maximum) => new(
        Classify(current, DataSourceKind.Derived, Now.AddSeconds(-1)),
        Classify(maximum, DataSourceKind.Derived, Now.AddSeconds(-1)),
        0.95,
        null);

    private static ClassifiedValue<T> Classify<T>(T value, DataSourceKind source, DateTime at) => source switch
    {
        DataSourceKind.Live => ClassifiedValue<T>.Live(value, at),
        DataSourceKind.Derived => ClassifiedValue<T>.Derived(value, at),
        DataSourceKind.Cached => ClassifiedValue<T>.Cached(value, at),
        DataSourceKind.Simulated => ClassifiedValue<T>.Simulated(value, at),
        _ => new ClassifiedValue<T>(default!, DataSourceKind.Unknown, at, false, null, "source_unknown"),
    };

    private static TargetRoiCalibration Calibrated() =>
        TargetRoiCalibration.Confirmed(0.40, 0.06, 0.20, 0.02, 1920, 1080, Now);
}
