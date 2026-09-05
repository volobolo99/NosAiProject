using System.Globalization;

namespace NosAi.Core.WorldModel;

/// <summary>
/// Version stamp of the AP-01 World Model contract family. Bumped whenever a
/// consumer of a serialized <see cref="WorldModelSnapshot"/> could misread it.
/// </summary>
public static class WorldModelContract
{
    /// <summary>Schema version carried by every snapshot (docs/ROADMAP_ESECUTIVA.md AP-01).</summary>
    public const int SchemaVersion = 1;

    /// <summary>Sentinel used by UNKNOWN facts that were never observed by any channel.</summary>
    public const string NotObservedReason = "not observed";
}

/// <summary>
/// Classification of a fact's origin. Mirrors the runtime wire vocabulary
/// (<c>LIVE</c>/<c>DERIVED</c>/<c>CACHED</c>/<c>SIMULATED</c>/<c>UNKNOWN</c>)
/// one-to-one so sensor fusion can map values without a translation table.
/// Unknown is never zero, false or empty.
/// </summary>
public enum FactSource : byte
{
    /// <summary>Zero on purpose: a <c>default</c> <see cref="WorldFact{T}"/> is UNKNOWN, never LIVE.</summary>
    Unknown = 0,
    Live = 1,
    Derived = 2,
    Cached = 3,
    Simulated = 4
}

/// <summary>Which sensor family produced the fact. <see cref="None"/> is reserved for UNKNOWN facts.</summary>
public enum SensorChannel : byte
{
    None = 0,
    Network = 1,
    Memory = 2,
    Screen = 3,
    Local = 4
}

/// <summary>Freshness verdict of a fact against a caller-supplied budget.</summary>
public enum FactFreshness : byte
{
    /// <summary>The fact carries no observed value; age is meaningless.</summary>
    Unknown = 0,
    /// <summary>Observed within the budget.</summary>
    Fresh = 1,
    /// <summary>Observed, but older than the budget.</summary>
    Stale = 2
}

public static class FactSourceText
{
    public static string ToWire(this FactSource source) => source switch
    {
        FactSource.Live => "LIVE",
        FactSource.Derived => "DERIVED",
        FactSource.Cached => "CACHED",
        FactSource.Simulated => "SIMULATED",
        FactSource.Unknown => "UNKNOWN",
        _ => "UNKNOWN"
    };

    public static bool TryParseWire(string? text, out FactSource source)
    {
        switch (text)
        {
            case "LIVE": source = FactSource.Live; return true;
            case "DERIVED": source = FactSource.Derived; return true;
            case "CACHED": source = FactSource.Cached; return true;
            case "SIMULATED": source = FactSource.Simulated; return true;
            case "UNKNOWN": source = FactSource.Unknown; return true;
            default: source = FactSource.Unknown; return false;
        }
    }

    /// <summary>A source the runtime may act on. Simulated and Unknown are never actionable.</summary>
    public static bool IsActionable(this FactSource source)
        => source is FactSource.Live or FactSource.Derived or FactSource.Cached;
}

/// <summary>
/// Type-erased view of a <see cref="WorldFact{T}"/> so an aggregate can audit
/// provenance without knowing the payload type.
/// </summary>
public interface IWorldFact
{
    FactSource Source { get; }
    SensorChannel Channel { get; }
    float Confidence { get; }
    long ObservedAtUnixMillis { get; }
    bool HasValue { get; }
    string? Reason { get; }
    FactFreshness FreshnessAt(long nowUnixMillis, long maxAgeMillis);
}

