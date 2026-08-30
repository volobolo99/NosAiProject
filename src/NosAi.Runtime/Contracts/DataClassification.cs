namespace NosAi.Runtime.Contracts;

/// <summary>
/// Provenance of an externally visible observation. Unknown is not zero, false or empty.
/// Callers must inspect <see cref="HasValue"/> / <see cref="Source"/> before using <see cref="Value"/>.
/// </summary>
public enum DataSourceKind
{
    Live = 0,
    Derived = 1,
    Cached = 2,
    Simulated = 3,
    Unknown = 4
}

public static class DataSourceKindText
{
    public static string ToWire(this DataSourceKind kind) => kind switch
    {
        DataSourceKind.Live => "LIVE",
        DataSourceKind.Derived => "DERIVED",
        DataSourceKind.Cached => "CACHED",
        DataSourceKind.Simulated => "SIMULATED",
        DataSourceKind.Unknown => "UNKNOWN",
        _ => "UNKNOWN"
    };

    public static bool IsTrustedProductionSource(this DataSourceKind kind)
        => kind is DataSourceKind.Live or DataSourceKind.Derived or DataSourceKind.Cached;
}

public sealed record ClassifiedValue<T>(
    T Value,
    DataSourceKind Source,
    DateTime ObservedAtUtc,
    bool HasObservedValue,
    string? Warning = null,
    string? FailureReason = null)
{
    public bool HasValue => HasObservedValue && Source != DataSourceKind.Unknown;

    public static ClassifiedValue<T> Live(T value, DateTime? observedAtUtc = null, string? warning = null)
        => new(value, DataSourceKind.Live, observedAtUtc ?? DateTime.UtcNow, true, warning, null);

    public static ClassifiedValue<T> Derived(T value, DateTime? observedAtUtc = null, string? warning = null)
        => new(value, DataSourceKind.Derived, observedAtUtc ?? DateTime.UtcNow, true, warning, null);

    public static ClassifiedValue<T> Unknown(string reason, string? warning = null)
        => new(default!, DataSourceKind.Unknown, DateTime.UtcNow, false, warning, reason);

    public object ToWire() => new
    {
        value = HasValue ? (object?)Value : null,
        source = Source.ToWire(),
        observedAtUtc = ObservedAtUtc,
        warning = Warning,
        failureReason = FailureReason
    };
}
