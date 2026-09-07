using System.Globalization;
using NosAi.Core.WorldModel;
using NosAi.LiveIntegration;
using RuntimeContracts = NosAi.Runtime.Contracts;

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
/// <item><description><see cref="WorldModelSnapshot.Mobs"/>/<see cref="WorldModelSnapshot.Npcs"/> stay empty here, but the reason is narrower than "no classification exists": the wire's entity type 3 really is monster and NPC together (<c>NosAi.Runtime.Autonomy.SelectableEntity.Vnum</c>'s own remark), but <c>NosAi.Runtime.Autonomy.TargetEstablishment.Assess</c> already resolves exactly this ambiguity for combat authorization, via <c>GameReferenceDatabase.Exists("monster", vnum)</c>, and is already wired into the live targeting path (<c>Gate3Runtime</c>). What is missing is only wiring the same lookup <i>here</i>, to turn <see cref="GameplayObservation.Entities"/>'s <c>SelectableEntity</c> list into real <see cref="Mob"/> records for this projection specifically -- a mechanical gap, not a classification one.</description></item>
/// <item><description><see cref="Player.Skills"/> and item/skill names stay <c>Unknown</c>: no name catalogue exists here either, only the wire's own numeric vnum/slot identifiers.</description></item>
/// <item><description><see cref="Player.Equipment"/> stays empty: <c>InventorySlotReading.InventoryKind</c>'s own remarks now document candidate meanings (OpenNos's <c>InventoryType</c>), but the one this project would need for "worn" -- <c>Wear=8</c> -- is not yet cross-checked against a real equip, and even a confirmed kind would still not name *which* <see cref="EquipmentSlot"/> an item occupies (no item-category decode exists -- see <c>AP-07_A1_STATUS.md</c>). Populating this needs both, not just the kind.</description></item>
/// <item><description><see cref="WorldModelSnapshot.Quests"/> stays empty: no quest observation channel exists yet (AP-06).</description></item>
/// </list>
/// </summary>
public static class GameplayObservationProjector
{
    /// <summary>Reason recorded when the player's own map is not yet known, so the snapshot still needs a valid (if placeholder) <see cref="MapId"/>.</summary>
    public const string UnknownMapSentinelId = "unknown-map";

    public static WorldModelSnapshot Project(GameplayObservation observation, EntityId playerId, long version, DateTime nowUtc)
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

        EquatableArray<Cooldown> cooldowns = observation.SkillsReady.HasValue
            ? EquatableArray<Cooldown>.From(observation.SkillsReady.Value.Select(ready => new Cooldown(
                new SkillId(ready.Slot.ToString(CultureInfo.InvariantCulture)),
                ClassifiedValueBridge.WithSource(ready.Source, TimeSpan.Zero, ready.ObservedAtUtc))))
            : EquatableArray<Cooldown>.Empty;

        EquatableArray<InventoryItem> inventory = observation.Inventory.HasValue
            ? EquatableArray<InventoryItem>.From(observation.Inventory.Value.Select(slot => new InventoryItem(
                new ItemId(slot.Vnum.ToString(CultureInfo.InvariantCulture)),
                WorldFact<string>.Unknown("item_name_catalog_not_available", slot.ObservedAtUtc),
                ClassifiedValueBridge.WithSource(slot.Source, slot.Amount, slot.ObservedAtUtc),
                ClassifiedValueBridge.WithSource(slot.Source, slot.Slot, slot.ObservedAtUtc))))
            : EquatableArray<InventoryItem>.Empty;

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
            EquatableArray<Skill>.Empty,
            cooldowns,
            inventory,
            EquatableArray<EquipmentItem>.Empty);

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

        return new WorldModelSnapshot(
            version,
            nowUtc,
            player,
            map,
            EquatableArray<Mob>.Empty,
            EquatableArray<Npc>.Empty,
            drops,
            EquatableArray<Quest>.Empty,
            EquatableArray<WorldAction>.Empty,
            EquatableArray<Goal>.Empty);
    }

    private static WorldFact<bool> DeriveIsAlive(RuntimeContracts.ClassifiedValue<int> hp)
    {
        if (!hp.HasValue)
            return WorldFact<bool>.Unknown(hp.FailureReason ?? "hp_not_observed", hp.ObservedAtUtc);

        return WorldFact<bool>.Derived(hp.Value > 0, 1.0, hp.ObservedAtUtc);
    }

}
