namespace NosAi.Core.WorldModel;

/// <summary>
/// Identity of a live entity in the world -- a <see cref="Player"/>, a
/// <see cref="Mob"/>, an <see cref="Npc"/> or a <see cref="Drop"/> all share
/// this id space (mirrors the client's own single entity-id numbering),
/// so a caller can look up "what is at entity id N" without knowing its
/// category up front.
/// </summary>
public readonly record struct EntityId
{
    public string Value { get; }

    public EntityId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public override string ToString() => Value;
}

/// <summary>Identity of a persistent, versioned <see cref="MapModel"/>.</summary>
public readonly record struct MapId
{
    public string Value { get; }

    public MapId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public override string ToString() => Value;
}

/// <summary>Identity of a map-to-map <see cref="Portal"/>.</summary>
public readonly record struct PortalId
{
    public string Value { get; }

    public PortalId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public override string ToString() => Value;
}

/// <summary>Identity of an item catalog entry, shared by <see cref="InventoryItem"/>, <see cref="EquipmentItem"/> and <see cref="Drop"/>.</summary>
public readonly record struct ItemId
{
    public string Value { get; }

    public ItemId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public override string ToString() => Value;
}

/// <summary>Identity of a <see cref="Skill"/>, also used to key its <see cref="Cooldown"/>.</summary>
public readonly record struct SkillId
{
    public string Value { get; }

    public SkillId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public override string ToString() => Value;
}

/// <summary>Identity of one active <see cref="StatusEffect"/> instance (a buff or debuff application).</summary>
public readonly record struct StatusEffectId
{
    public string Value { get; }

    public StatusEffectId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public override string ToString() => Value;
}

/// <summary>Identity of a <see cref="Quest"/>.</summary>
public readonly record struct QuestId
{
    public string Value { get; }

    public QuestId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public override string ToString() => Value;
}

/// <summary>Identity of one recorded <see cref="WorldAction"/> (a planned/executed/verified action, not a UI/skill button).</summary>
public readonly record struct ActionId
{
    public string Value { get; }

    public ActionId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public override string ToString() => Value;
}

/// <summary>Identity of one active strategic <see cref="Goal"/>.</summary>
public readonly record struct GoalId
{
    public string Value { get; }

    public GoalId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public override string ToString() => Value;
}
