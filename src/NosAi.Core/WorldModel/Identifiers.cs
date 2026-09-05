using System.Globalization;

namespace NosAi.Core.WorldModel;

/// <summary>Map identity as the client reports it. Zero is "no map".</summary>
public readonly record struct MapId(int Value)
{
    public static MapId None => default;
    public bool IsNone => Value == 0;
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Entity identity as observed on the wire or screen. Never a memory address,
/// process handle or client-internal pointer: those are privileged and do not
/// belong in the World Model.
/// </summary>
public readonly record struct EntityId(long Value)
{
    public static EntityId None => default;
    public bool IsNone => Value == 0;
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Quest identity, stable across sessions. Empty is "no quest".</summary>
public readonly record struct QuestId(string Value)
{
    public static QuestId None => new(string.Empty);
    public bool IsNone => string.IsNullOrEmpty(Value);
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Item catalogue identity (vnum-like). Zero is "no item".</summary>
public readonly record struct ItemId(int Value)
{
    public static ItemId None => default;
    public bool IsNone => Value == 0;
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Skill catalogue identity. Zero is "no skill".</summary>
public readonly record struct SkillId(int Value)
{
    public static SkillId None => default;
    public bool IsNone => Value == 0;
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Status-effect (buff/debuff) catalogue identity. Zero is "no effect".</summary>
public readonly record struct EffectId(int Value)
{
    public static EffectId None => default;
    public bool IsNone => Value == 0;
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Identity of a catalogued action (docs/CATALOGO_AZIONI_E_POSTCONDIZIONI.md).</summary>
public readonly record struct ActionId(string Value)
{
    public static ActionId None => new(string.Empty);
    public bool IsNone => string.IsNullOrEmpty(Value);
    public override string ToString() => Value ?? string.Empty;
}

/// <summary>Identity of a World Model goal record. Distinct from <see cref="Planning.GoalId"/>, which addresses planner goal-stack entries.</summary>
public readonly record struct WorldGoalId(string Value)
{
    public static WorldGoalId None => new(string.Empty);
    public bool IsNone => string.IsNullOrEmpty(Value);
    public override string ToString() => Value ?? string.Empty;
}
