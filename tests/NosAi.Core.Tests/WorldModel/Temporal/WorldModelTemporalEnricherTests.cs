using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Temporal;
using Xunit;

namespace NosAi.Core.Tests.WorldModel.Temporal;

public sealed class WorldModelTemporalEnricherTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(5);

    private static WorldModelSnapshot SnapshotWithPlayerAt(WorldPosition position, DateTime observedAt)
    {
        WorldModelSnapshot unknown = WorldModelSnapshot.Unknown("bootstrap");
        return unknown with
        {
            Player = unknown.Player with { Position = WorldFact<WorldPosition>.Live(position, 1.0, observedAt) }
        };
    }

    [Fact]
    public void FirstCycle_WithNoPriorSnapshot_DerivesUnknownVelocity()
    {
        WorldModelSnapshot previous = WorldModelSnapshot.Unknown("no_prior_cycle");
        WorldModelSnapshot current = SnapshotWithPlayerAt(new WorldPosition(0, 0), Now);

        WorldModelSnapshot enriched = WorldModelTemporalEnricher.Enrich(previous, current, Now, MaxAge, MaxGap);

        Assert.False(enriched.Player.Velocity.HasValue);
    }

    [Fact]
    public void TwoCycles_DerivePlayerVelocityFromThePositionDelta()
    {
        WorldModelSnapshot previous = SnapshotWithPlayerAt(new WorldPosition(0, 0), Now - TimeSpan.FromSeconds(2));
        WorldModelSnapshot current = SnapshotWithPlayerAt(new WorldPosition(4, 0), Now);

        WorldModelSnapshot enriched = WorldModelTemporalEnricher.Enrich(previous, current, Now, MaxAge, MaxGap);

        Assert.True(enriched.Player.Velocity.HasValue);
        Assert.Equal(2f, enriched.Player.Velocity.Value.DxPerSecond);
    }

    [Fact]
    public void PlayerPositionConfidence_DecaysWithAge()
    {
        WorldModelSnapshot previous = WorldModelSnapshot.Unknown("no_prior_cycle");
        WorldModelSnapshot current = SnapshotWithPlayerAt(new WorldPosition(1, 1), Now - TimeSpan.FromSeconds(5));

        WorldModelSnapshot enriched = WorldModelTemporalEnricher.Enrich(previous, current, Now, MaxAge, MaxGap);

        Assert.Equal(0.5, enriched.Player.Position.Confidence, precision: 6);
    }

    [Fact]
    public void UnmatchedMob_HasNoPriorSighting_DerivesUnknownVelocity()
    {
        var mob = new Mob(new EntityId("mob-1"), WorldFact<WorldPosition>.Live(new WorldPosition(1, 1), 1.0, Now), WorldFact<string>.Unknown("r"), WorldFact<bool>.Unknown("r"), WorldFact<bool>.Unknown("r"), CombatantStatus.Empty);
        WorldModelSnapshot previous = WorldModelSnapshot.Unknown("no_prior_cycle");
        WorldModelSnapshot current = WorldModelSnapshot.Unknown("bootstrap") with { Mobs = EquatableArray<Mob>.From(new[] { mob }) };

        WorldModelSnapshot enriched = WorldModelTemporalEnricher.Enrich(previous, current, Now, MaxAge, MaxGap);

        Assert.False(enriched.Mobs[0].Velocity.HasValue);
    }

    [Fact]
    public void MatchedMobAcrossCycles_DerivesVelocity_ByEntityId()
    {
        var previousMob = new Mob(new EntityId("mob-1"), WorldFact<WorldPosition>.Live(new WorldPosition(0, 0), 1.0, Now - TimeSpan.FromSeconds(1)), WorldFact<string>.Unknown("r"), WorldFact<bool>.Unknown("r"), WorldFact<bool>.Unknown("r"), CombatantStatus.Empty);
        var currentMob = previousMob with { Position = WorldFact<WorldPosition>.Live(new WorldPosition(3, 0), 1.0, Now) };

        WorldModelSnapshot previous = WorldModelSnapshot.Unknown("prior") with { Mobs = EquatableArray<Mob>.From(new[] { previousMob }) };
        WorldModelSnapshot current = WorldModelSnapshot.Unknown("bootstrap") with { Mobs = EquatableArray<Mob>.From(new[] { currentMob }) };

        WorldModelSnapshot enriched = WorldModelTemporalEnricher.Enrich(previous, current, Now, MaxAge, MaxGap);

        Assert.True(enriched.Mobs[0].Velocity.HasValue);
        Assert.Equal(3f, enriched.Mobs[0].Velocity.Value.DxPerSecond);
    }

    [Fact]
    public void NoMobsThisCycle_ReturnsTheSameEmptyCollection()
    {
        WorldModelSnapshot previous = WorldModelSnapshot.Unknown("prior");
        WorldModelSnapshot current = WorldModelSnapshot.Unknown("bootstrap");

        WorldModelSnapshot enriched = WorldModelTemporalEnricher.Enrich(previous, current, Now, MaxAge, MaxGap);

        Assert.Empty(enriched.Mobs);
    }

    [Fact]
    public void EnrichmentIsDeterministic_SameInputsProduceEqualOutput()
    {
        WorldModelSnapshot previous = SnapshotWithPlayerAt(new WorldPosition(0, 0), Now - TimeSpan.FromSeconds(1));
        WorldModelSnapshot current = SnapshotWithPlayerAt(new WorldPosition(2, 0), Now);

        WorldModelSnapshot a = WorldModelTemporalEnricher.Enrich(previous, current, Now, MaxAge, MaxGap);
        WorldModelSnapshot b = WorldModelTemporalEnricher.Enrich(previous, current, Now, MaxAge, MaxGap);

        Assert.Equal(a, b);
    }

    [Fact]
    public void EnrichmentDoesNotChangeVersionOrOtherFields_OnlyPositionConfidenceAndVelocity()
    {
        WorldModelSnapshot previous = WorldModelSnapshot.Unknown("prior");
        WorldModelSnapshot current = SnapshotWithPlayerAt(new WorldPosition(1, 1), Now) with { Version = 9 };

        WorldModelSnapshot enriched = WorldModelTemporalEnricher.Enrich(previous, current, Now, MaxAge, MaxGap);

        Assert.Equal(9, enriched.Version);
        Assert.Equal(current.Map, enriched.Map);
        Assert.Equal(current.Player.Id, enriched.Player.Id);
    }
}
