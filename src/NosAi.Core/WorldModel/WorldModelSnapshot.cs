namespace NosAi.Core.WorldModel;

/// <summary>
/// The single, self-consistent, point-in-time Unified World Model consumed
/// by every downstream planning stage (docs/NOSAI_ARCHITECTURE_BASELINE.md
/// S:3 "World Model": "Single semantic state for planning"). Produced by
/// Sensor Fusion (AP-01/A2) from Network/Memory/Screen/Local observations;
/// this type only defines its shape.
///
/// <see cref="Version"/> increases monotonically with every fused update,
/// mirroring the existing convention on <c>NosAi.Core.PlannerWorldState.Version</c>.
/// Because every field here is either a value type, an immutable record, or
/// an <see cref="EquatableArray{T}"/> (never a bare array/list), two
/// snapshots built from the same fused inputs at the same instant compare
/// equal -- the deterministic-replay requirement in
/// docs/ROADMAP_ESECUTIVA.md S:AP-01's Definition of Done.
/// </summary>
public sealed record WorldModelSnapshot(
    long Version,
    DateTime ObservedAtUtc,
    Player Player,
    MapModel Map,
    EquatableArray<Mob> Mobs,
    EquatableArray<Npc> Npcs,
    EquatableArray<Drop> Drops,
    EquatableArray<Quest> Quests,
    EquatableArray<WorldAction> RecentActions,
    EquatableArray<Goal> ActiveGoals)
{
    /// <summary>
    /// The most recent drop observed being taken, with whether the controlled character is
    /// the one who took it. This is the observation that closes an executed pickup: on its
    /// own, a drop's disappearance from the ground cannot be told apart from somebody else
    /// having claimed it first.
    /// </summary>
    /// <remarks>
    /// Unknown until a pickup is observed, stamped at <see cref="DateTime.UnixEpoch"/> so
    /// that two snapshots which never saw one stay equal to each other. Nobody having
    /// picked anything up is not the same fact as nobody having watched.
    /// </remarks>
    public WorldFact<DropClaim> LastDropClaim { get; init; }
        = WorldFact<DropClaim>.Unknown("no_pickup_observed_yet", DateTime.UnixEpoch);

    /// <summary>
    /// A snapshot with an explicitly Unknown player/map and empty
    /// collections, used before the first successful fusion cycle -- never
    /// replaced by a snapshot fabricating a plausible player/map/entity
    /// list that was not actually observed.
    /// </summary>
    public static WorldModelSnapshot Unknown(string reason, DateTime? observedAtUtc = null)
    {
        DateTime now = observedAtUtc ?? DateTime.UtcNow;
        var unknownPlayerId = new EntityId("unknown-player");
        var unknownMapId = new MapId("unknown-map");

        var unknownPlayer = new Player(
            unknownPlayerId,
            WorldFact<WorldPosition>.Unknown(reason, now),
            WorldFact<float>.Unknown(reason, now),
            WorldFact<bool>.Unknown(reason, now),
            WorldFact<MapId>.Unknown(reason, now),
            CombatantStatus.Empty,

            // Four Unknowns, not four empty lists. A snapshot whose every other
            // fact is Unknown(reason) used to assert, positively, that the
            // character had no skills, no cooldowns, nothing carried and nothing
            // worn -- four claims nobody had observed, in the one object whose
            // name says it knows nothing.
            WorldFact<EquatableArray<Skill>>.Unknown(reason, now),
            WorldFact<EquatableArray<Cooldown>>.Unknown(reason, now),
            WorldFact<EquatableArray<InventoryItem>>.Unknown(reason, now),
            WorldFact<EquatableArray<EquipmentItem>>.Unknown(reason, now));

        return new WorldModelSnapshot(
            Version: 0,
            now,
            unknownPlayer,
            MapModel.Unknown(unknownMapId, reason, now),
            EquatableArray<Mob>.Empty,
            EquatableArray<Npc>.Empty,
            EquatableArray<Drop>.Empty,
            EquatableArray<Quest>.Empty,
            EquatableArray<WorldAction>.Empty,
            EquatableArray<Goal>.Empty);
    }
}
