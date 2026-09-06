namespace NosAi.Core.WorldModel;

/// <summary>Whether a <see cref="StatusEffect"/> helps (Buff) or harms (Debuff) its target.</summary>
public enum StatusEffectPolarity
{
    Buff = 0,
    Debuff = 1
}

/// <summary>
/// One active buff or debuff on a <see cref="Player"/> or <see cref="Mob"/>.
/// docs/ROADMAP_ESECUTIVA.md S:AP-01 lists Buff and Debuff as separate
/// nouns; they are modelled here as one record with an explicit
/// <see cref="Polarity"/> discriminator rather than two near-identical
/// types, since a buff and a debuff have exactly the same observable shape
/// (id, name, remaining duration, magnitude) and differ only in which way
/// that magnitude affects the target.
/// </summary>
public sealed record StatusEffect(
    StatusEffectId Id,
    StatusEffectPolarity Polarity,
    WorldFact<string> Name,
    WorldFact<TimeSpan> RemainingDuration,
    WorldFact<double> Magnitude);

/// <summary>Remaining lockout on a <see cref="Skill"/>.</summary>
public sealed record Cooldown(
    SkillId SkillId,
    WorldFact<TimeSpan> RemainingDuration)
{
    /// <summary>True only when the remaining duration is a known, positive value; an unknown or zero/negative reading is never treated as "on cooldown".</summary>
    public bool IsActive => RemainingDuration.HasValue && RemainingDuration.Value > TimeSpan.Zero;
}

/// <summary>One skill/ability a <see cref="Player"/> can use.</summary>
public sealed record Skill(
    SkillId Id,
    WorldFact<string> Name,
    WorldFact<int> Level,
    WorldFact<bool> IsUsable);
