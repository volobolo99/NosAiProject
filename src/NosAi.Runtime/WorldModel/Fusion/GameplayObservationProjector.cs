using System.Globalization;
using NosAi.Core.WorldModel;
using NosAi.LiveIntegration;
using RuntimeContracts = NosAi.Runtime.Contracts;

// Aliased rather than imported wholesale: NosAi.Runtime.Autonomy also declares
// a Goal, which would collide with NosAi.Core.WorldModel.Goal in this file.
using CatalogueClass = NosAi.Runtime.Autonomy.CatalogueClass;
using SelectableEntity = NosAi.Runtime.Autonomy.SelectableEntity;
using TargetEstablishment = NosAi.Runtime.Autonomy.TargetEstablishment;

namespace NosAi.Runtime.WorldModel.Fusion;

/// <summary>
/// Projects the existing, already-decorator-fused <see cref="GameplayObservation"/>
/// (network wire + memory readers, per <c>IGameplayProvider</c>'s decorator
/// chain -- see <c>PositionAwareGameplayProvider</c>, <c>MemoryMapWorldProvider</c>,
/// <c>NetworkGameplayProvider</c>) into AP-01's versioned
/// <see cref="WorldModelSnapshot"/> (docs/ROADMAP_ESECUTIVA.md S:AP-01).
///
/// This is a pure, stateless projection: given the same observation, player
/// id, version and instant, it always returns the same snapshot (the
/// "replay deterministico" requirement in AP-01's Definition of Done). It
/// does not itself resolve conflicts between multiple live channels --
/// <see cref="FactFusion"/> is the mechanism that combines this projection's
/// output with a second channel's reading of the same fact once one exists,
/// and AP-02/A3's <see cref="VisualObservationFusion"/> is the first real
/// caller: it fuses this projection's network-derived vitals with the
/// vision channel's screen-derived reading of the same HP/MP facts.
///
/// Honest, deliberate gaps -- populated as <c>Unknown</c>/empty rather than
/// guessed, and left for the phase that actually owns closing them:
/// <list type="bullet">
/// <item><description><see cref="WorldModelSnapshot.Mobs"/>/<see cref="WorldModelSnapshot.Npcs"/> are populated from <see cref="GameplayObservation.Entities"/>, but only for the entities a caller-supplied catalogue classifier positively establishes -- see the <c>classifyVnum</c> parameter and <c>ProjectEntities</c>. Without a classifier both stay empty, which is what every caller that does not pass one still gets. Their health is a fraction over two Unknown bounds (<see cref="Resource.FromObservedFraction"/>): <c>NosTaleWorldProtocolDecoder.DecodeOtherVitals</c> does read a non-player entity's absolute current and maximum HP off the <c>st</c> packet, but divides them away because <c>EntitySighting</c> has nowhere to put them, so the absolutes cannot reach here yet (<c>docs/agents/phases/AP-05/AP-05_A2A4_DEEPSEEK_mob_absolute_vitals.md</c>).</description></item>
/// <item><description>A correction to what an earlier version of this list claimed: <c>NosAi.Runtime.Autonomy.TargetEstablishment.Assess</c> is called from the live targeting path (<c>Gate3Runtime.IsAttackable</c>), but the catalogue it needs is <b>never supplied there</b> -- <c>Gate3ExecutionOrchestrator</c>'s <c>catalogue</c> parameter is optional and no production construction site passes it (<c>Gate1BootstrapHost</c> does not reference <c>GameReferenceDatabase</c> at all), so the monster/NPC distinction never actually runs there. What that call returns on a live client is <b>not</b> uniformly <c>reference_catalogue_not_loaded</c>: <c>Assess</c> answers from the two evidence short-circuits first (the entity hit us, or we acted on it), then refuses with <c>target_vnum_not_observed</c> when no <c>in</c> packet named the entity -- which is the common case, 25 vnums against 7 685 moves on the reference capture. <c>reference_catalogue_not_loaded</c> is only what is left over: a vnum was observed, neither short-circuit fired, and the catalogue that would classify it is absent. This projection's own classifier is wired separately and does not depend on that gap, but the gap itself is real and still open.</description></item>
/// <item><description><see cref="Player.Skills"/> and item/skill names stay <c>Unknown</c>: no name catalogue exists here either, only the wire's own numeric vnum/slot identifiers.</description></item>
/// <item><description><see cref="Player.Equipment"/> stays empty: <c>InventorySlotReading.InventoryKind</c>'s own remarks now document candidate meanings (OpenNos's <c>InventoryType</c>), but the one this project would need for "worn" -- <c>Wear=8</c> -- is not yet cross-checked against a real equip, and a confirmed kind is now the only half still missing: <c>NosAi.Runtime.GameData.ItemReferenceDecoder</c> does decode which <see cref="EquipmentSlot"/> an item occupies, from <c>Item.dat</c>'s own field (commit <c>e597c0d</c>), and <c>--loadout-report</c> already consumes it. An earlier version of this bullet said no such decode existed; that stopped being true and is corrected here.</description></item>
/// <item><description><see cref="WorldModelSnapshot.Quests"/> stays empty: no quest observation channel exists yet (AP-06).</description></item>
/// </list>
/// </summary>
public static class GameplayObservationProjector
{
    /// <summary>Reason recorded when the player's own map is not yet known, so the snapshot still needs a valid (if placeholder) <see cref="MapId"/>.</summary>
    public const string UnknownMapSentinelId = "unknown-map";

