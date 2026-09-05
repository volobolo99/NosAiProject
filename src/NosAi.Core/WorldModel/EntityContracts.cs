namespace NosAi.Core.WorldModel;

/// <summary>
/// The part of combat state shared by anything that can fight or be fought
/// (a <see cref="Player"/> or a <see cref="Mob"/>): resource pools and
/// active status effects. Composition over inheritance: both entity types
/// hold one of these rather than sharing a base class, keeping both simple
/// immutable records.
/// </summary>
public sealed record CombatantStatus(
    EquatableArray<Resource> Resources,
    EquatableArray<StatusEffect> ActiveEffects)
{
    /// <summary>A combatant with no known resources or effects yet (e.g. an entity seen only once, before its HUD/stat panel has been read).</summary>
    public static CombatantStatus Empty { get; } = new(EquatableArray<Resource>.Empty, EquatableArray<StatusEffect>.Empty);
}

/// <summary>The controlled character. Exactly one <see cref="Player"/> exists per World Model snapshot.</summary>
public sealed record Player(
    EntityId Id,
    WorldFact<WorldPosition> Position,
    WorldFact<float> OrientationRadians,
    WorldFact<bool> IsAlive,
    WorldFact<MapId> CurrentMap,
    CombatantStatus Status,
    EquatableArray<Skill> Skills,
    EquatableArray<Cooldown> Cooldowns,
    EquatableArray<InventoryItem> Inventory,
    EquatableArray<EquipmentItem> Equipment)
{
    /// <summary>
    /// A single shared Unknown instance for <see cref="Velocity"/>'s default.
    /// <see cref="WorldFact{T}.Unknown"/> stamps <see cref="DateTime.UtcNow"/>
    /// when no instant is given, which would make every default-constructed
    /// <see cref="Player"/> carry a different, wall-clock-dependent
    /// <see cref="WorldFact{T}.ObservedAtUtc"/> and break value equality
    /// between two otherwise-identical players (AP-01's "replay
    /// deterministico" requirement) -- a fixed sentinel instant, computed
    /// once, keeps the default itself deterministic.
    /// </summary>
    private static readonly WorldFact<WorldVelocity> UnderivedVelocity =
        WorldFact<WorldVelocity>.Unknown("not_yet_derived", DateTime.UnixEpoch);

    /// <summary>
    /// Estimated movement rate, derived across two fusion cycles by
    /// <c>NosAi.Core.WorldModel.Temporal.WorldModelTemporalEnricher</c> --
    /// never set by Sensor Fusion itself (AP-01/A2), which only ever sees one
    /// instant at a time. An init-only addition (not a positional parameter)
    /// so every existing construction site keeps compiling, the same
    /// treatment <c>GameplayObservation</c>'s own additive fields already use.
    /// </summary>
    public WorldFact<WorldVelocity> Velocity { get; init; } = UnderivedVelocity;
}

/// <summary>A hostile or neutral non-player creature.</summary>
public sealed record Mob(
    EntityId Id,
    WorldFact<WorldPosition> Position,
    WorldFact<string> Species,
    WorldFact<bool> IsHostile,
    WorldFact<bool> IsAlive,
    CombatantStatus Status)
{
    /// <summary>See the remarks on <see cref="Player.UnderivedVelocity"/>: a fixed sentinel instant keeps this default deterministic.</summary>
    private static readonly WorldFact<WorldVelocity> UnderivedVelocity =
        WorldFact<WorldVelocity>.Unknown("not_yet_derived", DateTime.UnixEpoch);

    /// <summary>Estimated movement rate. See the remarks on <see cref="Player.Velocity"/>: derived temporally, never set by Sensor Fusion.</summary>
    public WorldFact<WorldVelocity> Velocity { get; init; } = UnderivedVelocity;
}

/// <summary>A non-hostile, non-player character offering dialogue/services (quest giver, vendor, ...).</summary>
public sealed record Npc(
    EntityId Id,
    WorldFact<WorldPosition> Position,
    WorldFact<string> Name,
    WorldFact<bool> HasAvailableInteraction);

/// <summary>An item lying in the world, not yet picked up.</summary>
public sealed record Drop(
    EntityId Id,
    ItemId Item,
    WorldFact<WorldPosition> Position,
    WorldFact<int> Quantity);
