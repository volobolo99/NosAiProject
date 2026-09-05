namespace NosAi.Core.Hardware;

/// <summary>
/// Provenance of a hardware capability reading. Mirrors the semantics of
/// <c>NosAi.Runtime.Contracts.DataSourceKind</c> (Live/Derived/Cached/Simulated/Unknown)
/// used across the rest of the runtime for every observed gameplay fact.
///
/// This type is intentionally re-declared here rather than referenced from
/// <c>NosAi.Runtime.Contracts</c>: <c>NosAi.Core.csproj</c> has zero project
/// references by design (docs/ROADMAP_ESECUTIVA.md S:1.3 — "NosAi.Core non
/// referenzia nulla"), while <c>NosAi.Runtime</c> already depends on
/// <c>NosAi.Core</c> (see NosAi.Runtime.csproj ProjectReference). Referencing
/// <c>NosAi.Runtime.Contracts</c> from here would therefore require a
/// circular project reference, which is not legal in .NET and is not
/// something this task is permitted to restructure (the assigned ownership
/// forbids editing NosAi.Core.csproj and NosAi.Runtime/Hardware/*). See the
/// AP-00/A1 handoff notes for the follow-up recommendation this implies for
/// A3/A6 (hoisting the canonical classification primitive down into
/// NosAi.Core so both assemblies share one definition).
/// </summary>
public enum DataSourceKind
{
    /// <summary>Read directly from a live hardware/OS API in this process.</summary>
    Live = 0,

    /// <summary>Computed from one or more other classified values (e.g. free VRAM = total - used).</summary>
    Derived = 1,

    /// <summary>A previously live/derived reading that is being reused because a fresh read was not taken.</summary>
    Cached = 2,

    /// <summary>Produced by a local simulator/estimator rather than observed hardware. Never authoritative.</summary>
    Simulated = 3,

    /// <summary>No trustworthy reading exists. Never converted into a synthetic zero/false/default value.</summary>
    Unknown = 4
}

/// <summary>Helpers for classifying a <see cref="DataSourceKind"/> as trustworthy for scheduling decisions.</summary>
public static class DataSourceKindClassification
{
    /// <summary>
    /// True for every source kind that represents a value the runtime actually
    /// obtained (as opposed to <see cref="DataSourceKind.Unknown"/>, which
    /// carries no usable value at all). Feasibility and budget logic must
    /// never treat <see cref="DataSourceKind.Unknown"/> as satisfying a
    /// resource requirement.
    /// </summary>
    public static bool IsTrustedForScheduling(this DataSourceKind kind)
        => kind is DataSourceKind.Live or DataSourceKind.Derived or DataSourceKind.Cached;
}

/// <summary>
/// A hardware capability reading paired with its provenance, freshness and
/// (when absent) the reason it could not be observed. Unknown hardware is
/// represented explicitly via <see cref="Unknown"/>; it is never collapsed
/// into <c>default(T)</c>, so a caller that forgets to check
/// <see cref="HasValue"/> cannot silently treat "we don't know" as zero,
/// false or an empty string.
///
/// Structurally and semantically equivalent to
/// <c>NosAi.Runtime.Contracts.ClassifiedValue&lt;T&gt;</c> — see the remarks
/// on <see cref="DataSourceKind"/> for why this is a separate declaration
/// rather than a shared reference.
/// </summary>
/// <typeparam name="T">The observed value's type. Must be a value type or an immutable reference type so record equality is meaningful.</typeparam>
public sealed record ClassifiedValue<T>(
    T Value,
    DataSourceKind Source,
    DateTime ObservedAtUtc,
    bool HasObservedValue,
    string? Warning = null,
    string? FailureReason = null)
{
    /// <summary>
    /// True only when the value was actually observed/derived/cached AND the
    /// source is not <see cref="DataSourceKind.Unknown"/>. Every consumer of
    /// a <see cref="ClassifiedValue{T}"/> must gate on this before reading
    /// <see cref="Value"/>.
    /// </summary>
    public bool HasValue => HasObservedValue && Source != DataSourceKind.Unknown;

    /// <summary>A value read directly from live hardware/OS telemetry.</summary>
    public static ClassifiedValue<T> Live(T value, DateTime? observedAtUtc = null, string? warning = null)
        => new(value, DataSourceKind.Live, observedAtUtc ?? DateTime.UtcNow, true, warning, null);

    /// <summary>A value computed from other classified values (e.g. free = total - used).</summary>
    public static ClassifiedValue<T> Derived(T value, DateTime? observedAtUtc = null, string? warning = null)
        => new(value, DataSourceKind.Derived, observedAtUtc ?? DateTime.UtcNow, true, warning, null);

    /// <summary>A previously observed value being reused because a fresh read was not taken at <paramref name="observedAtUtc"/>.</summary>
    public static ClassifiedValue<T> Cached(T value, DateTime observedAtUtc, string? warning = null)
        => new(value, DataSourceKind.Cached, observedAtUtc, true, warning, null);

    /// <summary>A value produced by local simulation/estimation. Never authoritative for scheduling gates.</summary>
    public static ClassifiedValue<T> Simulated(T value, DateTime? observedAtUtc = null, string? warning = null)
        => new(value, DataSourceKind.Simulated, observedAtUtc ?? DateTime.UtcNow, true, warning, null);

    /// <summary>
    /// No trustworthy reading exists. <paramref name="reason"/> is mandatory
    /// so an Unknown value always carries a diagnosable cause instead of a
    /// silent gap.
    /// </summary>
    public static ClassifiedValue<T> Unknown(string reason, string? warning = null)
        => new(default!, DataSourceKind.Unknown, DateTime.UtcNow, false, warning, reason);
}
