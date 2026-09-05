using System.Collections.Immutable;

namespace NosAi.Core.WorldModel;

/// <summary>Lifecycle of a quest as the client shows it.</summary>
public enum QuestStatus : byte
{
    Unknown = 0,
    Available = 1,
    Active = 2,
    Completable = 3,
    Completed = 4,
    Failed = 5
}

/// <summary>One quest objective with observed progress. <see cref="Required"/> zero means the target is not stated by the client.</summary>
public sealed record QuestObjective(
    string Key,
    WorldFact<string> Description,
    WorldFact<int> Progress,
    WorldFact<int> Required)
{
    public bool IsSatisfied => Progress.TryGetValue(out var p) && Required.TryGetValue(out var r) && r > 0 && p >= r;
}

/// <summary>A quest and its objectives.</summary>
public sealed record QuestState(
    QuestId Id,
    WorldFact<string> Title,
    WorldFact<QuestStatus> Status,
    ImmutableArray<QuestObjective> Objectives)
{
    public bool Equals(QuestState? other)
        => other is not null
           && Id.Equals(other.Id)
           && Title.Equals(other.Title)
           && Status.Equals(other.Status)
           && ImmutableSequence.Equal(Objectives, other.Objectives);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(Title);
        hash.Add(Status);
        ImmutableSequence.AddTo(ref hash, Objectives);
        return hash.ToHashCode();
    }
}

/// <summary>Inventory bag as the client partitions it.</summary>
public enum InventoryBag : byte
{
    Unknown = 0,
    Equipment = 1,
    Main = 2,
    Etc = 3,
    Miniland = 4,
    Specialist = 5,
    Costume = 6
}

/// <summary>An item in an inventory slot.</summary>
public sealed record InventoryItem(
    InventoryBag Bag,
    int Slot,
    WorldFact<ItemId> Item,
    WorldFact<int> Quantity,
    WorldFact<int> Rarity,
    WorldFact<int> Upgrade);

/// <summary>Equipment slots the client exposes.</summary>
public enum EquipmentSlot : byte
{
    Unknown = 0,
    MainWeapon = 1,
    Armor = 2,
    Hat = 3,
    Gloves = 4,
    Boots = 5,
    SecondaryWeapon = 6,
    Necklace = 7,
    Ring = 8,
    Bracelet = 9,
    Mask = 10,
    Fairy = 11,
    Amulet = 12,
    Specialist = 13,
    CostumeSuit = 14,
    CostumeHat = 15,
    WeaponSkin = 16
}

/// <summary>An equipped item and its observed attributes.</summary>
public sealed record EquipmentItem(
    EquipmentSlot Slot,
    WorldFact<ItemId> Item,
    WorldFact<int> Rarity,
    WorldFact<int> Upgrade,
    WorldFact<int> Durability);

/// <summary>A skill the character knows, with its cooldown state.</summary>
public sealed record SkillState(
    SkillId Id,
    WorldFact<string> Name,
    WorldFact<int> MpCost,
    WorldFact<int> Range,
    WorldFact<int> CastTimeMillis,
    WorldFact<int> CooldownMillis,
    WorldFact<bool> Ready,
    WorldFact<long> ReadyAtUnixMillis)
{
    /// <summary>Whether the skill is usable at <paramref name="nowUnixMillis"/>. Null when the model cannot tell.</summary>
    public bool? IsReadyAt(long nowUnixMillis)
    {
        if (ReadyAtUnixMillis.TryGetValue(out var readyAt))
        {
            return nowUnixMillis >= readyAt;
        }

        return Ready.TryGetValue(out var ready) ? ready : null;
    }
}

/// <summary>Whether a status effect helps or harms the bearer.</summary>
public enum StatusEffectPolarity : byte
{
    Unknown = 0,
    Buff = 1,
    Debuff = 2
}

/// <summary>A buff or debuff on the character. Buff and debuff share one shape; polarity tells them apart.</summary>
public sealed record StatusEffectState(
    EffectId Id,
    StatusEffectPolarity Polarity,
    WorldFact<string> Name,
    WorldFact<int> Level,
    WorldFact<long> AppliedAtUnixMillis,
    WorldFact<long> ExpiresAtUnixMillis)
{
    /// <summary>Whether the effect is still running at <paramref name="nowUnixMillis"/>. Null when expiry is unknown.</summary>
    public bool? IsActiveAt(long nowUnixMillis)
        => ExpiresAtUnixMillis.TryGetValue(out var expiresAt) ? nowUnixMillis < expiresAt : null;
}

/// <summary>What a cooldown applies to.</summary>
public enum CooldownScope : byte
{
    Unknown = 0,
    Skill = 1,
    Item = 2,
    Global = 3
}

/// <summary>A running cooldown. <see cref="Key"/> is the skill/item id, or 0 for a global cooldown.</summary>
public sealed record CooldownState(
    CooldownScope Scope,
    int Key,
    WorldFact<long> StartedAtUnixMillis,
    WorldFact<long> ReadyAtUnixMillis)
{
    /// <summary>Remaining milliseconds at <paramref name="nowUnixMillis"/>; zero when elapsed; null when unknown.</summary>
    public long? RemainingAt(long nowUnixMillis)
        => ReadyAtUnixMillis.TryGetValue(out var readyAt) ? Math.Max(0, readyAt - nowUnixMillis) : null;
}

/// <summary>Currencies and countable resources the client shows.</summary>
public enum ResourceKind : byte
{
    Unknown = 0,
    Gold = 1,
    BankGold = 2,
    Reputation = 3,
    Dignity = 4,
    SpecialistPoints = 5,
    Experience = 6,
    JobExperience = 7,
    HeroExperience = 8,
    ActPoints = 9
}

/// <summary>A resource amount with provenance. Amounts are signed longs; an unknown amount is UNKNOWN, never 0.</summary>
public sealed record ResourceState(
    ResourceKind Kind,
    WorldFact<long> Amount,
    WorldFact<long> Maximum);
