using NosAi.Core.WorldModel;
using NosAi.LiveIntegration;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.WorldModel.Fusion;
using Xunit;
using CoreDataSourceKind = NosAi.Core.WorldModel.DataSourceKind;

namespace NosAi.Runtime.Tests.WorldModel.Fusion;

/// <summary>
/// AP-05: the observed entity list becoming real <see cref="Mob"/>/<see cref="Npc"/>
/// records in the canonical World Model, and the boundary that keeps
/// unclassified entities out of both.
/// </summary>
public sealed class GameplayObservationEntityProjectionTests
{
    private static readonly DateTime Now = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Earlier = new(2026, 9, 7, 11, 59, 30, DateTimeKind.Utc);
    private static readonly EntityId PlayerId = new("player-1");

    private static GameplayObservation WithEntities(params SelectableEntity[] entities) =>
        GameplayObservation.Unobserved("only_entities_under_test", Now) with
        {
            Entities = ClassifiedValue<IReadOnlyList<SelectableEntity>>.Live(entities, Now)
        };

    private static WorldModelSnapshot Project(GameplayObservation observation, Func<int, CatalogueClass>? classify) =>
        GameplayObservationProjector.Project(observation, PlayerId, version: 1, Now, classify);

    /// <summary>
    /// The monster from this repository's own capture: entity 313816, vnum 36,
    /// last seen at 198/310 HP (0.6387) -- the fraction is all
    /// <see cref="SelectableEntity"/> carries today.
    /// </summary>
    private static SelectableEntity Monster313816(double? hpRatio = 198d / 310d, DateTime? at = null) =>
        new(313816, new MapPoint(109, 63), hpRatio, at ?? Earlier, Vnum: 36);

    [Fact]
    public void NoClassifier_LeavesBothListsEmpty_EvenWithEntitiesInView()
    {
        WorldModelSnapshot snapshot = Project(WithEntities(Monster313816()), classify: null);

        Assert.Empty(snapshot.Mobs);
        Assert.Empty(snapshot.Npcs);
    }

    [Fact]
    public void CataloguedMonster_ProjectsIntoMobs_WithItsIdAndPosition()
    {
        WorldModelSnapshot snapshot = Project(WithEntities(Monster313816()), _ => CatalogueClass.Monster);

        Mob mob = Assert.Single(snapshot.Mobs);
        Assert.Equal("mob-313816", mob.Id.Value);
        Assert.True(mob.Position.HasValue);
        Assert.Equal(new WorldPosition(109, 63), mob.Position.Value);
        Assert.Equal(CoreDataSourceKind.Live, mob.Position.Source);
        Assert.Equal(Earlier, mob.Position.ObservedAtUtc);
        Assert.Empty(snapshot.Npcs);
    }

    /// <summary>
    /// The health arrives as a fraction with no absolute bound, so it is
    /// carried as exactly that -- never as a fabricated 0.64-out-of-1 pair.
    /// </summary>
    [Fact]
    public void MobHealth_IsAFractionOverTwoUnknownBounds()
    {
        WorldModelSnapshot snapshot = Project(WithEntities(Monster313816()), _ => CatalogueClass.Monster);

        Resource health = Assert.Single(Assert.Single(snapshot.Mobs).Status.Resources);
        Assert.Equal(ResourceKind.Health, health.Kind);

        Assert.True(health.Fraction.HasValue);
        Assert.Equal(198d / 310d, health.Fraction.Value, precision: 10);

        Assert.False(health.Current.HasValue);
        Assert.False(health.Maximum.HasValue);
        Assert.Equal(GameplayObservationProjector.FractionOnlyHealthReason, health.Current.Reason);
        Assert.Equal(GameplayObservationProjector.FractionOnlyHealthReason, health.Maximum.Reason);
    }

    /// <summary>
    /// <see cref="SelectableEntity.ObservedAtUtc"/> is the instant the
    /// position was stated; the health may be older and that record does not
    /// say. Claiming it Live at the position's instant would overstate a
    /// freshness nobody observed.
    /// </summary>
    [Fact]
    public void MobHealth_IsLabelledCached_BecauseItsOwnInstantIsNotCarried()
    {
        WorldModelSnapshot snapshot = Project(WithEntities(Monster313816()), _ => CatalogueClass.Monster);

        Resource health = Assert.Single(Assert.Single(snapshot.Mobs).Status.Resources);

        Assert.Equal(CoreDataSourceKind.Cached, health.Fraction.Source);
        Assert.Equal(GameplayObservationProjector.HealthInstantNotCarriedReason, health.Fraction.Reason);
    }

    [Theory]
    [InlineData(0.5, true)]
    [InlineData(1.0, true)]
    [InlineData(0.0, false)]
    public void MobIsAlive_FollowsTheObservedFraction(double hpRatio, bool expectedAlive)
    {
        WorldModelSnapshot snapshot = Project(WithEntities(Monster313816(hpRatio)), _ => CatalogueClass.Monster);

        Mob mob = Assert.Single(snapshot.Mobs);
        Assert.True(mob.IsAlive.HasValue);
        Assert.Equal(expectedAlive, mob.IsAlive.Value);
    }

    /// <summary>Most sightings are moves, which carry no health at all. Absent is not dead and not alive.</summary>
    [Fact]
    public void MobWithNoStatedHealth_HasNoResourcesAndAnUnknownIsAlive()
    {
        WorldModelSnapshot snapshot = Project(WithEntities(Monster313816(hpRatio: null)), _ => CatalogueClass.Monster);

        Mob mob = Assert.Single(snapshot.Mobs);
        Assert.Empty(mob.Status.Resources);
        Assert.False(mob.IsAlive.HasValue);
    }

