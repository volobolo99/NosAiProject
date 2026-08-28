using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Tactical;

public sealed record RankedAction(CandidateAction Action, double Score, int Rank);

public sealed class TacticalRanking
{
    public IReadOnlyList<RankedAction> Rank(IEnumerable<CandidateAction> candidates)
    {
        return candidates
            .OrderByDescending(a => double.IsFinite(a.UtilityScore) ? a.UtilityScore : double.NegativeInfinity)
            .ThenBy(a => a.Id, StringComparer.Ordinal)
            .Select((action, index) => new RankedAction(action, action.UtilityScore, index + 1))
            .ToArray();
    }
}