    /// <summary>Reason a projected entity's health carries when the wire stated a fraction and no absolute bound.</summary>
    public const string FractionOnlyHealthReason = "wire_states_fraction_only_no_absolute_bounds";

    /// <summary>Reason recorded on the bounds of an entity's health when the wire stated them in points.</summary>
    public const string AbsoluteVitalsReason = "st_states_current_and_maximum_hp";

    /// <summary>Reason <see cref="Player.Skills"/> carries: no observation channel in this project reads the character's abilities.</summary>
    public const string SkillListNeverReadReason = "skill_list_never_read:no_observation_channel";

    /// <summary>Reason <see cref="Player.Equipment"/> carries: no observation channel in this project reads what the character wears into the World Model.</summary>
    public const string EquipmentNeverReadReason = "equipment_never_read:no_observation_channel";

    /// <summary>
    /// Reason recorded on every health-derived fact of a projected entity:
    /// <see cref="SelectableEntity.ObservedAtUtc"/> is the instant its
    /// <i>position</i> was last stated, and that record carries no separate
    /// instant for its health, so the health's own age cannot be stated here.
    /// </summary>
    public const string HealthInstantNotCarriedReason = "hp_instant_not_carried_by_selectable_entity";

    /// <param name="classifyVnum">
    /// What the reference catalogue says a vnum is, as a <b>pure</b> function
    /// (see <see cref="TargetEstablishment.ClassifyByCatalogue"/>, which is
    /// the predicate this must be built from). Optional and
    /// <see langword="null"/> by default: with no classifier,
    /// <see cref="WorldModelSnapshot.Mobs"/> and
    /// <see cref="WorldModelSnapshot.Npcs"/> stay empty exactly as they did
    /// before this parameter existed -- the same "no source, no change"
    /// treatment <c>WorldModelFusionLoop</c>'s own optional sources use.
    /// <para>
    /// It must be pure and cheap, because this projection is pure and runs
    /// per fusion cycle: a live <c>GameReferenceDatabase</c> handle must
    /// <b>not</b> be captured here directly. That type is a single
    /// <c>SqliteConnection</c> with no synchronisation and no cache, and one
    /// classification costs up to three queries; the caller memoises per vnum
    /// and owns the handle's thread affinity. Passing an impure classifier
    /// would also break this method's own determinism contract, which
    /// <c>GameplayObservationProjectorTests</c> asserts directly.
    /// </para>
    /// </param>
    public static WorldModelSnapshot Project(
        GameplayObservation observation,
        EntityId playerId,
        long version,
        DateTime nowUtc,
        Func<int, CatalogueClass>? classifyVnum = null)
    {
        ArgumentNullException.ThrowIfNull(observation);

        WorldFact<WorldPosition> position = ClassifiedValueBridge.ToWorldFact(observation.PlayerPosition, p => new WorldPosition(p.X, p.Y));
        WorldFact<MapId> currentMap = ClassifiedValueBridge.ToWorldFact(observation.MapId, id => new MapId($"map-{id.ToString(CultureInfo.InvariantCulture)}"));
        WorldFact<bool> isAlive = DeriveIsAlive(observation.Hp);

        var status = new CombatantStatus(
            EquatableArray<Resource>.From(new[]
            {
                new Resource(ResourceKind.Health, ClassifiedValueBridge.ToWorldFact(observation.Hp, v => (double)v), ClassifiedValueBridge.ToWorldFact(observation.MaxHp, v => (double)v)),
                new Resource(ResourceKind.Mana, ClassifiedValueBridge.ToWorldFact(observation.Mp, v => (double)v), ClassifiedValueBridge.ToWorldFact(observation.MaxMp, v => (double)v))
            }),
            EquatableArray<StatusEffect>.Empty);

        WorldFact<EquatableArray<Cooldown>> cooldowns = observation.SkillsReady.HasValue
            ? ClassifiedValueBridge.WithSource(
                observation.SkillsReady.Source,
                EquatableArray<Cooldown>.From(observation.SkillsReady.Value.Select(ready => new Cooldown(
                    new SkillId(ready.Slot.ToString(CultureInfo.InvariantCulture)),
                    ClassifiedValueBridge.WithSource(ready.Source, TimeSpan.Zero, ready.ObservedAtUtc)))),
                observation.SkillsReady.ObservedAtUtc)
            : WorldFact<EquatableArray<Cooldown>>.Unknown(
                observation.SkillsReady.FailureReason ?? "skills_ready_not_published", nowUtc);

        WorldFact<EquatableArray<InventoryItem>> inventory = observation.Inventory.HasValue
            ? ClassifiedValueBridge.WithSource(
                observation.Inventory.Source,
                EquatableArray<InventoryItem>.From(observation.Inventory.Value.Select(slot => new InventoryItem(
                    new ItemId(slot.Vnum.ToString(CultureInfo.InvariantCulture)),
                    WorldFact<string>.Unknown("item_name_catalog_not_available", slot.ObservedAtUtc),
                    ClassifiedValueBridge.WithSource(slot.Source, slot.Amount, slot.ObservedAtUtc),
                    ClassifiedValueBridge.WithSource(slot.Source, slot.Slot, slot.ObservedAtUtc)))),
                observation.Inventory.ObservedAtUtc)
            : WorldFact<EquatableArray<InventoryItem>>.Unknown(
                observation.Inventory.FailureReason ?? "inventory_not_published", nowUtc);

        EquatableArray<Drop> drops = observation.GroundItems.HasValue
            ? EquatableArray<Drop>.From(observation.GroundItems.Value.Select(item => new Drop(
                new EntityId($"drop-{item.DropId.ToString(CultureInfo.InvariantCulture)}"),
                new ItemId(item.Vnum.ToString(CultureInfo.InvariantCulture)),
                ClassifiedValueBridge.WithSource(item.Source, new WorldPosition(item.X, item.Y), item.ObservedAtUtc),
                ClassifiedValueBridge.WithSource(item.Source, item.Amount, item.ObservedAtUtc))))
            : EquatableArray<Drop>.Empty;

        var player = new Player(
            playerId,
            position,
            WorldFact<float>.Unknown("orientation_not_observed", nowUtc),
            isAlive,
            currentMap,
            status,
            // No observation channel in this project reads either list. Stated
            // as Unknown with the reason rather than as an empty array, so a
            // reader is told nothing was looked for instead of being told the
            // character has no abilities and wears nothing.
            WorldFact<EquatableArray<Skill>>.Unknown(SkillListNeverReadReason, nowUtc),
            cooldowns,
            inventory,
            WorldFact<EquatableArray<EquipmentItem>>.Unknown(EquipmentNeverReadReason, nowUtc));

        MapModel map = observation.MapId.HasValue
            ? new MapModel(
                new MapId($"map-{observation.MapId.Value.ToString(CultureInfo.InvariantCulture)}"),
                WorldFact<string>.Unknown("map_name_catalog_not_available", observation.MapId.ObservedAtUtc),
                WorldFact<NosAi.Core.WorldModel.MapBounds>.Unknown("map_bounds_not_yet_reconstructed", observation.MapId.ObservedAtUtc),
                EquatableArray<Tile>.Empty,
                EquatableArray<Portal>.Empty,
                EquatableArray<Polygon>.Empty,
                version,
                nowUtc)
            : MapModel.Unknown(new MapId(UnknownMapSentinelId), observation.MapId.FailureReason ?? "map_id_not_observed", nowUtc);

        (EquatableArray<Mob> mobs, EquatableArray<Npc> npcs) = ProjectEntities(observation, classifyVnum);

        return new WorldModelSnapshot(
            version,
            nowUtc,
            player,
            map,
            mobs,
            npcs,
            drops,
            EquatableArray<Quest>.Empty,
            EquatableArray<WorldAction>.Empty,
            EquatableArray<Goal>.Empty);
    }

