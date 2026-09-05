namespace NosAi.Core.WorldModel;

/// <summary>Stato di freschezza di un fatto rispetto a un istante e a una <see cref="FreshnessPolicy"/>.</summary>
public enum Freshness : byte
{
    /// <summary>Età entro <see cref="FreshnessPolicy.FreshMaxMillis"/>.</summary>
    Fresh = 0,

    /// <summary>Età oltre il limite fresco ma entro <see cref="FreshnessPolicy.StaleAfterMillis"/>: utilizzabile con confidenza ridotta dal consumer.</summary>
    Aging = 1,

    /// <summary>Età oltre <see cref="FreshnessPolicy.StaleAfterMillis"/>: non utilizzabile per agire.</summary>
    Stale = 2,

    /// <summary>Il fatto è UNKNOWN oppure il timestamp è nel futuro (disaccordo di clock): nessuna età valida.</summary>
    Unknown = 3
}

/// <summary>Soglie di freschezza in millisecondi. Il valore appartiene al consumer (canale, regola), non al fatto.</summary>
public readonly record struct FreshnessPolicy
{
    public long FreshMaxMillis { get; }
    public long StaleAfterMillis { get; }

    public FreshnessPolicy(long freshMaxMillis, long staleAfterMillis)
    {
        if (freshMaxMillis < 0) throw new ArgumentOutOfRangeException(nameof(freshMaxMillis), freshMaxMillis, "must be >= 0");
        if (staleAfterMillis < freshMaxMillis) throw new ArgumentOutOfRangeException(nameof(staleAfterMillis), staleAfterMillis, "must be >= freshMaxMillis");
        FreshMaxMillis = freshMaxMillis;
        StaleAfterMillis = staleAfterMillis;
    }

    /// <summary>Classifica un'età. Un'età negativa (timestamp nel futuro) è <see cref="Freshness.Unknown"/>, mai "massimamente fresca".</summary>
    public Freshness Classify(long ageMillis)
    {
        if (ageMillis < 0) return Freshness.Unknown;
        if (ageMillis <= FreshMaxMillis) return Freshness.Fresh;
        if (ageMillis <= StaleAfterMillis) return Freshness.Aging;
        return Freshness.Stale;
    }
}

