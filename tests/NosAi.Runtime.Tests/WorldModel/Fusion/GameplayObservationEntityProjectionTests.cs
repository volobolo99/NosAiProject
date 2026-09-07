using NosAi.Core.WorldModel;
using NosAi.LiveIntegration;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;
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

    /// <summary>
    /// The same monster as the wire's <c>st</c> packet states it: the fraction
    /// and the two numbers it was divided from.
    /// </summary>
    private static SelectableEntity Monster313816WithPoints(int current = 198, int maximum = 310) =>
        Monster313816((double)current / maximum) with
        {
            Vitals = new NosAi.Runtime.Perception.Network.AbsoluteVitals(current, maximum)
        };

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
    /// When the wire stated points, the World Model holds points. This is the
    /// whole reason the decoder stopped dividing them away.
    /// </summary>
    /// <remarks>
    /// The fraction is not discarded either: <see cref="Resource"/> derives it
    /// from the two bounds itself, so a consumer reading only
    /// <see cref="Resource.Fraction"/> sees the same number as before while one
    /// reading the bounds now sees something instead of Unknown.
    /// </remarks>
    [Fact]
    public void MobHealth_IsHeldInPoints_WhenTheWireStatedPoints()
    {
        WorldModelSnapshot snapshot = Project(
            WithEntities(Monster313816WithPoints()), _ => CatalogueClass.Monster);

        Resource health = Assert.Single(Assert.Single(snapshot.Mobs).Status.Resources);
        Assert.Equal(ResourceKind.Health, health.Kind);

        Assert.True(health.Current.HasValue);
        Assert.True(health.Maximum.HasValue);
        Assert.Equal(198d, health.Current.Value);
        Assert.Equal(310d, health.Maximum.Value);

        Assert.True(health.Fraction.HasValue);
        Assert.Equal(198d / 310d, health.Fraction.Value, precision: 10);
    }

    /// <summary>
    /// The bounds carry the same Cached label and the same named reason the
    /// fraction already carried, and for the same cause: the numbers are real,
    /// and <see cref="SelectableEntity.ObservedAtUtc"/> is the position's
    /// instant, so their age is not knowable here.
    /// </summary>
    [Fact]
    public void MobHealthInPoints_IsCached_NotLive()
    {
        WorldModelSnapshot snapshot = Project(
            WithEntities(Monster313816WithPoints()), _ => CatalogueClass.Monster);

        Resource health = Assert.Single(Assert.Single(snapshot.Mobs).Status.Resources);

        Assert.Equal(CoreDataSourceKind.Cached, health.Current.Source);
        Assert.Equal(CoreDataSourceKind.Cached, health.Maximum.Source);
        Assert.Equal(GameplayObservationProjector.AbsoluteVitalsReason, health.Current.Reason);
        Assert.Equal(GameplayObservationProjector.AbsoluteVitalsReason, health.Maximum.Reason);
    }

    /// <summary>
    /// An entity the wire only ever described in percent keeps the fraction-only
    /// shape. The two shapes coexist because the two packets differ, and neither
    /// is turned into the other.
    /// </summary>
    [Fact]
    public void TwoMobs_OneWithPointsAndOneWithout_KeepTheirOwnShapes()
    {
        SelectableEntity withPoints = Monster313816WithPoints();
        var withoutPoints = new SelectableEntity(313826, new MapPoint(110, 64), 1.0, Earlier, Vnum: 36);

        WorldModelSnapshot snapshot = Project(
            WithEntities(withPoints, withoutPoints), _ => CatalogueClass.Monster);

        Assert.Equal(2, snapshot.Mobs.Count);

        Resource stated = Assert.Single(snapshot.Mobs.Single(m => m.Id.Value == "mob-313816").Status.Resources);
        Resource inferred = Assert.Single(snapshot.Mobs.Single(m => m.Id.Value == "mob-313826").Status.Resources);

        Assert.True(stated.Maximum.HasValue);
        Assert.False(inferred.Maximum.HasValue);
        Assert.Equal(GameplayObservationProjector.FractionOnlyHealthReason, inferred.Maximum.Reason);
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
    public void MobHostility_StaysUnknown_WhenNothingHasHitUs()
    {
        WorldModelSnapshot snapshot = Project(WithEntities(Monster313816()), _ => CatalogueClass.Monster);

        Mob mob = Assert.Single(snapshot.Mobs);
        Assert.False(mob.IsHostile.HasValue);
        Assert.Equal("hostility_never_established:no_observed_hit_from_this_entity", mob.IsHostile.Reason);
    }

    /// <summary>
    /// An entity that hit the controlled character is beyond doubt something
    /// that fights -- the strongest evidence <see cref="TargetEstablishment"/>
    /// recognises, and the only one this projection accepts for hostility.
    /// </summary>
    [Fact]
    public void MobThatHitUs_IsEstablishedHostile_FromTheWiresOwnRecord()
    {
        GameplayObservation observation = WithEntities(Monster313816()) with
        {
            HitBy = ClassifiedValue<Aggressor>.Live(new Aggressor(313816, 3), Now)
        };

        WorldModelSnapshot snapshot = Project(observation, _ => CatalogueClass.Monster);

        Mob mob = Assert.Single(snapshot.Mobs);
        Assert.True(mob.IsHostile.HasValue);
        Assert.True(mob.IsHostile.Value);
        Assert.Equal(CoreDataSourceKind.Live, mob.IsHostile.Source);
        Assert.Equal(Now, mob.IsHostile.ObservedAtUtc);
    }

    /// <summary>
    /// The aggressor is one named entity, not a licence for everything in
    /// view: a mob that did not hit us keeps an Unknown hostility even while
    /// another one is established.
    /// </summary>
    [Fact]
    public void OnlyTheEntityThatHitUs_IsEstablishedHostile()
    {
        var attacker = new SelectableEntity(1, new MapPoint(1, 1), 0.5, Earlier, Vnum: 36);
        var bystander = new SelectableEntity(2, new MapPoint(2, 2), 0.5, Earlier, Vnum: 36);

        GameplayObservation observation = WithEntities(attacker, bystander) with
        {
            HitBy = ClassifiedValue<Aggressor>.Live(new Aggressor(1, 3), Now)
        };

        WorldModelSnapshot snapshot = Project(observation, _ => CatalogueClass.Monster);

        Assert.True(snapshot.Mobs.Single(m => m.Id.Value == "mob-1").IsHostile.Value);
        Assert.False(snapshot.Mobs.Single(m => m.Id.Value == "mob-2").IsHostile.HasValue);
    }

    /// <summary>
    /// Having acted on an entity says the client accepted it as a target, not
    /// that the entity is hostile -- an NPC can be selected too. Only a hit
    /// establishes hostility here.
    /// </summary>
    [Fact]
    public void SelectingAnEntity_DoesNotEstablishHostility()
    {
        GameplayObservation observation = WithEntities(Monster313816()) with
        {
            SelectedTarget = ClassifiedValue<TargetedEntity>.Live(new TargetedEntity(313816, 3), Now)
        };

        WorldModelSnapshot snapshot = Project(observation, _ => CatalogueClass.Monster);

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
