namespace NosAi.Core.WorldModel;

public enum Element : byte
{
    Unknown = 0,
    Neutral = 1,
    Fire = 2,
    Water = 3,
    Light = 4,
    Shadow = 5
}

public enum SkillTargetKind : byte
{
    Unknown = 0,
    Self = 1,
    SingleEnemy = 2,
    SingleAlly = 3,
    AreaAroundSelf = 4,
    AreaAtTarget = 5,
    NoTarget = 6
}

/// <summary>Skill conosciuta del personaggio. I costi e i tempi sono fatti perché possono arrivare da catalogo (CACHED) o da UI (LIVE).</summary>
public sealed record SkillState(
    SkillId Id,
    Fact<TemplateId> Template,
    Fact<byte> Level,
    Fact<int> MpCost,
    Fact<byte> Range,
    Fact<SkillTargetKind> TargetKind,
    Fact<Element> Element,
    Fact<int> CastTimeMillis,
    Fact<int> CooldownMillis,
    Fact<bool> Usable) : IFactCarrier
{
    public FactSummary Summarize()
    {
        FactSummary summary = FactSummary.Empty;
        summary.Add(Template);
        summary.Add(Level);
        summary.Add(MpCost);
        summary.Add(Range);
        summary.Add(TargetKind);
        summary.Add(Element);
        summary.Add(CastTimeMillis);
        summary.Add(CooldownMillis);
        summary.Add(Usable);
        return summary;
    }

    public static SkillState Unknown(SkillId id, string reason, long observedAtUnixMillis = 0) => new(
        id,
        Fact<TemplateId>.Unknown(reason, observedAtUnixMillis),
        Fact<byte>.Unknown(reason, observedAtUnixMillis),
        Fact<int>.Unknown(reason, observedAtUnixMillis),
        Fact<byte>.Unknown(reason, observedAtUnixMillis),
        Fact<SkillTargetKind>.Unknown(reason, observedAtUnixMillis),
        Fact<Element>.Unknown(reason, observedAtUnixMillis),
        Fact<int>.Unknown(reason, observedAtUnixMillis),
        Fact<int>.Unknown(reason, observedAtUnixMillis),
        Fact<bool>.Unknown(reason, observedAtUnixMillis));
}

/// <summary>Buff attivo sul giocatore.</summary>
public sealed record BuffState(
    StatusEffectId Id,
    Fact<TemplateId> Template,
    Fact<byte> Level,
    Fact<EntityId> Source,
    Fact<long> AppliedAtUnixMillis,
    Fact<long> ExpiresAtUnixMillis) : IFactCarrier
{
    /// <summary>Millisecondi residui; null quando la scadenza è UNKNOWN. Zero quando già scaduto.</summary>
    public long? RemainingMillisAt(long nowUnixMillis)
        => ExpiresAtUnixMillis.TryGetValue(out long expires) ? Math.Max(0, expires - nowUnixMillis) : null;

    public FactSummary Summarize()
    {
        FactSummary summary = FactSummary.Empty;
        summary.Add(Template);
        summary.Add(Level);
        summary.Add(Source);
        summary.Add(AppliedAtUnixMillis);
        summary.Add(ExpiresAtUnixMillis);
        return summary;
    }

    public static BuffState Unknown(StatusEffectId id, string reason, long observedAtUnixMillis = 0) => new(
        id,
        Fact<TemplateId>.Unknown(reason, observedAtUnixMillis),
        Fact<byte>.Unknown(reason, observedAtUnixMillis),
        Fact<EntityId>.Unknown(reason, observedAtUnixMillis),
        Fact<long>.Unknown(reason, observedAtUnixMillis),
        Fact<long>.Unknown(reason, observedAtUnixMillis));
}

