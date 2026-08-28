using NosAi.Runtime.Contracts;
using NosAi.Runtime.Guard;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Safety;
using NosAi.Runtime.Tactical;
using NosAi.Runtime.WorldModel;

namespace NosAi.Runtime.Orchestration;

public sealed class Orchestrator
{
    private readonly IPerceptionProvider _perception;
    private readonly IPerceptionWorldAdapter _adapter;
    private readonly IWorldModel _worldModel;
    private readonly TacticalPlanner _planner;
    private readonly IGuardAi _guardAi;
    private readonly ISafetyGate _safetyGate;

    public Orchestrator(
        IPerceptionProvider perception,
        IPerceptionWorldAdapter adapter,
        IWorldModel worldModel,
        TacticalPlanner planner,
        IGuardAi guardAi,
        ISafetyGate safetyGate)
    {
        _perception = perception;
        _adapter = adapter;
        _worldModel = worldModel;
        _planner = planner;
        _guardAi = guardAi;
        _safetyGate = safetyGate;
    }

    public GuardDecision Tick(TrustTier maxTrustTier, IEnumerable<CandidateAction> candidates)
    {
        var snapshot = _perception.Capture();
        _worldModel.Update(_adapter.ToWorldState(snapshot));

        var ranked = _planner.Rank(_worldModel.Current, candidates);
        var selected = ranked.FirstOrDefault()?.Action
            ?? new CandidateAction("noop", ActionKind.NoOp, TrustTier.Tier1, 0);

        var guard = _guardAi.Evaluate(selected, maxTrustTier);
        _safetyGate.Authorize(selected, guard);
        return guard;
    }
}
