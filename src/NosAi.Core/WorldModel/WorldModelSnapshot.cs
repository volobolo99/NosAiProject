using System.Collections.Immutable;
using System.Globalization;

namespace NosAi.Core.WorldModel;

/// <summary>
/// The AP-01 Unified World Model: one immutable, versioned, semantic state
/// consumed by simulation, ranking and planning.
/// </summary>
/// <remarks>
/// <para>
/// Every important fact is a <see cref="WorldFact{T}"/> with provenance,
/// confidence, timestamp and freshness. Collections are sorted by identity so
/// two snapshots built from the same observations are value-equal and yield
/// the same <see cref="ComputeDigest"/>, which is what deterministic replay
/// checks against.
/// </para>
/// <para>
/// The snapshot holds no privileged data: no memory addresses, handles, raw
/// packets, credentials or server-side state. It proposes nothing and
/// authorizes nothing; Guard/Trust/Safety decide downstream.
/// </para>
/// </remarks>
public sealed record WorldModelSnapshot(
    int SchemaVersion,
    long Revision,
    long ObservedAtUnixMillis,
    PlayerState Player,
    MapState Map,
    ImmutableArray<MobState> Mobs,
    ImmutableArray<NpcState> Npcs,
    ImmutableArray<DropState> Drops,
    ImmutableArray<QuestState> Quests,
    ImmutableArray<InventoryItem> Inventory,
    ImmutableArray<EquipmentItem> Equipment,
    ImmutableArray<SkillState> Skills,
    ImmutableArray<StatusEffectState> StatusEffects,
    ImmutableArray<CooldownState> Cooldowns,
    ImmutableArray<ResourceState> Resources,
    ImmutableArray<ActionRecord> Actions,
    ImmutableArray<GoalRecord> Goals)
{
    /// <summary>A snapshot in which nothing has been observed. Every fact is UNKNOWN with reason "not observed".</summary>
    public static WorldModelSnapshot Empty(long atUnixMillis, long revision = 0) => new(
        WorldModelContract.SchemaVersion,
        revision,
        atUnixMillis,
        PlayerState.NotObserved(atUnixMillis),
        MapState.NotObserved(atUnixMillis),
        ImmutableArray<MobState>.Empty,
        ImmutableArray<NpcState>.Empty,
        ImmutableArray<DropState>.Empty,
        ImmutableArray<QuestState>.Empty,
        ImmutableArray<InventoryItem>.Empty,
        ImmutableArray<EquipmentItem>.Empty,
        ImmutableArray<SkillState>.Empty,
        ImmutableArray<StatusEffectState>.Empty,
        ImmutableArray<CooldownState>.Empty,
        ImmutableArray<ResourceState>.Empty,
        ImmutableArray<ActionRecord>.Empty,
        ImmutableArray<GoalRecord>.Empty);

    public IEnumerable<StatusEffectState> Buffs
        => StatusEffects.IsDefaultOrEmpty ? [] : StatusEffects.Where(e => e.Polarity == StatusEffectPolarity.Buff);

    public IEnumerable<StatusEffectState> Debuffs
        => StatusEffects.IsDefaultOrEmpty ? [] : StatusEffects.Where(e => e.Polarity == StatusEffectPolarity.Debuff);

    /// <summary>The next revision of this snapshot, stamped at <paramref name="atUnixMillis"/>. Content is unchanged; callers apply <c>with</c> afterwards.</summary>
    public WorldModelSnapshot Advance(long atUnixMillis)
    {
        if (atUnixMillis < ObservedAtUnixMillis)
        {
            throw new ArgumentOutOfRangeException(nameof(atUnixMillis), atUnixMillis, "A revision cannot be stamped before its predecessor.");
        }

        return this with { Revision = checked(Revision + 1), ObservedAtUnixMillis = atUnixMillis };
    }

    /// <summary>Every fact in the snapshot, in a deterministic order.</summary>
    public IEnumerable<IWorldFact> Facts()
    {
        yield return Player.Id;
        yield return Player.Name;
        yield return Player.Level;
        yield return Player.JobLevel;
        yield return Player.Position;
        yield return Player.Facing;
        yield return Player.Hp;
        yield return Player.Mp;
        yield return Player.Alive;
        yield return Player.InCombat;
        yield return Player.Target;
        yield return Player.LastAggressor;

        yield return Map.Id;
        yield return Map.Name;
        yield return Map.Bounds;
        foreach (var tile in Map.Tiles.OrEmpty()) yield return tile.State;
        foreach (var region in Map.Regions.OrEmpty()) yield return region.State;
        foreach (var portal in Map.Portals.OrEmpty())
        {
            yield return portal.Cell;
            yield return portal.DestinationMap;
            yield return portal.DestinationCell;
        }

        foreach (var mob in Mobs.OrEmpty())
        {
            yield return mob.CatalogueId;
            yield return mob.Name;
            yield return mob.Level;
            yield return mob.Position;
            yield return mob.Hp;
            yield return mob.Hostile;
            yield return mob.TargetingPlayer;
            yield return mob.Presence;
        }

        foreach (var npc in Npcs.OrEmpty())
        {
            yield return npc.CatalogueId;
            yield return npc.Name;
            yield return npc.Position;
            yield return npc.Interactable;
            yield return npc.Presence;
        }

        foreach (var drop in Drops.OrEmpty())
        {
            yield return drop.Item;
            yield return drop.Quantity;
            yield return drop.Position;
            yield return drop.Owner;
            yield return drop.Presence;
        }

        foreach (var quest in Quests.OrEmpty())
        {
            yield return quest.Title;
            yield return quest.Status;
            foreach (var objective in quest.Objectives.OrEmpty())
            {
                yield return objective.Description;
                yield return objective.Progress;
                yield return objective.Required;
            }
        }

        foreach (var item in Inventory.OrEmpty())
        {
            yield return item.Item;
            yield return item.Quantity;
            yield return item.Rarity;
            yield return item.Upgrade;
        }

        foreach (var item in Equipment.OrEmpty())
        {
            yield return item.Item;
            yield return item.Rarity;
            yield return item.Upgrade;
            yield return item.Durability;
        }

        foreach (var skill in Skills.OrEmpty())
        {
            yield return skill.Name;
            yield return skill.MpCost;
            yield return skill.Range;
            yield return skill.CastTimeMillis;
            yield return skill.CooldownMillis;
            yield return skill.Ready;
            yield return skill.ReadyAtUnixMillis;
        }

        foreach (var effect in StatusEffects.OrEmpty())
        {
            yield return effect.Name;
            yield return effect.Level;
            yield return effect.AppliedAtUnixMillis;
            yield return effect.ExpiresAtUnixMillis;
        }

        foreach (var cooldown in Cooldowns.OrEmpty())
        {
            yield return cooldown.StartedAtUnixMillis;
            yield return cooldown.ReadyAtUnixMillis;
        }

        foreach (var resource in Resources.OrEmpty())
        {
            yield return resource.Amount;
            yield return resource.Maximum;
        }

        foreach (var action in Actions.OrEmpty())
        {
            yield return action.Availability;
            yield return action.LastAttemptedAtUnixMillis;
            yield return action.LastOutcome;
            yield return action.ConsecutiveFailures;
        }

        foreach (var goal in Goals.OrEmpty())
        {
            yield return goal.Status;
            yield return goal.Progress;
            yield return goal.DeadlineUnixMillis;
        }
    }

    /// <summary>Whether any known fact came from a simulation. Planning may use such a snapshot; acting on it is forbidden.</summary>
    public bool HasSimulatedFact => Facts().Any(f => f.HasValue && f.Source == FactSource.Simulated);

    /// <summary>Whether every known fact is one the runtime may act on (LIVE/DERIVED/CACHED, never SIMULATED).</summary>
    public bool IsActionable => Facts().All(f => !f.HasValue || f.Source.IsActionable());

    /// <summary>Number of facts with an observed value.</summary>
    public int KnownFactCount => Facts().Count(f => f.HasValue);

    /// <summary>Number of UNKNOWN facts.</summary>
    public int UnknownFactCount => Facts().Count(f => !f.HasValue);

    /// <summary>Whether the survival-critical facts are fresh at <paramref name="nowUnixMillis"/>: player HP, MP, alive and position.</summary>
    public bool AreVitalsFreshAt(long nowUnixMillis, long maxAgeMillis)
        => Player.Hp.FreshnessAt(nowUnixMillis, maxAgeMillis) == FactFreshness.Fresh
           && Player.Mp.FreshnessAt(nowUnixMillis, maxAgeMillis) == FactFreshness.Fresh
           && Player.Alive.FreshnessAt(nowUnixMillis, maxAgeMillis) == FactFreshness.Fresh
           && Player.Position.FreshnessAt(nowUnixMillis, maxAgeMillis) == FactFreshness.Fresh;

    /// <summary>
    /// Structural violations, empty when the snapshot is well-formed. Checked
    /// by <see cref="ThrowIfInvalid"/> at the boundary where sensor fusion
    /// hands a snapshot to planning.
    /// </summary>
    public ImmutableArray<string> Validate()
    {
        var problems = ImmutableArray.CreateBuilder<string>();

        if (SchemaVersion != WorldModelContract.SchemaVersion)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"schema version {SchemaVersion} does not match contract {WorldModelContract.SchemaVersion}"));
        }

        if (Revision < 0)
        {
            problems.Add("revision is negative");
        }

        if (ObservedAtUnixMillis < 0)
        {
            problems.Add("observedAt is negative");
        }

        if (Player is null) problems.Add("player is null");
        if (Map is null) problems.Add("map is null");

        if (Player is not null)
        {
            if (Player.Hp.TryGetValue(out var hp) && !hp.IsValid) problems.Add("player hp is not a valid vital");
            if (Player.Mp.TryGetValue(out var mp) && !mp.IsValid) problems.Add("player mp is not a valid vital");
            if (Player.Level.TryGetValue(out var level) && level < 1) problems.Add("player level below 1");
        }

        if (Map is not null)
        {
            if (Map.Bounds.TryGetValue(out var bounds) && !bounds.IsValid) problems.Add("map bounds are not strictly positive");
            if (Map.Bounds.TryGetValue(out var b) && b.IsValid)
            {
                foreach (var tile in Map.Tiles.OrEmpty())
                {
                    if (!b.Contains(tile.Cell))
                    {
                        problems.Add(string.Create(CultureInfo.InvariantCulture, $"tile {tile.Cell} outside map bounds {b.Width}x{b.Height}"));
                        break;
                    }
                }
            }

            CheckDistinct(Map.Tiles.OrEmpty().Select(t => t.Cell), "tile cell", problems);
            CheckDistinct(Map.Portals.OrEmpty().Select(p => p.Id), "portal id", problems);
        }

        CheckSortedDistinct(Mobs.OrEmpty().Select(m => m.Id.Value), "mob id", problems);
        CheckSortedDistinct(Npcs.OrEmpty().Select(n => n.Id.Value), "npc id", problems);
        CheckSortedDistinct(Drops.OrEmpty().Select(d => d.Id.Value), "drop id", problems);
        CheckDistinct(Quests.OrEmpty().Select(q => q.Id), "quest id", problems);
        CheckDistinct(Inventory.OrEmpty().Select(i => (i.Bag, i.Slot)), "inventory slot", problems);
        CheckDistinct(Equipment.OrEmpty().Select(e => e.Slot), "equipment slot", problems);
        CheckDistinct(Skills.OrEmpty().Select(s => s.Id), "skill id", problems);
        CheckDistinct(StatusEffects.OrEmpty().Select(e => (e.Id, e.Polarity)), "status effect", problems);
        CheckDistinct(Cooldowns.OrEmpty().Select(c => (c.Scope, c.Key)), "cooldown", problems);
        CheckDistinct(Resources.OrEmpty().Select(r => r.Kind), "resource kind", problems);
        CheckDistinct(Actions.OrEmpty().Select(a => a.Id), "action id", problems);
        CheckDistinct(Goals.OrEmpty().Select(g => g.Id), "goal id", problems);

        foreach (var mob in Mobs.OrEmpty())
        {
            if (mob.Hp.TryGetValue(out var mobHp) && !mobHp.IsValid)
            {
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"mob {mob.Id} hp is not a valid vital"));
            }
        }

        foreach (var item in Inventory.OrEmpty())
        {
            if (item.Quantity.TryGetValue(out var qty) && qty < 0)
            {
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"inventory {item.Bag}/{item.Slot} quantity is negative"));
            }
        }

        foreach (var goal in Goals.OrEmpty())
        {
            if (goal.Progress.TryGetValue(out var progress) && (float.IsNaN(progress) || progress < 0f || progress > 1f))
            {
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"goal {goal.Id} progress outside [0,1]"));
            }
        }

        foreach (var fact in Facts())
        {
            if (fact.ObservedAtUnixMillis > ObservedAtUnixMillis)
            {
                problems.Add("a fact is observed later than the snapshot itself");
                break;
            }
        }

        return problems.ToImmutable();
    }

    /// <summary>Fail-closed guard: throws with every violation listed when the snapshot is malformed.</summary>
    public void ThrowIfInvalid()
    {
        var problems = Validate();
        if (!problems.IsEmpty)
        {
            throw new InvalidOperationException("World Model snapshot is invalid: " + string.Join("; ", problems));
        }
    }

    /// <summary>
    /// Deterministic 64-bit FNV-1a digest of the canonical text form. Equal
    /// snapshots have equal digests; a replay that diverges shows up here.
    /// </summary>
    public ulong ComputeDigest() => WorldModelCanonicalText.Digest(this);

    public bool Equals(WorldModelSnapshot? other)
        => other is not null
           && SchemaVersion == other.SchemaVersion
           && Revision == other.Revision
           && ObservedAtUnixMillis == other.ObservedAtUnixMillis
           && Equals(Player, other.Player)
           && Equals(Map, other.Map)
           && ImmutableSequence.Equal(Mobs, other.Mobs)
           && ImmutableSequence.Equal(Npcs, other.Npcs)
           && ImmutableSequence.Equal(Drops, other.Drops)
           && ImmutableSequence.Equal(Quests, other.Quests)
           && ImmutableSequence.Equal(Inventory, other.Inventory)
           && ImmutableSequence.Equal(Equipment, other.Equipment)
           && ImmutableSequence.Equal(Skills, other.Skills)
           && ImmutableSequence.Equal(StatusEffects, other.StatusEffects)
           && ImmutableSequence.Equal(Cooldowns, other.Cooldowns)
           && ImmutableSequence.Equal(Resources, other.Resources)
           && ImmutableSequence.Equal(Actions, other.Actions)
           && ImmutableSequence.Equal(Goals, other.Goals);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SchemaVersion);
        hash.Add(Revision);
        hash.Add(ObservedAtUnixMillis);
        hash.Add(Player);
        hash.Add(Map);
        ImmutableSequence.AddTo(ref hash, Mobs);
        ImmutableSequence.AddTo(ref hash, Npcs);
        ImmutableSequence.AddTo(ref hash, Drops);
        ImmutableSequence.AddTo(ref hash, Quests);
        ImmutableSequence.AddTo(ref hash, Inventory);
        ImmutableSequence.AddTo(ref hash, Equipment);
        ImmutableSequence.AddTo(ref hash, Skills);
        ImmutableSequence.AddTo(ref hash, StatusEffects);
        ImmutableSequence.AddTo(ref hash, Cooldowns);
        ImmutableSequence.AddTo(ref hash, Resources);
        ImmutableSequence.AddTo(ref hash, Actions);
        ImmutableSequence.AddTo(ref hash, Goals);
        return hash.ToHashCode();
    }

    private static void CheckDistinct<TKey>(IEnumerable<TKey> keys, string what, ImmutableArray<string>.Builder problems)
    {
        var seen = new HashSet<TKey>();
        foreach (var key in keys)
        {
            if (!seen.Add(key))
            {
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"duplicate {what} {key}"));
                return;
            }
        }
    }

    private static void CheckSortedDistinct(IEnumerable<long> keys, string what, ImmutableArray<string>.Builder problems)
    {
        long? previous = null;
        foreach (var key in keys)
        {
            if (previous is not null && key <= previous.Value)
            {
                problems.Add(string.Create(CultureInfo.InvariantCulture, $"{what} sequence is not strictly ascending at {key}"));
                return;
            }

            previous = key;
        }
    }
}

internal static class ImmutableArrayExtensions
{
    public static ImmutableArray<T> OrEmpty<T>(this ImmutableArray<T> array)
        => array.IsDefault ? ImmutableArray<T>.Empty : array;
}
