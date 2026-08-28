using System.Diagnostics;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Orchestration;

/// <summary>Bounded autonomous runtime. Model output remains untrusted data.</summary>
public sealed class AutonomousAgentRuntime
{
    private readonly IAgentPlanner _planner;
    private readonly IAgentExecutor _executor;
    private readonly IAgentVerifier _verifier;
    private readonly IGuardAi _guard;
    private readonly ISafetyGate _safety;
    private readonly AutonomousRuntimeOptions _options;

    public AutonomousAgentRuntime(IAgentPlanner planner, IAgentExecutor executor,
        IAgentVerifier verifier, IGuardAi guard, ISafetyGate safety,
        AutonomousRuntimeOptions? options = null)
    {
        _planner = planner;
        _executor = executor;
        _verifier = verifier;
        _guard = guard;
        _safety = safety;
        _options = options ?? new AutonomousRuntimeOptions();
    }

    public AutonomousRunResult Run(object context, TrustTier maxAllowedTier)
    {
        var plan = _planner.Plan(context);
        var traces = new List<AutonomousStepTrace>();
        var replans = 0;
        var actions = 0;
        var index = 0;
        var retries = 0;

        while (index < plan.Steps.Count && index < _options.MaxSteps)
        {
            if (actions >= _options.MaxActions)
                return Fail(plan, traces, replans, "ACTION_BUDGET_EXHAUSTED");

            var step = plan.Steps[index];
            var guard = _guard.Evaluate(step.Action, maxAllowedTier);
            var authorized = step.Action.RequiredTrustTier <= maxAllowedTier
                && guard.Allowed
                && _safety.Authorize(step.Action, guard);
            if (!authorized)
            {
                traces.Add(new(index, step.Id, "BLOCKED", retries, guard.Reason));
                return Fail(plan, traces, replans, "TRUST_GUARD_SAFETY_REJECTED");
            }

            try
            {
                var sw = Stopwatch.StartNew();
                var observed = _executor.Execute(step.Action);
                sw.Stop();
                actions++;
                var verification = _verifier.Verify(step.Action, observed);
                if (verification.Passed)
                {
                    traces.Add(new(index, step.Id, "VERIFIED", retries + 1, verification.Reason));
                    index++;
                    retries = 0;
                    continue;
                }

                retries++;
                traces.Add(new(index, step.Id, "VERIFY_FAILED", retries, verification.Reason));
                if (retries <= _options.MaxRetriesPerStep) continue;
                if (replans >= _options.MaxReplans)
                    return Fail(plan, traces, replans, verification.Reason);

                replans++;
                plan = _planner.Replan(new { Context = context, RecoveryReason = verification.Reason }, index, verification.Reason);
                index = 0;
                retries = 0;
                if (plan.Steps.Count == 0) return Fail(plan, traces, replans, "EMPTY_REPLAN");
            }
            catch (Exception ex)
            {
                actions++;
                retries++;
                traces.Add(new(index, step.Id, "EXECUTOR_ERROR", retries, ex.GetType().Name));
                if (retries <= _options.MaxRetriesPerStep) continue;
                if (replans >= _options.MaxReplans) return Fail(plan, traces, replans, "EXECUTOR_ERROR");
                replans++;
                plan = _planner.Replan(new { Context = context, RecoveryReason = ex.GetType().Name }, index, ex.GetType().Name);
                index = 0;
                retries = 0;
                if (plan.Steps.Count == 0) return Fail(plan, traces, replans, "EMPTY_REPLAN");
            }
        }

        if (index >= _options.MaxSteps) return Fail(plan, traces, replans, "STEP_BUDGET_EXHAUSTED");
        return new(plan, true, replans, traces, "PLAN_COMPLETED");
    }

    private static AutonomousRunResult Fail(AgentPlan plan, List<AutonomousStepTrace> traces, int replans, string reason)
        => new(plan, false, replans, traces, reason);
}

public sealed record AutonomousStepTrace(int Index, string StepId, string Status, int Attempts, string Reason);
public sealed record AutonomousRunResult(AgentPlan Plan, bool Completed, int Replans,
    IReadOnlyList<AutonomousStepTrace> Traces, string Reason);