    /// <summary>
    /// Presence in the monster table says what an entity is, not how it
    /// behaves: many NosTale monsters are passive until struck, and
    /// <see cref="Mob"/>'s own contract covers "hostile or neutral".
    /// </summary>
    [Fact]
    public void MobHostility_StaysUnknown_BecauseTheCatalogueWasNotAskedThat()
    {
        WorldModelSnapshot snapshot = Project(WithEntities(Monster313816()), _ => CatalogueClass.Monster);

        Assert.False(Assert.Single(snapshot.Mobs).IsHostile.HasValue);
    }

    [Fact]
    public void MobSpecies_StaysUnknown_ButKeepsTheVnumInItsReason()
    {
        WorldModelSnapshot snapshot = Project(WithEntities(Monster313816()), _ => CatalogueClass.Monster);

        Mob mob = Assert.Single(snapshot.Mobs);
        Assert.False(mob.Species.HasValue);
        Assert.Equal("species_name_catalog_not_available:vnum=36", mob.Species.Reason);
    }

    [Fact]
    public void SpecialNonMonsterEntity_ProjectsIntoNpcs_NeverIntoMobs()
    {
        WorldModelSnapshot snapshot = Project(
            WithEntities(Monster313816()),
            _ => CatalogueClass.SpecialNonMonsterEntity);

        Npc npc = Assert.Single(snapshot.Npcs);
        Assert.Equal("npc-313816", npc.Id.Value);
        Assert.Equal(new WorldPosition(109, 63), npc.Position.Value);
        Assert.False(npc.Name.HasValue);
        Assert.Equal("npc_name_catalog_not_available:vnum=36", npc.Name.Reason);
        Assert.False(npc.HasAvailableInteraction.HasValue);
        Assert.Empty(snapshot.Mobs);
    }

    /// <summary>
    /// The residue rule: everything the catalogue does not positively
    /// establish lands in neither list. Turning "not a monster" into "an NPC"
    /// would invert <see cref="TargetEstablishment"/>'s safety asymmetry.
    /// </summary>
    [Theory]
    [InlineData(CatalogueClass.AbsentFromMonsterTable)]
    [InlineData(CatalogueClass.CatalogueNotLoaded)]
    [InlineData(CatalogueClass.CatalogueUnreadable)]
    public void UnestablishedEntity_ReachesNeitherList(CatalogueClass verdict)
    {
        WorldModelSnapshot snapshot = Project(WithEntities(Monster313816()), _ => verdict);

        Assert.Empty(snapshot.Mobs);
        Assert.Empty(snapshot.Npcs);
    }

    /// <summary>
    /// Only <c>in</c> carries a vnum, so most entities are located long before
    /// anything says what they are. They are not classified, and the
    /// classifier is not even consulted for them.
    /// </summary>
    [Fact]
    public void EntityWithNoVnum_IsNotClassifiedAndNotProjected()
    {
        var withoutVnum = new SelectableEntity(999, new MapPoint(1, 2), 0.5, Earlier, Vnum: null);
        int calls = 0;

        WorldModelSnapshot snapshot = Project(WithEntities(withoutVnum), _ => { calls++; return CatalogueClass.Monster; });

        Assert.Empty(snapshot.Mobs);
        Assert.Empty(snapshot.Npcs);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void MixedEntities_SplitIntoTheirOwnLists()
    {
        var monster = new SelectableEntity(1, new MapPoint(1, 1), 0.5, Earlier, Vnum: 36);
        var teleporter = new SelectableEntity(2, new MapPoint(2, 2), null, Earlier, Vnum: 900);
        var unknown = new SelectableEntity(3, new MapPoint(3, 3), 0.9, Earlier, Vnum: 7);

        WorldModelSnapshot snapshot = Project(
            WithEntities(monster, teleporter, unknown),
            vnum => vnum switch
            {
                36 => CatalogueClass.Monster,
                900 => CatalogueClass.SpecialNonMonsterEntity,
                _ => CatalogueClass.AbsentFromMonsterTable
            });

        Assert.Equal("mob-1", Assert.Single(snapshot.Mobs).Id.Value);
        Assert.Equal("npc-2", Assert.Single(snapshot.Npcs).Id.Value);
    }

    /// <summary>
    /// The projection stays pure: same observation, same classifier, same
    /// instant produce equal snapshots, which AP-01's deterministic replay
    /// requires and which a live catalogue handle captured in here would break.
    /// </summary>
    [Fact]
    public void ProjectionWithAClassifier_StaysDeterministic()
    {
        GameplayObservation observation = WithEntities(Monster313816());

        WorldModelSnapshot first = Project(observation, _ => CatalogueClass.Monster);
        WorldModelSnapshot second = Project(observation, _ => CatalogueClass.Monster);

        Assert.Equal(first, second);
    }

    [Fact]
    public void UnobservedEntityList_LeavesBothListsEmpty_EvenWithAClassifier()
    {
        WorldModelSnapshot snapshot = Project(
            GameplayObservation.Unobserved("gameplay_provider_not_available", Now),
            _ => CatalogueClass.Monster);

        Assert.Empty(snapshot.Mobs);
        Assert.Empty(snapshot.Npcs);
    }
}