    /// <summary>
    /// Splits the observed entity list into the two things the catalogue can
    /// positively establish, and drops everything it cannot.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Only positive answers project.</b> An entity reaches
    /// <see cref="WorldModelSnapshot.Mobs"/> only when the catalogue says its
    /// vnum is a monster, and <see cref="WorldModelSnapshot.Npcs"/> only when
    /// the catalogue says the vnum is one of <c>monster.dat</c>'s RaceType-8
    /// rows. Everything else -- no vnum yet (most entities: only <c>in</c>
    /// carries one), a vnum absent from the table, an unreadable catalogue --
    /// lands in neither list.
    /// </para>
    /// <para>
    /// Deriving the <see cref="Npc"/> list from the <i>residue</i> instead
    /// ("not established as a monster, therefore an NPC") would invert
    /// <see cref="TargetEstablishment"/>'s own safety property
    /// (docs/TASTI_E_BERSAGLIO.md § 6.2): that class is careful never to have
    /// to recognise what it may not attack, and an unclassified entity is
    /// unknown, not benign. So these two lists mean "entities the catalogue
    /// established", never "every entity in view" -- the full, unfiltered set
    /// remains <see cref="GameplayObservation.Entities"/>, which nothing here
    /// discards.
    /// </para>
    /// <para>
    /// <b>Provenance.</b> <see cref="SelectableEntity"/> carries no source of
    /// its own, so the enclosing list's source is used with each entity's own
    /// instant. Health is the exception and is deliberately weakened to
    /// <see cref="DataSourceKind.Cached"/>: that record's
    /// <see cref="SelectableEntity.ObservedAtUtc"/> is when its <i>position</i>
    /// was stated, and a <c>mv</c> packet carries a position with a health
    /// remembered from an older <c>in</c>/<c>st</c>. Labelling that health
    /// Live at the position's instant would claim a freshness never observed,
    /// so it is labelled as what it certainly is -- a value carried forward --
    /// with <see cref="HealthInstantNotCarriedReason"/> naming the cause.
    /// Widening <see cref="SelectableEntity"/> to keep the health's own
    /// instant (and the absolute bounds the <c>st</c> packet already decodes)
    /// is the task specified in
    /// <c>docs/agents/phases/AP-05/AP-05_A2A4_DEEPSEEK_mob_absolute_vitals.md</c>;
    /// until it lands, a fraction over two Unknown bounds is the whole truth.
    /// </para>
    /// </remarks>
    private static (EquatableArray<Mob> Mobs, EquatableArray<Npc> Npcs) ProjectEntities(
        GameplayObservation observation,
        Func<int, CatalogueClass>? classifyVnum)
    {
        if (classifyVnum is null || !observation.Entities.HasValue)
            return (EquatableArray<Mob>.Empty, EquatableArray<Npc>.Empty);

        RuntimeContracts.DataSourceKind source = observation.Entities.Source;
        List<Mob>? mobs = null;
        List<Npc>? npcs = null;

        foreach (SelectableEntity entity in observation.Entities.Value)
        {
            if (entity.Vnum is not { } vnum)
                continue;

            switch (classifyVnum(vnum))
            {
                case CatalogueClass.Monster:
                    (mobs ??= new List<Mob>()).Add(ToMob(entity, vnum, source, EstablishHostility(observation, entity)));
                    break;
                case CatalogueClass.SpecialNonMonsterEntity:
                    (npcs ??= new List<Npc>()).Add(ToNpc(entity, vnum, source));
                    break;
                default:
                    break;
            }
        }

        return (
            mobs is null ? EquatableArray<Mob>.Empty : EquatableArray<Mob>.From(mobs),
            npcs is null ? EquatableArray<Npc>.Empty : EquatableArray<Npc>.From(npcs));
    }