/// <summary>Debuff attivo sul giocatore. <see cref="Severity"/> ∈ [0,1] è DERIVED dal catalogo effetti.</summary>
public sealed record DebuffState(
    StatusEffectId Id,
    Fact<TemplateId> Template,
    Fact<byte> Level,
    Fact<EntityId> Source,
    Fact<long> AppliedAtUnixMillis,
    Fact<long> ExpiresAtUnixMillis,
    Fact<float> Severity,
    Fact<bool> Dispellable) : IFactCarrier
{
    public long? RemainingMillisAt(long nowUnixMillis)
        => ExpiresAtUnixMillis.TryGetValue(out long expires) ? Math.Max(0, expires - nowUnixMillis) : null;

    public FactSummary Summarize()
    {
        FactSummary summary = FactSummary.Empty;
        summary.Add(Template);
        summary.Add(Level);
        summary.Add(Source);
        summary.Add(AppliedAtUnixMillis);
        summary.Add(ExpiresAtUnixMillis);
        summary.Add(Severity);
        summary.Add(Dispellable);
        return summary;
    }

    public static DebuffState Unknown(StatusEffectId id, string reason, long observedAtUnixMillis = 0) => new(
        id,
        Fact<TemplateId>.Unknown(reason, observedAtUnixMillis),
        Fact<byte>.Unknown(reason, observedAtUnixMillis),
        Fact<EntityId>.Unknown(reason, observedAtUnixMillis),
        Fact<long>.Unknown(reason, observedAtUnixMillis),
        Fact<long>.Unknown(reason, observedAtUnixMillis),
        Fact<float>.Unknown(reason, observedAtUnixMillis),
        Fact<bool>.Unknown(reason, observedAtUnixMillis));
}

/// <summary>
/// Cooldown di una skill. <see cref="ReadyAtUnixMillis"/> UNKNOWN significa
/// "non sappiamo se è pronta": il planner non può assumerla disponibile.
/// </summary>
public readonly record struct CooldownState(SkillId Skill, Fact<long> ReadyAtUnixMillis) : IFactCarrier
{
    /// <summary>Vero solo con scadenza conosciuta e già raggiunta.</summary>
    public bool IsObservedReadyAt(long nowUnixMillis)
        => ReadyAtUnixMillis.TryGetValue(out long ready) && nowUnixMillis >= ready;

    public long? RemainingMillisAt(long nowUnixMillis)
        => ReadyAtUnixMillis.TryGetValue(out long ready) ? Math.Max(0, ready - nowUnixMillis) : null;

    public FactSummary Summarize()
    {
        FactSummary summary = FactSummary.Empty;
        summary.Add(ReadyAtUnixMillis);
        return summary;
    }

    public static CooldownState Unknown(SkillId skill, string reason, long observedAtUnixMillis = 0)
        => new(skill, Fact<long>.Unknown(reason, observedAtUnixMillis));
}

/// <summary>Risorse quantificabili del personaggio oltre a HP/MP, che restano su <see cref="PlayerState"/>.</summary>
public enum ResourceKind : byte
{
    Unknown = 0,
    Gold = 1,
    Experience = 2,
    JobExperience = 3,
    HeroExperience = 4,
    SpecialistPoints = 5,
    Reputation = 6,
    Stamina = 7,
    Potions = 8,
    Ammunition = 9
}

/// <summary>Risorsa con valore corrente, massimo e tasso di rigenerazione (DERIVED).</summary>
public readonly record struct ResourceState(
    ResourceKind Kind,
    Fact<long> Current,
    Fact<long> Maximum,
    Fact<float> RegenPerSecond) : IFactCarrier
{
    public bool TryGetRatio(out float ratio)
    {
        if (Current.TryGetValue(out long current) && Maximum.TryGetValue(out long max) && max > 0)
        {
            ratio = Math.Clamp((float)current / max, 0f, 1f);
            return true;
        }

        ratio = 0f;
        return false;
    }

    public FactSummary Summarize()
    {
        FactSummary summary = FactSummary.Empty;
        summary.Add(Current);
        summary.Add(Maximum);
        summary.Add(RegenPerSecond);
        return summary;
    }

    public static ResourceState Unknown(ResourceKind kind, string reason, long observedAtUnixMillis = 0) => new(
        kind,
        Fact<long>.Unknown(reason, observedAtUnixMillis),
        Fact<long>.Unknown(reason, observedAtUnixMillis),
        Fact<float>.Unknown(reason, observedAtUnixMillis));
}
