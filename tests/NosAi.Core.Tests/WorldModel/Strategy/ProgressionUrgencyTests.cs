using System;
using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Strategy;
using Xunit;

namespace NosAi.Core.Tests.WorldModel.Strategy;

/// <summary>
/// Progression was the last strategic goal no signal could ever produce, so the plan could
/// never land on it. These tests pin how the experience pool feeds it, and above all that an
/// unread pool reports nothing instead of zero: a character whose progress nobody measured is
/// not a character with nothing left to gain.
/// </summary>
public sealed class ProgressionUrgencyTests
{
    private static readonly DateTime Now = DateTime.UnixEpoch;

    [Fact]
    public void ExperienceNeverRead_ReportsNothingRatherThanNoUrgency()
    {
        Assert.Null(StrategyPlanner.AssessProgressionUrgency(BuildPlayer(CombatantStatus.Empty)));
    }

    [Fact]
    public void AFreshLevel_IsLowUrgency()
    {
        StrategicSignal? signal = StrategyPlanner.AssessProgressionUrgency(
            BuildPlayer(WithExperience(current: 10, max: 100)));

        Assert.NotNull(signal);
        Assert.Equal(0.1, signal!.Urgency, precision: 6);
        Assert.Equal("experience_fraction", signal.Reason);
    }

    /// <summary>
    /// Abandoning a bar at nine tenths wastes more than abandoning one just begun, so the
    /// closer the level the louder the goal — and the reason says which case it is.
    /// </summary>
    [Fact]
    public void ALevelWithinReach_IsNamedAsSuch()
    {
        StrategicSignal? signal = StrategyPlanner.AssessProgressionUrgency(
            BuildPlayer(WithExperience(current: 95, max: 100)));

        Assert.NotNull(signal);
        Assert.Equal(0.95, signal!.Urgency, precision: 6);
        Assert.Equal("level_nearly_reached", signal.Reason);
    }

    [Fact]
    public void TheGoalKindIsProgression()
    {
        StrategicSignal? signal = StrategyPlanner.AssessProgressionUrgency(
            BuildPlayer(WithExperience(current: 50, max: 100)));

        Assert.NotNull(signal);
        Assert.Equal(StrategicGoalKind.Progression, signal!.Kind);
    }

    [Fact]
    public void NullPlayer_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => StrategyPlanner.AssessProgressionUrgency(null!));
    }

    private static CombatantStatus WithExperience(double current, double max) =>
        new(
            EquatableArray<Resource>.From(new[]
            {
                new Resource(
                    ResourceKind.Experience,
                    WorldFact<double>.Live(current, 1d, Now),
                    WorldFact<double>.Live(max, 1d, Now)),
            }),
            EquatableArray<StatusEffect>.Empty);

    private static Player BuildPlayer(CombatantStatus status) =>
        new(
            new EntityId("player-1"),
            WorldFact<WorldPosition>.Unknown("r", Now),
            WorldFact<float>.Unknown("r", Now),
            WorldFact<bool>.Live(true, 1d, Now),
            WorldFact<MapId>.Live(new MapId("map-1"), 1d, Now),
            status,
            WorldFact<EquatableArray<Skill>>.Live(EquatableArray<Skill>.Empty, 1d, Now),
            WorldFact<EquatableArray<Cooldown>>.Live(EquatableArray<Cooldown>.Empty, 1d, Now),
            WorldFact<EquatableArray<InventoryItem>>.Live(EquatableArray<InventoryItem>.Empty, 1d, Now),
            WorldFact<EquatableArray<EquipmentItem>>.Live(EquatableArray<EquipmentItem>.Empty, 1d, Now));
}