    private static Mob ToMob(
        SelectableEntity entity,
        int vnum,
        RuntimeContracts.DataSourceKind source,
        WorldFact<bool> isHostile)
    {
        WorldFact<double> healthFraction = entity.HpRatio is { } ratio
            ? WorldFact<double>.Cached(ratio, 1.0, entity.ObservedAtUtc, HealthInstantNotCarriedReason)
            : WorldFact<double>.Unknown("hp_never_stated_for_this_entity", entity.ObservedAtUtc);

        CombatantStatus status = BuildHealth(entity, healthFraction);

        return new Mob(
            new EntityId(string.Create(CultureInfo.InvariantCulture, $"mob-{entity.EntityId}")),
            ClassifiedValueBridge.WithSource(source, new WorldPosition(entity.At.X, entity.At.Y), entity.ObservedAtUtc),

            // The catalogue established what the vnum is, not what it is called:
            // no name table is read here, the same gap the map and item names
            // above already carry. The vnum travels in the reason so the
            // identity is not lost while the name is missing.
            WorldFact<string>.Unknown(
                string.Create(CultureInfo.InvariantCulture, $"species_name_catalog_not_available:vnum={vnum}"),
                entity.ObservedAtUtc),

            isHostile,

            DeriveEntityIsAlive(healthFraction),
            status);
    }

