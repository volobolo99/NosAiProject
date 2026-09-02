using System.Collections.Generic;
using System.Collections.Immutable;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Safety;

/// <summary>
/// A Guard policy decision with its reasoning attached.
/// </summary>
/// <remarks>
/// Gate 6 previously reduced this to <c>bool</c> plus an <c>out string</c>. The
/// decision was identical; what was lost was the assessed risk and the list of
/// constraints violated, which is what makes a refusal reviewable afterwards.
/// </remarks>
public sealed record GuardEvaluationResult(
    bool IsAllowedByPolicy,
    float AssessedRisk,
    string Rationale,
    ImmutableArray<string> ViolatedConstraints);

/// <summary>Applies the operating policy that decides whether an action may proceed.</summary>
public sealed class GuardPolicyEngine
{
    public GuardEvaluationResult Evaluate(
        ActionCandidate candidate,
        PredictedOutcome outcome,
        RuntimeMode currentMode)
    {
        var violations = new List<string>();

        if (currentMode == RuntimeMode.Stopped)
        {
            violations.Add("Runtime in stato STOPPED: tutte le azioni sono inibite.");
            return new GuardEvaluationResult(
                false,
                1.0f,
                "Blocco fail-closed Watchdog.",
                violations.ToImmutableArray());
        }

        if (currentMode == RuntimeMode.Cooling &&
            candidate.Type is ActionType.UseSkill or ActionType.UseBasicAttack)
        {
            violations.Add("Runtime in stato COOLING: inibite azioni di combattimento non necessarie.");
            return new GuardEvaluationResult(
                false,
                0.8f,
                "Throttling termico attivo.",
                violations.ToImmutableArray());
        }

        if (outcome.RiskScore > 0.75f && candidate.Type != ActionType.EmergencyFlee)
        {
            violations.Add($"Rischio stimato eccessivo ({outcome.RiskScore:P1} > 75%).");
            return new GuardEvaluationResult(
                false,
                outcome.RiskScore,
                "Violazione soglia rischio massimo.",
                violations.ToImmutableArray());
        }

        return new GuardEvaluationResult(
            true,
            outcome.RiskScore,
            "Azione conforme alle policy operative.",
            ImmutableArray<string>.Empty);
    }

    /// <summary>
    /// The boolean form Gate 6 used, kept so its call sites read unchanged.
    /// </summary>
    /// <remarks>
    /// It answers the same question as <see cref="Evaluate"/> and delegates to it,
    /// so the two can no longer drift apart. Callers that need the assessed risk or
    /// the violated constraints should use <see cref="Evaluate"/> directly.
    /// </remarks>
    public bool EvaluatePolicy(
        ActionCandidate candidate,
        PredictedOutcome outcome,
        RuntimeMode currentMode,
        out string? violation)
    {
        GuardEvaluationResult result = Evaluate(candidate, outcome, currentMode);
        violation = result.IsAllowedByPolicy ? null : result.Rationale;
        return result.IsAllowedByPolicy;
    }
}
