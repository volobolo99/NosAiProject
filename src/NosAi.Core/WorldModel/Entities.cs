using System.Collections.Immutable;

namespace NosAi.Core.WorldModel;

/// <summary>Facing direction as the client encodes it (0..7, clockwise from north).</summary>
public enum Orientation : byte
{
    North = 0,
    NorthEast = 1,
    East = 2,
    SouthEast = 3,
    South = 4,
    SouthWest = 5,
    West = 6,
    NorthWest = 7
}

/// <summary>Coarse taxonomy of a thing on the map.</summary>
public enum EntityKind : byte
{
    Unknown = 0,
    Player = 1,
    Mob = 2,
    Npc = 3,
    Drop = 4,
    Portal = 5
}

/// <summary>Whether an entity is currently in view, remembered from an earlier view, or known gone.</summary>
public enum EntityPresence : byte
{
    Unknown = 0,
    Visible = 1,
    Remembered = 2,
    Gone = 3
}

/// <summary>A pair of current/maximum values (HP, MP). Ratio is null when the maximum is unknown or zero.</summary>
public readonly record struct Vital(int Current, int Maximum)
{
    public bool IsValid => Maximum > 0 && Current >= 0 && Current <= Maximum;
    public float? Ratio => Maximum > 0 ? Math.Clamp((float)Current / Maximum, 0f, 1f) : null;
}

/// <summary>
/// The controlled character. Every field is a fact: a snapshot with an UNKNOWN
/// HP is a legal snapshot that the planner must refuse to fight from.
/// </summary>
public sealed record PlayerState(
    WorldFact<EntityId> Id,
    WorldFact<string> Name,
    WorldFact<int> Level,
    WorldFact<int> JobLevel,
    WorldFact<MapCell> Position,
    WorldFact<Orientation> Facing,
    WorldFact<Vital> Hp,
    WorldFact<Vital> Mp,
    WorldFact<bool> Alive,
    WorldFact<bool> InCombat,
    WorldFact<EntityId> Target,
    WorldFact<EntityId> LastAggressor)
{
    public static PlayerState NotObserved(long atUnixMillis) => new(
        WorldFact<EntityId>.NotObserved(atUnixMillis),
        WorldFact<string>.NotObserved(atUnixMillis),
        WorldFact<int>.NotObserved(atUnixMillis),
        WorldFact<int>.NotObserved(atUnixMillis),
        WorldFact<MapCell>.NotObserved(atUnixMillis),
        WorldFact<Orientation>.NotObserved(atUnixMillis),
        WorldFact<Vital>.NotObserved(atUnixMillis),
        WorldFact<Vital>.NotObserved(atUnixMillis),
        WorldFact<bool>.NotObserved(atUnixMillis),
        WorldFact<bool>.NotObserved(atUnixMillis),
        WorldFact<EntityId>.NotObserved(atUnixMillis),
        WorldFact<EntityId>.NotObserved(atUnixMillis));

    /// <summary>Whether the minimum needed to reason about survival is known: HP, MP and alive.</summary>
    public bool HasVitals => Hp.HasValue && Mp.HasValue && Alive.HasValue;

    /// <summary>HP ratio when known, else null. Never zero for an unknown HP.</summary>
    public float? HpRatio => Hp.TryGetValue(out var hp) ? hp.Ratio : null;
}

/// <summary>A hostile or neutral creature.</summary>
public sealed record MobState(
    EntityId Id,
    WorldFact<int> CatalogueId,
    WorldFact<string> Name,
    WorldFact<int> Level,
    WorldFact<MapCell> Position,
    WorldFact<Vital> Hp,
    WorldFact<bool> Hostile,
    WorldFact<bool> TargetingPlayer,
    WorldFact<EntityPresence> Presence)
{
    public bool IsVisible => Presence.TryGetValue(out var p) && p == EntityPresence.Visible;
}

/// <summary>A non-player character or interactable.</summary>
public sealed record NpcState(
    EntityId Id,
    WorldFact<int> CatalogueId,
    WorldFact<string> Name,
    WorldFact<MapCell> Position,
    WorldFact<bool> Interactable,
    WorldFact<EntityPresence> Presence);

/// <summary>An item lying on the ground.</summary>
public sealed record DropState(
    EntityId Id,
    WorldFact<ItemId> Item,
    WorldFact<int> Quantity,
    WorldFact<MapCell> Position,
    WorldFact<EntityId> Owner,
    WorldFact<EntityPresence> Presence);

/// <summary>Sorted, immutable collections of entities keyed by id for deterministic iteration.</summary>
public static class EntityCollections
{
    public static ImmutableArray<MobState> SortById(IEnumerable<MobState> mobs)
        => mobs.OrderBy(m => m.Id.Value).ToImmutableArray();

    public static ImmutableArray<NpcState> SortById(IEnumerable<NpcState> npcs)
        => npcs.OrderBy(n => n.Id.Value).ToImmutableArray();

    public static ImmutableArray<DropState> SortById(IEnumerable<DropState> drops)
        => drops.OrderBy(d => d.Id.Value).ToImmutableArray();

    public static bool TryFind(ImmutableArray<MobState> mobs, EntityId id, out MobState mob)
    {
        if (!mobs.IsDefaultOrEmpty)
        {
            foreach (var candidate in mobs)
            {
                if (candidate.Id == id)
                {
                    mob = candidate;
                    return true;
                }
            }
        }

        mob = null!;
        return false;
    }
}