    /// <summary>
    /// Whether this entity is hostile, established only by the wire's own
    /// record of it having hit the controlled character.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Being in <c>monster.dat</c> is not being hostile. <see cref="Mob"/>'s
    /// own contract covers "hostile or neutral", many NosTale monsters are
    /// passive until struck, and the hostility code in that file's
    /// <c>PREATT</c> block (decoded as <c>MonsterReference.Hostility</c>) has
    /// never been cross-checked against a real client in this repository --
    /// promoting it to a fact here would be exactly the guess the catalogue is
    /// consulted to avoid.
    /// </para>
    /// <para>
    /// <see cref="GameplayObservation.HitBy"/> is different in kind: the
    /// decoder publishes it only after establishing, from <c>cond</c>'s own
    /// entity id, that the <c>su</c> it read was aimed at <i>this</i>
    /// character. An entity that hit us is beyond doubt something that
    /// fights -- the same reasoning that makes
    /// <see cref="TargetEstablishment.Assess"/> rank
    /// <c>TargetEvidence.AttackedUs</c> above a catalogue lookup.
    /// </para>
    /// <para>
    /// <see cref="GameplayObservation.SelectedTarget"/> is deliberately
    /// <b>not</b> consulted: the character having acted on an entity says the
    /// client accepted it as a target, not that the entity is hostile -- an
    /// NPC can be selected too.
    /// </para>
    /// <para>
    /// The wire names one aggressor, the most recent, so at most one mob per
    /// cycle is established this way. <c>WorldModelTemporalEnricher</c> carries
    /// that answer forward across cycles as <see cref="DataSourceKind.Cached"/>
    /// once established; this method only ever states what the current
    /// observation says.
    /// </para>
    /// </remarks>
    private static WorldFact<bool> EstablishHostility(GameplayObservation observation, SelectableEntity entity)
    {
        if (observation.HitBy is { HasValue: true } aggressor && aggressor.Value.EntityId == entity.EntityId)
            return ClassifiedValueBridge.WithSource(observation.HitBy.Source, true, observation.HitBy.ObservedAtUtc);

        return WorldFact<bool>.Unknown("hostility_never_established:no_observed_hit_from_this_entity", entity.ObservedAtUtc);
    }

