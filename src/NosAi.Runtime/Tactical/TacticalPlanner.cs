using NosAi.Runtime.Contracts;
using NosAi.Runtime.WorldModel;

namespace NosAi.Runtime.Tactical;

public sealed class TacticalPlanner
{
    private readonly IActionSimulator _simulator;
    private readonly TacticalRanking _ranking;

    public TacticalPlanner(IActionSimulator simulator, TacticalRanking ranking)
    {
        _simulator = simulator;
        _ranking = ranking;
    }

    public IReadOnlyList<RankedAction> Rank(WorldState state, IEnumerable<CandidateAction> candidates)
    {
        var simulated = candidates.Select(action => action with
        {
            UtilityScore = _simulator.Evaluate(state, action)
        });
        return _ranking.Rank(simulated);
    }
}
