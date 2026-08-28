using NosAi.Runtime.Contracts;
using NosAi.Runtime.Guard;
using NosAi.Runtime.Safety;
using NosAi.Runtime.Tactical;
using NosAi.Runtime.WorldModel;

namespace NosAi.Runtime.Orchestration;

/// <summary>Closed-loop orchestration shell. Execution remains behind Guard/Safety.</summary>
public sealed class AutonomousOrchestratorLoop
{
    private readonly Orchestrator _orchestrator;
    private readonly IWorldModel _worldModel;
    private readonly Func<CandidateAction, bool> _execute;
    private readonly Func<CandidateAction, bool> _verify;
    private readonly int _maxSteps;
    private readonly int _maxRetries;

    public AutonomousOrchestratorLoop(
        Orchestrator orchestrator,
        IWorldModel worldModel,
        Func<CandidateAction, bool> execute,
        Func<CandidateAction, bool> verify,
        int maxSteps = 8,
        int maxRetries = 2)
    {
        _orchestrator = orchestrator;
        _worldModel = worldModel;
        _execute = execute;
        _verify = verify;
        _maxSteps = Math.Max(1, maxSteps);
        _maxRetries = Math.Max(0, maxRetries);
    }

    public AutonomousLoopResult Run(TrustTier maxTrustTier, Func<WorldState, IEnumerable<CandidateAction>> candidateFactory)
    {
        var trace = new List<AutonomousStepTrace>();
        for (var step = 0; step < _maxSteps; step++)
        {
            var candidates = candidateFactory(_worldModel.Current).ToArray();
            var guard = _orchestrator.Tick(maxTrustTier, candidates);
            var selected = candidates.FirstOrDefault(c => c.Id == guard.Action.Id)
                ?? candidates.FirstOrDefault();

            if (selected is null)
                return new AutonomousLoopResult(false, "no_candidate", trace);

            var attempt = 0;
            while (attempt <= _maxRetries)
            {
                attempt++;
                var executed = _execute(selected);
                var verified = executed && _verify(selected);
                trace.Add(new AutonomousStepTrace(step, attempt, selected.Id, executed, verified));
                if (verified) break;
                if (attempt > _maxRetries)
                    return new AutonomousLoopResult(false, "verification_failed", trace);
            }
        }

        return new AutonomousLoopResult(true, "step_budget_reached", trace);
    }
}

public sealed record AutonomousStepTrace(int Step, int Attempt, string ActionId, bool Executed, bool Verified);
public sealed record AutonomousLoopResult(bool Succeeded, string Reason, IReadOnlyList<AutonomousStepTrace> Trace);
