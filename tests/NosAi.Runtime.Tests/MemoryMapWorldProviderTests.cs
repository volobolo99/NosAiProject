using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Map id and standing cell from memory: the <c>--grid-check</c> reading on the
/// observation, without touching the position used to aim.
/// </summary>
public sealed class MemoryMapWorldProviderTests
{
    private static readonly DateTime At = new(2026, 9, 3, 10, 0, 0, DateTimeKind.Utc);

    private sealed class BlankProvider : IGameplayProvider
    {
        public GameplayObservation Observation { get; init; } = GameplayObservation.Unobserved("nothing_read", At);

        public string Name => "blank";

        public GameplayObservation Observe() => Observation;
    }

    private static MemoryMapWorldProvider Over(MapWorldObservation world, GameplayObservation? inner = null) =>
        new(new BlankProvider { Observation = inner ?? GameplayObservation.Unobserved("nothing_read", At) }, () => world);

    [Fact]
    public void AReadableMapIdAndStandingCellAreLiveAndNamed()
    {
        GameplayObservation observation = Over(new MapWorldObservation(
            ClassifiedValue<int>.Live(7, At),
            ClassifiedValue<MapPoint>.Live(new MapPoint(12, 8), At))).Observe();

        Assert.True(observation.MapId.HasValue);
        Assert.Equal(7, observation.MapId.Value);
        Assert.Equal(DataSourceKind.Live, observation.MapId.Source);
        Assert.True(observation.StandingCell.HasValue);
        Assert.Equal(new MapPoint(12, 8), observation.StandingCell.Value);
        Assert.Equal(DataSourceKind.Live, observation.StandingCell.Source);
        Assert.Contains("map-world", Over(MapWorldObservation.Unknown("x")).Name, StringComparison.Ordinal);
    }

    [Fact]
    public void AReadableMapIdWithAnUnreadableStandingCellKeepsEachFieldsReason()
    {
        GameplayObservation observation = Over(new MapWorldObservation(
            ClassifiedValue<int>.Live(4, At),
            ClassifiedValue<MapPoint>.Unknown("player_object_unreadable"))).Observe();

        Assert.Equal(4, observation.MapId.Value);
        Assert.False(observation.StandingCell.HasValue);
        Assert.Equal("player_object_unreadable", observation.StandingCell.FailureReason);
    }

    [Fact]
    public void AMissingSessionIsUnknownWithTheReason()
    {
        GameplayObservation observation = Over(
            MapWorldObservation.Unknown("player_manager_null")).Observe();

        Assert.False(observation.MapId.HasValue);
        Assert.Equal("player_manager_null", observation.MapId.FailureReason);
        Assert.False(observation.StandingCell.HasValue);
        Assert.Equal("player_manager_null", observation.StandingCell.FailureReason);
    }

    [Fact]
    public void AThrowingReaderCostsTheMapWorldNotTheInnerVitals()
    {
        var inner = new GameplayObservation(
            ClassifiedValue<int>.Live(100, At),
            ClassifiedValue<int>.Live(100, At),
            ClassifiedValue<int>.Live(50, At),
            ClassifiedValue<int>.Live(50, At),
            ClassifiedValue<bool>.Unknown("target_flag_not_mapped"),
            ClassifiedValue<bool>.Unknown("combat_flag_not_mapped"),
            ClassifiedValue<int>.Unknown("not_counted"),
            At);

        var provider = new MemoryMapWorldProvider(
            new BlankProvider { Observation = inner },
            () => throw new InvalidOperationException("boom"));

        GameplayObservation observation = provider.Observe();

        Assert.True(observation.HasVitals);
        Assert.Equal(100, observation.Hp.Value);
        Assert.False(observation.MapId.HasValue);
        Assert.Equal(
            $"{MemoryMapWorldProvider.ReaderFailedPrefix}:InvalidOperationException",
            observation.MapId.FailureReason);
        Assert.Equal(
            $"{MemoryMapWorldProvider.ReaderFailedPrefix}:InvalidOperationException",
            observation.StandingCell.FailureReason);
    }

    [Fact]
    public void AnAnswerAlreadyOnTheObservationStands()
    {
        var established = GameplayObservation.Unobserved("nothing_read", At) with
        {
            MapId = ClassifiedValue<int>.Derived(99, At),
            StandingCell = ClassifiedValue<MapPoint>.Derived(new MapPoint(1, 1), At),
        };

        GameplayObservation observation = Over(
            new MapWorldObservation(
                ClassifiedValue<int>.Live(7, At),
                ClassifiedValue<MapPoint>.Live(new MapPoint(12, 8), At)),
            established).Observe();

        Assert.Equal(99, observation.MapId.Value);
        Assert.Equal(DataSourceKind.Derived, observation.MapId.Source);
        Assert.Equal(new MapPoint(1, 1), observation.StandingCell.Value);
        Assert.Equal(DataSourceKind.Derived, observation.StandingCell.Source);
    }

    [Fact]
    public void TheDecoratorDoesNotWritePlayerPosition()
    {
        GameplayObservation observation = Over(new MapWorldObservation(
            ClassifiedValue<int>.Live(7, At),
            ClassifiedValue<MapPoint>.Live(new MapPoint(12, 8), At))).Observe();

        Assert.False(observation.PlayerPosition.HasValue);
        Assert.Equal("nothing_read", observation.PlayerPosition.FailureReason);
        Assert.True(observation.StandingCell.HasValue);
    }

    [Fact]
    public void PublishingMapWorldDidNotMakeItAPreconditionForPlanning()
    {
        var inner = new GameplayObservation(
            ClassifiedValue<int>.Live(100, At),
            ClassifiedValue<int>.Live(100, At),
            ClassifiedValue<int>.Live(50, At),
            ClassifiedValue<int>.Live(50, At),
            ClassifiedValue<bool>.Unknown("target_flag_not_mapped"),
            ClassifiedValue<bool>.Unknown("combat_flag_not_mapped"),
            ClassifiedValue<int>.Unknown("not_counted"),
            At);

        GameplayObservation observation = new MemoryMapWorldProvider(
            new BlankProvider { Observation = inner },
            () => MapWorldObservation.Unknown("grid_file_not_found:1")).Observe();

        Assert.True(observation.HasVitals);
        Assert.Null(observation.UnusableReason);
        Assert.False(observation.MapId.HasValue);
        Assert.False(observation.StandingCell.HasValue);
    }

    [Fact]
    public void ASourceWithNoProcessIdIsProcessNotAttached()
    {
        using var source = new ClientMapWorldSource(() => null);
        MapWorldObservation world = source.Read();

        Assert.False(world.MapId.HasValue);
        Assert.Equal(MemoryMapWorldProvider.ProcessNotAttachedReason, world.MapId.FailureReason);
        Assert.False(world.StandingCell.HasValue);
        Assert.Equal(MemoryMapWorldProvider.ProcessNotAttachedReason, world.StandingCell.FailureReason);
    }

    [Fact]
    public void ASourceWithANonPositiveProcessIdIsProcessNotAttached()
    {
        using var source = new ClientMapWorldSource(() => 0);
        MapWorldObservation world = source.Read();

        Assert.Equal(MemoryMapWorldProvider.ProcessNotAttachedReason, world.MapId.FailureReason);
        Assert.Equal(MemoryMapWorldProvider.ProcessNotAttachedReason, world.StandingCell.FailureReason);
    }
}