/// <summary>
/// Un fatto del World Model: valore + provenienza + confidenza + istante di
/// osservazione. È l'unità minima con cui ogni fatto importante viaggia nella
/// pipeline; nessun campo importante è mai un valore nudo.
/// </summary>
/// <remarks>
/// Invarianti garantite dal costruttore:
/// <list type="bullet">
/// <item><see cref="Confidence"/> ∈ [0,1] e mai NaN.</item>
/// <item>Un fatto UNKNOWN ha <see cref="HasValue"/> falso, <see cref="Confidence"/> 0 e un <see cref="FailureReason"/> non vuoto.</item>
/// <item>Un fatto non-UNKNOWN non porta <see cref="FailureReason"/>.</item>
/// </list>
/// <see cref="Value"/> su un fatto UNKNOWN è <c>default</c> e non deve essere letto:
/// i consumer controllano <see cref="HasValue"/> o usano <see cref="TryGetValue"/>.
/// </remarks>
public readonly struct Fact<T> : IEquatable<Fact<T>>
{
    public T Value { get; }
    public FactProvenance Provenance { get; }
    public float Confidence { get; }
    public long ObservedAtUnixMillis { get; }
    public string? FailureReason { get; }

    public Fact(T value, FactProvenance provenance, float confidence, long observedAtUnixMillis, string? failureReason = null)
    {
        if (float.IsNaN(confidence) || confidence < 0f || confidence > 1f)
            throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "confidence must be within [0,1]");

        if (provenance.IsUnknown)
        {
            if (string.IsNullOrWhiteSpace(failureReason))
                throw new ArgumentException("an UNKNOWN fact must carry a failure reason", nameof(failureReason));
            if (confidence != 0f)
                throw new ArgumentOutOfRangeException(nameof(confidence), confidence, "an UNKNOWN fact has confidence 0");
            Value = default!;
        }
        else
        {
            if (failureReason is not null)
                throw new ArgumentException("a known fact must not carry a failure reason", nameof(failureReason));
            Value = value;
        }

        Provenance = provenance;
        Confidence = confidence;
        ObservedAtUnixMillis = observedAtUnixMillis;
        FailureReason = failureReason;
    }

    /// <summary>Vero quando il fatto porta un valore (LIVE, DERIVED, CACHED o SIMULATED).</summary>
    public bool HasValue => !Provenance.IsUnknown;

    public bool IsUnknown => Provenance.IsUnknown;

    public bool IsSimulated => Provenance.IsSimulated;

    /// <summary>Vero per un valore reale (LIVE/DERIVED/CACHED), indipendentemente dalla freschezza.</summary>
    public bool IsReal => Provenance.IsReal;

    public FactSourceKind Source => Provenance.Kind;

    public bool TryGetValue(out T value)
    {
        value = Value;
        return HasValue;
    }

    /// <summary>Età in millisecondi rispetto a <paramref name="nowUnixMillis"/>, o null per un fatto UNKNOWN.</summary>
    public long? AgeAt(long nowUnixMillis) => HasValue ? nowUnixMillis - ObservedAtUnixMillis : null;

    public Freshness FreshnessAt(long nowUnixMillis, in FreshnessPolicy policy)
        => AgeAt(nowUnixMillis) is { } age ? policy.Classify(age) : Freshness.Unknown;

    /// <summary>Vero quando il fatto ha un valore ed è stato osservato entro <paramref name="maxAgeMillis"/> (età nel futuro esclusa).</summary>
    public bool IsFresh(long nowUnixMillis, long maxAgeMillis)
    {
        if (maxAgeMillis < 0) return false;
        return AgeAt(nowUnixMillis) is { } age && age >= 0 && age <= maxAgeMillis;
    }

    /// <summary>
    /// Vero quando il fatto può sostenere un'azione reale: valore reale (mai
    /// SIMULATED, mai UNKNOWN), fresco e con confidenza almeno <paramref name="minConfidence"/>.
    /// Questa è una condizione necessaria, non un'autorizzazione: l'autorità resta a Guard/Trust/Safety.
    /// </summary>
    public bool IsActionable(long nowUnixMillis, long maxAgeMillis, float minConfidence = 0f)
        => IsReal && Confidence >= minConfidence && IsFresh(nowUnixMillis, maxAgeMillis);

    /// <summary>Copia con classe di origine CACHED: la stessa osservazione ripubblicata senza nuova lettura.</summary>
    public Fact<T> AsCached()
        => HasValue && !IsSimulated
            ? new Fact<T>(Value, new FactProvenance(FactSourceKind.Cached, Provenance.Channel, Provenance.SensorId), Confidence, ObservedAtUnixMillis)
            : this;

    /// <summary>Copia con confidenza scalata di <paramref name="factor"/> ∈ [0,1]. Su UNKNOWN è identità.</summary>
    public Fact<T> WithConfidenceScaled(float factor)
    {
        if (float.IsNaN(factor) || factor < 0f || factor > 1f)
            throw new ArgumentOutOfRangeException(nameof(factor), factor, "factor must be within [0,1]");
        return HasValue ? new Fact<T>(Value, Provenance, Confidence * factor, ObservedAtUnixMillis) : this;
    }

    public static Fact<T> Live(T value, ObservationChannel channel, float confidence, long observedAtUnixMillis, ushort sensorId = 0)
        => new(value, FactProvenance.Live(channel, sensorId), confidence, observedAtUnixMillis);

    public static Fact<T> Derived(T value, ObservationChannel channel, float confidence, long observedAtUnixMillis, ushort sensorId = 0)
        => new(value, FactProvenance.Derived(channel, sensorId), confidence, observedAtUnixMillis);

    public static Fact<T> Cached(T value, ObservationChannel channel, float confidence, long observedAtUnixMillis, ushort sensorId = 0)
        => new(value, FactProvenance.Cached(channel, sensorId), confidence, observedAtUnixMillis);

    public static Fact<T> Simulated(T value, float confidence, long observedAtUnixMillis, ushort sensorId = 0)
        => new(value, FactProvenance.Simulated(sensorId), confidence, observedAtUnixMillis);

    public static Fact<T> Unknown(string reason, long observedAtUnixMillis = 0)
        => new(default!, FactProvenance.Unknown, 0f, observedAtUnixMillis, reason);

    public bool Equals(Fact<T> other)
        => Provenance.Equals(other.Provenance)
           && Confidence.Equals(other.Confidence)
           && ObservedAtUnixMillis == other.ObservedAtUnixMillis
           && string.Equals(FailureReason, other.FailureReason, StringComparison.Ordinal)
           && (IsUnknown || EqualityComparer<T>.Default.Equals(Value, other.Value));

    public override bool Equals(object? obj) => obj is Fact<T> other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(Provenance, Confidence, ObservedAtUnixMillis, FailureReason, IsUnknown ? 0 : Value?.GetHashCode() ?? 0);

    public static bool operator ==(Fact<T> left, Fact<T> right) => left.Equals(right);

    public static bool operator !=(Fact<T> left, Fact<T> right) => !left.Equals(right);

    public override string ToString()
        => IsUnknown
            ? $"UNKNOWN({FailureReason})"
            : $"{Value}[{Provenance.Kind.ToWire()}/{Provenance.Channel} c={Confidence:0.00} t={ObservedAtUnixMillis}]";
}

/// <summary>Reasons canonici per fatti UNKNOWN condivisi tra produttori, così i consumer possono confrontarli senza parsing.</summary>
public static class UnknownReasons
{
    public const string NotObserved = "not_observed";
    public const string SensorUnavailable = "sensor_unavailable";
    public const string SensorDisagreement = "sensor_disagreement";
    public const string Stale = "stale";
    public const string OutOfRange = "out_of_range";
    public const string NotMapped = "not_mapped";
    public const string EmptySnapshot = "empty_snapshot";
}
