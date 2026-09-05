using NosAi.Core.WorldModel;

namespace NosAi.Runtime.WorldModel.Fusion;

/// <summary>
/// One channel's reading of a single World Model fact, tagged with the
/// channel's name so a resolution can be explained and reproduced.
/// </summary>
/// <param name="Fact">The channel's own classified reading.</param>
/// <param name="ChannelName">A short, stable identifier for the observation channel (e.g. "network", "memory-position", "screen-ocr"). Used only for tie-breaking and diagnostics -- never to bias which channel wins on trust grounds.</param>
public sealed record FusionCandidate<T>
{
    public WorldFact<T> Fact { get; }
    public string ChannelName { get; }

    public FusionCandidate(WorldFact<T> fact, string channelName)
    {
        ArgumentNullException.ThrowIfNull(fact);
        ArgumentException.ThrowIfNullOrWhiteSpace(channelName);
        Fact = fact;
        ChannelName = channelName;
    }
}

/// <summary>The result of resolving one or more <see cref="FusionCandidate{T}"/>s into a single fused fact.</summary>
/// <param name="Result">The winning fact, unmodified from whichever candidate produced it -- fusion never edits a candidate's own provenance/confidence.</param>
/// <param name="Disagreement">True when at least one other fresh candidate reported a different value than <see cref="Result"/>.</param>
/// <param name="DisagreementDetail">A human-readable explanation of which channels disagreed, or null when there was no disagreement.</param>
public sealed record FusionOutcome<T>(WorldFact<T> Result, bool Disagreement, string? DisagreementDetail);

/// <summary>
/// The single place AP-01+ Sensor Fusion resolves the same World Model fact
/// reported by more than one observation channel (docs/ROADMAP_ESECUTIVA.md
/// S:AP-01 DoD: "conflitti gestiti"). Every field on every contract in
/// <c>NosAi.Core.WorldModel</c> that could ever be reported by two channels
/// (network wire vs. memory reader vs. future screen/OCR) should be resolved
/// through this type rather than each caller inventing its own ad hoc
/// precedence, so the rule stays in exactly one place and is deterministic
/// and reproducible (docs/ROADMAP_ESECUTIVA.md S:AP-01 DoD: "replay
/// deterministico").
///
/// Precedence order, applied in this fixed sequence so two runs over the
/// same candidates always agree: (1) discard any candidate that is Unknown
/// or older than <c>maxAge</c> -- a stale or absent reading never outvotes a
/// fresh one; (2) among what remains, prefer the highest-trust
/// <see cref="DataSourceKind"/> (Live &gt; Derived &gt; Cached &gt;
/// Simulated); (3) tie-break by higher <see cref="WorldFact{T}.Confidence"/>;
/// (4) tie-break by the most recent <see cref="WorldFact{T}.ObservedAtUtc"/>;
/// (5) tie-break by channel name, ordinally, so a genuine four-way tie still
/// resolves to the same winner every time. The winner is returned exactly as
/// its channel reported it: fusion never invents a value, never averages,
/// and never edits a candidate's own provenance.
/// </summary>
public static class FactFusion
{
    /// <summary>
    /// Resolves <paramref name="candidates"/> for one fact into a single
    /// <see cref="FusionOutcome{T}"/>. Every candidate is expected to report
    /// the *same* logical fact (e.g. "the player's current map"); resolving
    /// unrelated facts together is a caller error, not something this method
    /// can detect.
    /// </summary>
    /// <param name="candidates">Zero or more channel readings of the same fact.</param>
    /// <param name="nowUtc">The instant freshness is measured against.</param>
    /// <param name="maxAge">How old a candidate may be and still compete. A candidate exactly at this age still competes; see <see cref="WorldFact{T}.IsFresh"/>.</param>
    /// <param name="comparer">How two values are compared for disagreement. Defaults to <see cref="EqualityComparer{T}.Default"/>.</param>
    public static FusionOutcome<T> Resolve<T>(
        IReadOnlyList<FusionCandidate<T>> candidates,
        DateTime nowUtc,
        TimeSpan maxAge,
        IEqualityComparer<T>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        comparer ??= EqualityComparer<T>.Default;

        List<FusionCandidate<T>> usable = new(candidates.Count);
        foreach (FusionCandidate<T> candidate in candidates)
        {
            if (candidate.Fact.IsFresh(maxAge, nowUtc))
                usable.Add(candidate);
        }

        if (usable.Count == 0)
            return new FusionOutcome<T>(WorldFact<T>.Unknown("no_fresh_channel_reported_this_fact", nowUtc), false, null);

        FusionCandidate<T> winner = usable[0];
        for (int i = 1; i < usable.Count; i++)
        {
            if (IsBetter(usable[i], winner))
                winner = usable[i];
        }

        List<string>? disagreeing = null;
        foreach (FusionCandidate<T> candidate in usable)
        {
            if (!ReferenceEquals(candidate, winner) && !comparer.Equals(candidate.Fact.Value, winner.Fact.Value))
            {
                disagreeing ??= new List<string>();
                disagreeing.Add(candidate.ChannelName);
            }
        }

        return disagreeing is null
            ? new FusionOutcome<T>(winner.Fact, false, null)
            : new FusionOutcome<T>(winner.Fact, true, $"'{winner.ChannelName}' won over disagreeing channel(s): {string.Join(", ", disagreeing)}");
    }

    private static bool IsBetter<T>(FusionCandidate<T> candidate, FusionCandidate<T> current)
    {
        int candidateRank = Rank(candidate.Fact.Source);
        int currentRank = Rank(current.Fact.Source);
        if (candidateRank != currentRank)
            return candidateRank > currentRank;

        if (candidate.Fact.Confidence != current.Fact.Confidence)
            return candidate.Fact.Confidence > current.Fact.Confidence;

        if (candidate.Fact.ObservedAtUtc != current.Fact.ObservedAtUtc)
            return candidate.Fact.ObservedAtUtc > current.Fact.ObservedAtUtc;

        return string.CompareOrdinal(candidate.ChannelName, current.ChannelName) < 0;
    }

    private static int Rank(DataSourceKind kind) => kind switch
    {
        DataSourceKind.Live => 4,
        DataSourceKind.Derived => 3,
        DataSourceKind.Cached => 2,
        DataSourceKind.Simulated => 1,
        _ => 0
    };
}
