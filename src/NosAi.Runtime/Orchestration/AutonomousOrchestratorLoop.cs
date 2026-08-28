using NosAi.Runtime.Contracts;
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

    public AutonomousOrchestratorLoop(Orchestrator orchestrator, IWorldModel worldModel, Func<CandidateAction, bool> execute, Func<CandidateAction, bool> verify, int maxSteps = 8, int maxRetries = 2)
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
            if (candidates.Length == 0) return new(false, "no_candidate", trace);
            var guard = _orchestrator.Tick(maxTrustTier, candidates);
            var selected = candidates.FirstOrDefault(c => c.Id == guard.Action.Id) ?? candidates[0];
            var verified = false;
            for (var attempt = 1; attempt <= _maxRetries + 1; attempt++)
            {
                var executed = _execute(selected);
                verified = executed && _verify(selected);
                trace.Add(new(step, attempt, selected.Id, executed, verified));
                if (verified) break;
            }
            if (!verified) return new(false, "verification_failed", trace);
        }
        return new(true, "step_budget_reached", trace);
    }
}

public sealed record AutonomousStepTrace(int Step, int Attempt, string ActionId, bool Executed, bool Verified);
public sealed record AutonomousLoopResult(bool Succeeded, string Reason, IReadOnlyList<AutonomousStepTrace> Trace);
