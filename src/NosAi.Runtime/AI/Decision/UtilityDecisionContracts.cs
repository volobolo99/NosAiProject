// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Versione: 1.0 Beta
// AI — Motore decisionale a utilità: contratti, contesto e regole
// ============================================================================
//
// Sostituisce l'euristica a due regole con un motore data-driven senza limite di
// regole: le regole vivono in un file sul volume dedicato (NOSAI-SSD), non nel
// codice, e si ricaricano senza ricompilare.
//
// Principio non negoziabile: una regola può leggere solo fatti OSSERVATI. Un
// fatto UNKNOWN non vale zero e non vale falso — la condizione che lo richiede
// non è valutabile, e la regola viene esclusa invece di decidere sul vuoto
// (ADR-0002, e la stessa lezione del fix "refuse to plan on numbers nobody
// observed" del Gate 3).

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.AI.Decision;

/// <summary>
/// The observed facts a rule may read. Every fact is classified: a value that
/// nobody observed is absent, never defaulted.
/// </summary>
public sealed class DecisionContext
{
    private readonly Dictionary<string, ClassifiedValue<double>> _facts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Facts currently known, whatever their provenance.</summary>
    public IReadOnlyCollection<string> FactNames => _facts.Keys;

    /// <summary>Records an observed fact.</summary>
    public DecisionContext With(string name, ClassifiedValue<double> value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        _facts[name] = value;
        return this;
    }

    /// <summary>Records a fact observed live from the game.</summary>
    public DecisionContext WithLive(string name, double value) => With(name, ClassifiedValue<double>.Live(value));

    /// <summary>Records a fact derived from another observation (e.g. screen reading).</summary>
    public DecisionContext WithDerived(string name, double value) => With(name, ClassifiedValue<double>.Derived(value));

    /// <summary>Records a fact as explicitly unobserved.</summary>
    public DecisionContext WithUnknown(string name, string reason) =>
        With(name, ClassifiedValue<double>.Unknown(reason));

    /// <summary>
    /// Reads a fact. Returns false when the fact was never recorded OR was
    /// recorded as UNKNOWN: both mean "not observed", and a caller must not be
    /// able to tell them apart by accident and read a zero.
    /// </summary>
    public bool TryRead(string name, out double value, out DataSourceKind source)
    {
        value = 0;
        source = DataSourceKind.Unknown;
        if (!_facts.TryGetValue(name, out ClassifiedValue<double>? fact)) return false;
        source = fact.Source;
        if (!fact.HasValue) return false;
        value = fact.Value;
        return true;
    }
}

/// <summary>Comparison a condition applies to a fact.</summary>
public enum ConditionOperator : byte
{
    LessThan = 0,
    LessOrEqual = 1,
    GreaterThan = 2,
    GreaterOrEqual = 3,
    Equal = 4,
    NotEqual = 5,
}

/// <summary>One condition over a single observed fact.</summary>
public sealed record RuleCondition(string Fact, ConditionOperator Operator, double Value)
{
    /// <summary>
    /// Evaluates against the context. <paramref name="evaluable"/> is false when
    /// the fact was not observed: the caller must skip the rule, not treat the
    /// condition as failed — "unknown" and "false" are different answers.
    /// </summary>
    public bool Evaluate(DecisionContext context, out bool evaluable, out DataSourceKind source)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.TryRead(Fact, out double actual, out source))
        {
            evaluable = false;
            return false;
        }
        evaluable = true;
        return Operator switch
        {
            ConditionOperator.LessThan => actual < Value,
            ConditionOperator.LessOrEqual => actual <= Value,
            ConditionOperator.GreaterThan => actual > Value,
            ConditionOperator.GreaterOrEqual => actual >= Value,
            ConditionOperator.Equal => Math.Abs(actual - Value) < 1e-9,
            ConditionOperator.NotEqual => Math.Abs(actual - Value) >= 1e-9,
            _ => false,
        };
    }
}

/// <summary>
/// One decision rule: when every condition holds, the action becomes a candidate
/// with the given utility.
/// </summary>
/// <remarks>
/// <see cref="Priority"/> is an ordering class applied ahead of utility, the same
/// device the tactical ranker uses: retuning utilities can never make a survival
/// rule lose to a damage rule.
/// </remarks>
public sealed record DecisionRule(
    string Id,
    string Action,
    ImmutableArray<RuleCondition> Conditions,
    double Utility,
    int Priority = 0,
    string? Rationale = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id)) throw new ArgumentException("A rule needs an id.");
        if (string.IsNullOrWhiteSpace(Action)) throw new ArgumentException($"Rule '{Id}' needs an action.");
        if (double.IsNaN(Utility) || double.IsInfinity(Utility))
            throw new ArgumentException($"Rule '{Id}' has a non-finite utility.");
        if (Conditions.IsDefault)
            throw new ArgumentException($"Rule '{Id}' has an uninitialised condition set.");
        foreach (RuleCondition condition in Conditions)
        {
            if (string.IsNullOrWhiteSpace(condition.Fact))
                throw new ArgumentException($"Rule '{Id}' has a condition with no fact.");
        }
    }
}

/// <summary>Why a rule did not produce a candidate.</summary>
public enum RuleSkipReason : byte
{
    ConditionFalse = 0,
    FactNotObserved = 1,
}

/// <summary>A rule that did not fire, with the reason it did not.</summary>
public sealed record SkippedRule(string RuleId, RuleSkipReason Reason, string? Detail);

/// <summary>
/// The engine's answer.
/// </summary>
/// <remarks>
/// <see cref="Source"/> is the weakest provenance among the facts the winning
/// rule actually read: a decision taken on screen-derived HP is DERIVED, and one
/// taken on simulated facts is SIMULATED. A decision can never be more trusted
/// than the observations behind it.
/// </remarks>
public sealed record DecisionOutcome(
    bool HasDecision,
    string? Action,
    string? RuleId,
    double Utility,
    int Priority,
    DataSourceKind Source,
    string Rationale,
    ImmutableArray<SkippedRule> Skipped)
{
    public static DecisionOutcome None(string rationale, ImmutableArray<SkippedRule> skipped) =>
        new(false, null, null, 0, 0, DataSourceKind.Unknown, rationale, skipped);
}
