namespace NosAi.Core.WorldModel;

/// <summary>
/// Provenance of one fused World Model fact. Mirrors the semantics of
/// <c>NosAi.Core.Hardware.DataSourceKind</c> (Live/Derived/Cached/Simulated/Unknown)
/// already used elsewhere in this assembly for hardware capability readings.
///
/// Re-declared here, in the World Model's own bounded context, rather than
/// reused from <c>NosAi.Core.Hardware</c>: that namespace is a hardware
/// capability/scheduling concern (docs/agents/phases/AP-00), and importing it
/// from Player/Quest/Mob contracts would tie two unrelated domains together
/// for no architectural reason. Unifying the two declarations behind one
/// canonical primitive is a reasonable future integration task (tracked in
/// this phase's handoff notes) but is out of scope for a contracts-only task.
/// </summary>
public enum DataSourceKind
{
    /// <summary>Read directly from a live observation channel (network/memory/screen/local) in this process.</summary>
    Live = 0,

    /// <summary>Computed from one or more other classified facts (e.g. a fused position from two agreeing sensors).</summary>
    Derived = 1,

    /// <summary>A previously live/derived fact that is being reused because a fresh observation was not taken.</summary>
    Cached = 2,

    /// <summary>Produced by local prediction/simulation rather than observed gameplay. Never authoritative.</summary>
    Simulated = 3,

    /// <summary>No trustworthy observation exists. Never converted into a synthetic zero/false/default value.</summary>
    Unknown = 4
}

/// <summary>
/// One fused World Model fact: a value paired with its provenance, a
/// continuous confidence score and the instant it was observed -- or,
/// when unobserved, an explicit <see cref="Unknown"/> carrying the reason
/// why. This is the type every uncertain field on every contract in this
/// namespace (Player position, Mob HP, Quest status, ...) is expressed as,
/// so "we don't know yet" can never be silently read back as zero, false or
/// an empty value (docs/ROADMAP_ESECUTIVA.md invariant "Unknown is not
/// zero, false or empty").
///
/// Distinct from <c>NosAi.Core.Hardware.ClassifiedValue&lt;T&gt;</c> by
/// design: gameplay sensor fusion routinely produces disagreement between
/// sources rather than a single categorical trust level, so this type
/// additionally carries a continuous <see cref="Confidence"/> in
/// [0, 1] alongside the categorical <see cref="Source"/>
/// (docs/NOSAI_AUTONOMOUS_PLAYER_SPEC.md S:4.1: "Sensor disagreement
/// reduces confidence rather than silently selecting convenient data").
/// </summary>
/// <typeparam name="T">The observed value's type. Should be a value type or an immutable reference type so record equality is meaningful.</typeparam>
public sealed record WorldFact<T>(
    T Value,
    DataSourceKind Source,
    double Confidence,
    DateTime ObservedAtUtc,
    bool HasObservedValue,
    string? Reason = null)
{
    /// <summary>
    /// True only when the fact was actually observed/derived/cached/simulated
    /// AND the source is not <see cref="DataSourceKind.Unknown"/>. Every
    /// consumer must gate on this before reading <see cref="Value"/>.
    /// </summary>
    public bool HasValue => HasObservedValue && Source != DataSourceKind.Unknown;

    /// <summary>True when this fact has a value and was observed within <paramref name="maxAge"/> of <paramref name="nowUtc"/>.</summary>
    public bool IsFresh(TimeSpan maxAge, DateTime nowUtc) => HasValue && nowUtc - ObservedAtUtc <= maxAge;

    /// <summary>A fact read directly from a live observation channel.</summary>
    public static WorldFact<T> Live(T value, double confidence, DateTime? observedAtUtc = null, string? reason = null)
        => new(value, DataSourceKind.Live, ClampConfidence(confidence), observedAtUtc ?? DateTime.UtcNow, true, reason);

    /// <summary>A fact computed/fused from other facts.</summary>
    public static WorldFact<T> Derived(T value, double confidence, DateTime? observedAtUtc = null, string? reason = null)
        => new(value, DataSourceKind.Derived, ClampConfidence(confidence), observedAtUtc ?? DateTime.UtcNow, true, reason);

    /// <summary>A previously observed fact being reused because a fresh observation was not taken at <paramref name="observedAtUtc"/>.</summary>
    public static WorldFact<T> Cached(T value, double confidence, DateTime observedAtUtc, string? reason = null)
        => new(value, DataSourceKind.Cached, ClampConfidence(confidence), observedAtUtc, true, reason);

    /// <summary>A fact produced by local prediction/simulation. Never authoritative gameplay truth.</summary>
    public static WorldFact<T> Simulated(T value, double confidence, DateTime? observedAtUtc = null, string? reason = null)
        => new(value, DataSourceKind.Simulated, ClampConfidence(confidence), observedAtUtc ?? DateTime.UtcNow, true, reason);

    /// <summary>
    /// No trustworthy observation exists. <paramref name="reason"/> is
    /// mandatory so an Unknown fact always carries a diagnosable cause
    /// instead of a silent gap.
    /// </summary>
    public static WorldFact<T> Unknown(string reason, DateTime? observedAtUtc = null)
        => new(default!, DataSourceKind.Unknown, 0d, observedAtUtc ?? DateTime.UtcNow, false, reason);

    private static double ClampConfidence(double confidence) => Math.Clamp(confidence, 0d, 1d);
}
