using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Temporal;
using Xunit;

namespace NosAi.Core.Tests.WorldModel.Temporal;

/// <summary>
/// AP-05: an established hostility must hold across fusion cycles. The wire
/// names one aggressor -- the most recent -- so without this a mob would be
/// hostile for exactly the cycle in which it struck and unknown again the next.
/// </summary>
public sealed class MobHostilityCarryForwardTests
{
    private static readonly DateTime Now = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Struck = new(2026, 9, 7, 11, 59, 55, DateTimeKind.Utc);
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxGap = TimeSpan.FromSeconds(5);

    private static Mob MobWithHostility(WorldFact<bool> isHostile, DateTime positionAt) => new(
        new EntityId("mob-1"),
        WorldFact<WorldPosition>.Live(new WorldPosition(1, 1), 1.0, positionAt),
        WorldFact<string>.Unknown("species_name_catalog_not_available", positionAt),
        isHostile,
        WorldFact<bool>.Derived(true, 1.0, positionAt),
        CombatantStatus.Empty);

    private static WorldModelSnapshot SnapshotWith(params Mob[] mobs) =>
        WorldModelSnapshot.Unknown("test") with { Mobs = EquatableArray<Mob>.From(mobs) };

    private static WorldModelSnapshot Enrich(WorldModelSnapshot previous, WorldModelSnapshot current) =>
        WorldModelTemporalEnricher.Enrich(previous, current, Now, MaxAge, MaxGap);

    [Fact]
    public void AnEstablishedHostility_SurvivesACycleThatDoesNotRestateIt()
    {
        Mob struck = MobWithHostility(WorldFact<bool>.Live(true, 1.0, Struck), Struck);
        Mob quiet = MobWithHostility(WorldFact<bool>.Unknown("no_observed_hit_from_this_entity", Now), Now);

        WorldModelSnapshot enriched = Enrich(SnapshotWith(struck), SnapshotWith(quiet));

        WorldFact<bool> hostility = enriched.Mobs[0].IsHostile;
        Assert.True(hostility.HasValue);
        Assert.True(hostility.Value);
    }

    /// <summary>
    /// Carried forward is not observed again: the label says so, and the
    /// instant stays the original one so the fact keeps ageing.
    /// </summary>
    [Fact]
    public void ACarriedHostility_IsLabelledCached_AndKeepsItsOriginalInstant()
    {
        Mob struck = MobWithHostility(WorldFact<bool>.Live(true, 0.9, Struck), Struck);
        Mob quiet = MobWithHostility(WorldFact<bool>.Unknown("no_observed_hit_from_this_entity", Now), Now);

        WorldModelSnapshot enriched = Enrich(SnapshotWith(struck), SnapshotWith(quiet));

        WorldFact<bool> hostility = enriched.Mobs[0].IsHostile;
        Assert.Equal(DataSourceKind.Cached, hostility.Source);
        Assert.Equal(Struck, hostility.ObservedAtUtc);
        Assert.Equal(0.9, hostility.Confidence);
    }

    /// <summary>Two quiet cycles must not reset the age: it is what a freshness gate reads.</summary>
    [Fact]
    public void AcrossSeveralQuietCycles_TheInstantKeepsAgeing_RatherThanResetting()
    {
        Mob struck = MobWithHostility(WorldFact<bool>.Live(true, 1.0, Struck), Struck);
        Mob quiet = MobWithHostility(WorldFact<bool>.Unknown("no_observed_hit_from_this_entity", Now), Now);

        WorldModelSnapshot first = Enrich(SnapshotWith(struck), SnapshotWith(quiet));
        WorldModelSnapshot second = Enrich(first, SnapshotWith(quiet));

        Assert.Equal(Struck, second.Mobs[0].IsHostile.ObservedAtUtc);
        Assert.Equal(DataSourceKind.Cached, second.Mobs[0].IsHostile.Source);
    }

    /// <summary>A fresh observation wins: nothing stale overwrites what this cycle actually saw.</summary>
    [Fact]
    public void AFreshlyStatedHostility_IsNotReplacedByTheCarriedOne()
    {
        Mob struck = MobWithHostility(WorldFact<bool>.Live(true, 0.5, Struck), Struck);
        Mob restruck = MobWithHostility(WorldFact<bool>.Live(true, 1.0, Now), Now);

        WorldModelSnapshot enriched = Enrich(SnapshotWith(struck), SnapshotWith(restruck));

        Assert.Equal(DataSourceKind.Live, enriched.Mobs[0].IsHostile.Source);
        Assert.Equal(Now, enriched.Mobs[0].IsHostile.ObservedAtUtc);
    }

    /// <summary>Carrying forward widens no claim: an unknown prior stays unknown.</summary>
    [Fact]
    public void AnUnknownPriorHostility_CarriesNothing()
    {
        Mob quietBefore = MobWithHostility(WorldFact<bool>.Unknown("no_observed_hit_from_this_entity", Struck), Struck);
        Mob quietNow = MobWithHostility(WorldFact<bool>.Unknown("no_observed_hit_from_this_entity", Now), Now);

        WorldModelSnapshot enriched = Enrich(SnapshotWith(quietBefore), SnapshotWith(quietNow));

        Assert.False(enriched.Mobs[0].IsHostile.HasValue);
    }

    /// <summary>Hostility travels with the entity id, never to a different mob.</summary>
    [Fact]
    public void AnUnmatchedEntity_InheritsNothingFromAnotherMob()
    {
        Mob struck = MobWithHostility(WorldFact<bool>.Live(true, 1.0, Struck), Struck);
        Mob other = MobWithHostility(WorldFact<bool>.Unknown("no_observed_hit_from_this_entity", Now), Now) with
        {
            Id = new EntityId("mob-2")
        };

        WorldModelSnapshot enriched = Enrich(SnapshotWith(struck), SnapshotWith(other));

        Assert.False(enriched.Mobs[0].IsHostile.HasValue);
    }
}