/// <summary>
/// One important fact of the World Model: a value with provenance, confidence,
/// observation timestamp and the ability to answer how fresh it is.
/// </summary>
/// <remarks>
/// <para>
/// Invariants enforced by the constructor: an UNKNOWN fact has no value,
/// confidence 0 and <see cref="SensorChannel.None"/>; a known fact has a channel
/// and a confidence in [0, 1] that is never NaN. Timestamps are Unix
/// milliseconds supplied by the caller (see <see cref="IMonotonicClock"/>); the
/// contract never reads a wall clock.
/// </para>
/// <para>
/// The struct is immutable and value-equal, so two snapshots built from the
/// same observations compare equal — the property deterministic replay relies on.
/// </para>
/// </remarks>
public readonly record struct WorldFact<T> : IWorldFact
{
    private readonly T? _value;
    private readonly string? _reason;

    public FactSource Source { get; }
    public SensorChannel Channel { get; }
    public float Confidence { get; }
    public long ObservedAtUnixMillis { get; }
    public bool HasValue { get; }

    /// <summary>Why the fact is UNKNOWN, or an optional note on a known fact. A <c>default</c> struct reports "uninitialized".</summary>
    public string? Reason => _reason ?? (HasValue ? null : "uninitialized");

    public WorldFact(
        T? value,
        FactSource source,
        SensorChannel channel,
        float confidence,
        long observedAtUnixMillis,
        bool hasValue,
        string? reason)
    {
        if (float.IsNaN(confidence) || confidence < 0f || confidence > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "Confidence must be within [0, 1].");
        }

        if (observedAtUnixMillis < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(observedAtUnixMillis), observedAtUnixMillis, "Timestamp must be non-negative Unix milliseconds.");
        }

        if (source == FactSource.Unknown)
        {
            if (hasValue)
            {
                throw new ArgumentException("An UNKNOWN fact cannot carry an observed value.", nameof(hasValue));
            }

            if (confidence != 0f)
            {
                throw new ArgumentException("An UNKNOWN fact must have confidence 0.", nameof(confidence));
            }

            if (channel != SensorChannel.None)
            {
                throw new ArgumentException("An UNKNOWN fact cannot name a sensor channel.", nameof(channel));
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException("An UNKNOWN fact must state why it is unknown.", nameof(reason));
            }
        }
        else
        {
            if (!hasValue)
            {
                throw new ArgumentException("A known fact must carry an observed value.", nameof(hasValue));
            }

            if (channel == SensorChannel.None)
            {
                throw new ArgumentException("A known fact must name the sensor channel that produced it.", nameof(channel));
            }
        }

        _value = value;
        Source = source;
        Channel = channel;
        Confidence = confidence;
        ObservedAtUnixMillis = observedAtUnixMillis;
        HasValue = hasValue;
        _reason = reason;
    }

    /// <summary>
    /// The observed value. Throws when the fact is UNKNOWN: the contract refuses
    /// to hand out <c>default</c> in place of a missing observation.
    /// </summary>
    public T Value => HasValue
        ? _value!
        : throw new InvalidOperationException($"Fact is UNKNOWN ({Reason}); inspect HasValue before reading Value.");

    public bool TryGetValue(out T value)
    {
        if (HasValue)
        {
            value = _value!;
            return true;
        }

        value = default!;
        return false;
    }

    public bool IsSimulated => Source == FactSource.Simulated;

    /// <summary>Whether the runtime may act on this fact: known and not simulated.</summary>
    public bool IsActionable => HasValue && Source.IsActionable();

    /// <summary>Milliseconds elapsed since observation, or null when UNKNOWN.</summary>
    public long? AgeAt(long nowUnixMillis)
        => HasValue ? Math.Max(0, nowUnixMillis - ObservedAtUnixMillis) : null;

    public FactFreshness FreshnessAt(long nowUnixMillis, long maxAgeMillis)
    {
        if (maxAgeMillis < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAgeMillis), maxAgeMillis, "Freshness budget must be non-negative.");
        }

        var age = AgeAt(nowUnixMillis);
        if (age is null)
        {
            return FactFreshness.Unknown;
        }

        return age.Value <= maxAgeMillis ? FactFreshness.Fresh : FactFreshness.Stale;
    }

    public static WorldFact<T> Live(T value, SensorChannel channel, float confidence, long observedAtUnixMillis)
        => new(value, FactSource.Live, channel, confidence, observedAtUnixMillis, true, null);

    public static WorldFact<T> Derived(T value, SensorChannel channel, float confidence, long observedAtUnixMillis, string? reason = null)
        => new(value, FactSource.Derived, channel, confidence, observedAtUnixMillis, true, reason);

    public static WorldFact<T> Cached(T value, SensorChannel channel, float confidence, long observedAtUnixMillis)
        => new(value, FactSource.Cached, channel, confidence, observedAtUnixMillis, true, null);

    public static WorldFact<T> Simulated(T value, float confidence, long observedAtUnixMillis, string? reason = null)
        => new(value, FactSource.Simulated, SensorChannel.Local, confidence, observedAtUnixMillis, true, reason);

    public static WorldFact<T> Unknown(string reason, long atUnixMillis)
        => new(default, FactSource.Unknown, SensorChannel.None, 0f, atUnixMillis, false, reason);

    public static WorldFact<T> NotObserved(long atUnixMillis)
        => Unknown(WorldModelContract.NotObservedReason, atUnixMillis);

    /// <summary>Re-labels a fact as CACHED without touching value, channel or timestamp.</summary>
    public WorldFact<T> AsCached()
        => HasValue
            ? new WorldFact<T>(_value, FactSource.Cached, Channel, Confidence, ObservedAtUnixMillis, true, _reason)
            : this;

    public override string ToString()
    {
        var value = HasValue ? Convert.ToString(_value, CultureInfo.InvariantCulture) ?? "null" : "∅";
        return string.Create(CultureInfo.InvariantCulture,
            $"{Source.ToWire()}[{Channel}] {value} c={Confidence:0.###} t={ObservedAtUnixMillis}{(Reason is null ? string.Empty : " (" + Reason + ")")}");
    }
}