    private static Npc ToNpc(SelectableEntity entity, int vnum, RuntimeContracts.DataSourceKind source) =>
        new(
            new EntityId(string.Create(CultureInfo.InvariantCulture, $"npc-{entity.EntityId}")),
            ClassifiedValueBridge.WithSource(source, new WorldPosition(entity.At.X, entity.At.Y), entity.ObservedAtUtc),
            WorldFact<string>.Unknown(
                string.Create(CultureInfo.InvariantCulture, $"npc_name_catalog_not_available:vnum={vnum}"),
                entity.ObservedAtUtc),

            // Whether this NPC has something to offer right now is a dialogue
            // state nothing on this channel reports; empty is not "nothing to
            // offer".
            WorldFact<bool>.Unknown("npc_interaction_availability_not_observed", entity.ObservedAtUtc));

    /// <summary>
    /// Alive/dead from an observed health fraction, or Unknown when none was
    /// ever stated.
    /// </summary>
    /// <remarks>
    /// A fraction of exactly zero is the wire saying the entity has no health
    /// left, which the decoder already treats as dead
    /// (<c>TargetSelector</c> and <c>Gate3Runtime</c> both skip
    /// <c>HpRatio is &lt;= 0</c>). An absent fraction is not zero and not
    /// alive: it is the absence of any statement, so nothing is concluded --
    /// the same boundary <see cref="DeriveIsAlive"/> draws for the player.
    /// </remarks>
    private static WorldFact<bool> DeriveEntityIsAlive(WorldFact<double> healthFraction) =>
        healthFraction.HasValue
            ? WorldFact<bool>.Derived(healthFraction.Value > 0, 1.0, healthFraction.ObservedAtUtc, HealthInstantNotCarriedReason)
            : WorldFact<bool>.Unknown(healthFraction.Reason ?? "hp_never_stated_for_this_entity", healthFraction.ObservedAtUtc);

    private static WorldFact<bool> DeriveIsAlive(RuntimeContracts.ClassifiedValue<int> hp)
    {
        if (!hp.HasValue)
            return WorldFact<bool>.Unknown(hp.FailureReason ?? "hp_not_observed", hp.ObservedAtUtc);

        return WorldFact<bool>.Derived(hp.Value > 0, 1.0, hp.ObservedAtUtc);
    }


    /// <summary>
    /// An entity's health, in points when the wire stated points and as a bare
    /// fraction when it did not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>st</c> packet states a monster's current and maximum hit points
    /// and the decoder keeps both (<see cref="SelectableEntity.Vitals"/>), so
    /// where they exist the <see cref="Resource"/> is built from them and its
    /// <see cref="Resource.Fraction"/> is derived by the contract itself. Where
    /// they do not -- an entity seen only through <c>in</c>, which states a
    /// percentage, or through <c>mv</c>, which states no health at all -- the
    /// fraction is the whole observation and
    /// <see cref="Resource.FromObservedFraction"/> says so by leaving both
    /// bounds Unknown with a named reason. Neither shape guesses the other.
    /// </para>
    /// <para>
    /// Both bounds carry <see cref="HealthInstantNotCarriedReason"/>'s instant
    /// for the same reason the fraction does:
    /// <see cref="SelectableEntity.ObservedAtUtc"/> is the <i>position</i>'s
    /// instant, and that record carries no separate one for the health. They are
    /// <c>Cached</c>, not <c>Live</c>, for exactly that reason -- the numbers are
    /// real and their age is not known here.
    /// </para>
    /// </remarks>
    private static CombatantStatus BuildHealth(SelectableEntity entity, WorldFact<double> healthFraction)
    {
        if (entity.Vitals is { } points)
        {
            var health = new Resource(
                ResourceKind.Health,
                WorldFact<double>.Cached(points.Current, 1.0, entity.ObservedAtUtc, AbsoluteVitalsReason),
                WorldFact<double>.Cached(points.Maximum, 1.0, entity.ObservedAtUtc, AbsoluteVitalsReason));

            return new CombatantStatus(
                EquatableArray<Resource>.From(new[] { health }),
                EquatableArray<StatusEffect>.Empty);
        }

        return healthFraction.HasValue
            ? new CombatantStatus(
                EquatableArray<Resource>.From(new[]
                {
                    Resource.FromObservedFraction(ResourceKind.Health, healthFraction, FractionOnlyHealthReason)
                }),
                EquatableArray<StatusEffect>.Empty)
            : CombatantStatus.Empty;
    }

}
