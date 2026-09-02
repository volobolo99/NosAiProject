using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.PlayAi;

public sealed class UtilityAi
{
    public CandidateAction Select(IEnumerable<CandidateAction> candidates)
    {
        return candidates
            .OrderByDescending(x => x.UtilityScore)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .FirstOrDefault()
            ?? new CandidateAction("noop", ActionKind.NoOp, TrustTier.Tier1_Assisted, 0);
    }
}
