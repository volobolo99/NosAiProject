namespace NosAi.Core.WorldModel;

/// <summary>
/// Aggregato a zero allocazioni dei fatti contenuti in una struttura del World
/// Model. Serve a rispondere alle domande di coerenza senza enumerare campo per
/// campo: "c'è qualcosa di simulato?", "quanto è vecchio il fatto più vecchio?",
/// "qual è la confidenza minima?".
/// </summary>
public struct FactSummary
{
    public int KnownCount { get; private set; }
    public int UnknownCount { get; private set; }
    public int SimulatedCount { get; private set; }
    public int CachedCount { get; private set; }

    /// <summary>Istante del fatto conosciuto più vecchio, o null senza fatti conosciuti.</summary>
    public long? OldestObservedAtUnixMillis { get; private set; }

    /// <summary>Istante del fatto conosciuto più recente, o null senza fatti conosciuti.</summary>
    public long? NewestObservedAtUnixMillis { get; private set; }

    /// <summary>Confidenza minima tra i fatti conosciuti; 1 quando non ce ne sono (nessun fatto = nessuna riduzione).</summary>
    public float MinConfidence { get; private set; }

    public static FactSummary Empty => new() { MinConfidence = 1f };

    public readonly int TotalCount => KnownCount + UnknownCount;

    public readonly bool HasKnownFacts => KnownCount > 0;

    public readonly bool ContainsSimulated => SimulatedCount > 0;

    /// <summary>Età del fatto più vecchio rispetto a <paramref name="nowUnixMillis"/>, o null senza fatti conosciuti.</summary>
    public readonly long? OldestAgeAt(long nowUnixMillis)
        => OldestObservedAtUnixMillis is { } oldest ? nowUnixMillis - oldest : null;

    /// <summary>
    /// Vero quando l'insieme è reale (nessun SIMULATED), non vuoto e il fatto più
    /// vecchio è entro <paramref name="maxAgeMillis"/>. Un'età negativa è un
    /// disaccordo di clock e rende l'insieme non azionabile.
    /// </summary>
    public readonly bool IsActionable(long nowUnixMillis, long maxAgeMillis, float minConfidence = 0f)
    {
        if (!HasKnownFacts || ContainsSimulated || maxAgeMillis < 0) return false;
        if (MinConfidence < minConfidence) return false;
        if (NewestObservedAtUnixMillis is { } newest && newest > nowUnixMillis) return false;
        return OldestAgeAt(nowUnixMillis) is { } age && age >= 0 && age <= maxAgeMillis;
    }

    public void Add<T>(Fact<T> fact)
    {
        if (fact.IsUnknown)
        {
            UnknownCount++;
            return;
        }

        if (KnownCount == 0)
        {
            MinConfidence = fact.Confidence;
            OldestObservedAtUnixMillis = fact.ObservedAtUnixMillis;
            NewestObservedAtUnixMillis = fact.ObservedAtUnixMillis;
        }
        else
        {
            if (fact.Confidence < MinConfidence) MinConfidence = fact.Confidence;
            if (fact.ObservedAtUnixMillis < OldestObservedAtUnixMillis) OldestObservedAtUnixMillis = fact.ObservedAtUnixMillis;
            if (fact.ObservedAtUnixMillis > NewestObservedAtUnixMillis) NewestObservedAtUnixMillis = fact.ObservedAtUnixMillis;
        }

        KnownCount++;
        if (fact.IsSimulated) SimulatedCount++;
        if (fact.Source == FactSourceKind.Cached) CachedCount++;
    }

    public void Merge(in FactSummary other)
    {
        UnknownCount += other.UnknownCount;
        if (other.KnownCount == 0) return;

        if (KnownCount == 0)
        {
            MinConfidence = other.MinConfidence;
            OldestObservedAtUnixMillis = other.OldestObservedAtUnixMillis;
            NewestObservedAtUnixMillis = other.NewestObservedAtUnixMillis;
        }
        else
        {
            if (other.MinConfidence < MinConfidence) MinConfidence = other.MinConfidence;
            if (other.OldestObservedAtUnixMillis < OldestObservedAtUnixMillis) OldestObservedAtUnixMillis = other.OldestObservedAtUnixMillis;
            if (other.NewestObservedAtUnixMillis > NewestObservedAtUnixMillis) NewestObservedAtUnixMillis = other.NewestObservedAtUnixMillis;
        }

        KnownCount += other.KnownCount;
        SimulatedCount += other.SimulatedCount;
        CachedCount += other.CachedCount;
    }

    public void AddAll<TCarrier>(ReadOnlySpan<TCarrier> carriers) where TCarrier : IFactCarrier
    {
        for (int i = 0; i < carriers.Length; i++)
        {
            FactSummary inner = carriers[i].Summarize();
            Merge(in inner);
        }
    }
}

/// <summary>Struttura del World Model che sa riassumere i propri fatti.</summary>
public interface IFactCarrier
{
    FactSummary Summarize();
}
