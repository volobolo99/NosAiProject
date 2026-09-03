// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// AI — The decide->act gate: whether a decision may be actuated, and under
//      whose authority
// ============================================================================
//
// This is the seam between "decide" (this module) and "act" (the input layer,
// owned elsewhere). It does not press a key or move a mouse — it decides whether
// a DecisionOutcome is even allowed to reach an actuator, and refuses, by name,
// everything the runtime must not act on, before any primitive is touched.
//
// Two refusals matter most:
//   - An UNTRUSTED decision. A decision is only as trustworthy as the weakest
//     fact behind it (the engine folds that into DecisionOutcome.Source). Acting
//     on the real client requires an observation the project actually trusts:
//     Live or Derived. A Simulated, Cached or UNKNOWN decision is refused even
//     when the authority is valid. Today the memory phases classify their reads
//     UNKNOWN until real-session concordance, so this gate refuses actuation of
//     everything they produce — which is exactly correct: the bot must not act on
//     numbers nobody has established.
//   - An UNAUTHORISED act. Emission needs a named authority (ADR-0020): the
//     cycle's SafetyToken, or an operator command. This gate checks the authority
//     is usable before clearing the act, so an expired or absent authority stops
//     here rather than at the actuator.
//
// It never turns an action into a concrete click or keystroke: that binding needs
// the screen projection and a keybinding map owned by the actuation layer, and
// inventing either here would be exactly the fabricated contract the project
// forbids. The concrete emit is the actuation layer's job, invoked only for an
// action this gate has cleared, under the authority it returns.

using System;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.LowLevel;

namespace NosAi.Runtime.AI.Decision;

/// <summary>The gate's verdict: clear to actuate, or refused with a named reason.</summary>
public readonly record struct ActuationVerdict(
    bool ShouldActuate,
    string? Action,
    ActuationAuthority Authority,
    string? RefusalReason);

/// <summary>Decides whether a <see cref="DecisionOutcome"/> may be actuated at all.</summary>
public static class DecisionActuationPolicy
{
    /// <summary>Refused because the engine produced no decision to act on.</summary>
    public const string NoDecisionReason = "actuation_refused_no_decision";

    /// <summary>Refused because the decision rests on an observation the project does not trust.</summary>
    public const string UntrustedSourcePrefix = "actuation_refused_untrusted_source";

    /// <summary>
    /// The only provenances the runtime will act on against the real client.
    /// </summary>
    /// <remarks>
    /// Live and Derived only. Simulated is a test/stand-in value; Cached is stale;
    /// Unknown is not observed at all. None of the three is a basis for touching
    /// the real game, and the gate says so rather than letting a plausible-looking
    /// but untrusted decision through.
    /// </remarks>
    public static bool IsActuatableSource(DataSourceKind source)
        => source is DataSourceKind.Live or DataSourceKind.Derived;

    /// <summary>
    /// Evaluates a decision for actuation. Order is deliberate: no decision, then
    /// untrusted source, then authority — so the reason names the first thing that
    /// was wrong, and an untrusted decision is refused even when the authority is
    /// perfectly valid.
    /// </summary>
    public static ActuationVerdict Evaluate(DecisionOutcome outcome, ActuationAuthority authority, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        if (!outcome.HasDecision)
            return new ActuationVerdict(false, null, authority, NoDecisionReason);

        if (!IsActuatableSource(outcome.Source))
            return new ActuationVerdict(false, outcome.Action, authority,
                $"{UntrustedSourcePrefix}:{outcome.Source.ToWire()}");

        if (!authority.IsUsable(nowUtc, out string? authorityRefusal))
            return new ActuationVerdict(false, outcome.Action, authority, authorityRefusal);

        return new ActuationVerdict(true, outcome.Action, authority, null);
    }
}
