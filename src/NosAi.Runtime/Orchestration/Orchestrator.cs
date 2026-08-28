using NosAi.Runtime.Contracts;
using NosAi.Runtime.Guard;
using NosAi.Runtime.Perception;
using NosAi.Runtime.PlayAi;
using NosAi.Runtime.Safety;
using NosAi.Runtime.WorldModel;

namespace NosAi.Runtime.Orchestration;

public sealed class Orchestrator
{
    private readonly IPerceptionProvider _perception;
    private readonly IPerceptionWorldAdapter _adapter;
    private readonly IWorldModel _worldModel;
    private readonly UtilityAi _utilityAi;
    private readonly IGuardAi _guardAi;
    private readonly ISafetyGate _safetyGate;

    public Orchestrator(
        IPerceptionProvider perception,
        IPerceptionWorldAdapter adapter,
        IWorldModel worldModel,
        UtilityAi utilityAi,
        IGuardAi guardAi,
        ISafetyGate safetyGate)
    {
        _perception = perception;
        _adapter = adapter;
        _worldModel = worldModel;
        _utilityAi = utilityAi;
        _guardAi = guardAi;
        _safetyGate = safetyGate;
    }

    public GuardDecision Tick(TrustTier maxTrustTier, IEnumerable<CandidateAction> candidates)
    {
        var snapshot = _perception.Capture();
        _worldModel.Update(_adapter.ToWorldState(snapshot));

        var selected = _utilityAi.Select(candidates);
        var guard = _guardAi.Evaluate(selected, maxTrustTier);
        _safetyGate.Authorize(selected, guard);
        return guard;
    }
}
