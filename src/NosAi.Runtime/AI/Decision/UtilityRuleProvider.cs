// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// AI — Real decision provider over the utility engine
// ============================================================================
//
// Replaces HeuristicRuleProvider (two hardcoded rules: critical HP → potion,
// otherwise carry on) with the utility engine loaded from file. Decisions stay
// DERIVED — real deterministic logic over observed facts — and are never
// promoted to LIVE, which is reserved for what was actually observed.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate5;

namespace NosAi.Runtime.AI.Decision;

/// <summary>
/// Real decision provider: an unbounded, file-driven utility rule engine.
/// </summary>
/// <remarks>
/// This is genuine deterministic logic, not a stand-in, so its suggestions are
/// classified by the provenance of the facts they were taken on rather than
/// marked SIMULATED. With no fact observed it produces no decision at all — the
/// runtime then does nothing, which is the correct behaviour for an automation
/// that cannot see.
/// </remarks>
public sealed class UtilityRuleProvider : IDecisionProvider
{
    private readonly UtilityDecisionEngine _engine;
    private readonly Func<DecisionContext> _contextSource;

    public ProviderType Type => ProviderType.HeuristicRuleEngine;
    public bool IsLoaded => true;

    /// <summary>Rules currently loaded; 0 means the runtime cannot decide anything.</summary>
    public int RuleCount => _engine.RuleCount;

    /// <summary>The outcome of the last decision, for the audit trail.</summary>
    public DecisionOutcome? LastOutcome { get; private set; }

    public UtilityRuleProvider(UtilityDecisionEngine engine, Func<DecisionContext> contextSource)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _contextSource = contextSource ?? throw new ArgumentNullException(nameof(contextSource));
    }

    public Task<bool> LoadModelAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

    public Task<DecisionSuggestion> GenerateDecisionAsync(string promptContext, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stopwatch = Stopwatch.StartNew();
        DecisionOutcome outcome = _engine.Decide(_contextSource());
        stopwatch.Stop();
        LastOutcome = outcome;

        if (!outcome.HasDecision)
        {
            // No decision is a real answer, and it must not look like an action.
            // UNKNOWN provenance keeps a caller from executing "ACTION_NONE".
            return Task.FromResult(new DecisionSuggestion(
                Guid.NewGuid(), Type, "ACTION_NONE", 0f,
                $"Nessuna decisione: {outcome.Rationale}. Regole caricate: {_engine.RuleCount}.",
                stopwatch.ElapsedMilliseconds, DateTime.UtcNow, DataSourceKind.Unknown));
        }

        return Task.FromResult(new DecisionSuggestion(
            Guid.NewGuid(), Type, outcome.Action!,
            (float)Math.Clamp(outcome.Utility, 0.0, 1.0),
            $"{outcome.Rationale} (utility={outcome.Utility:0.###}, priority={outcome.Priority})",
            stopwatch.ElapsedMilliseconds, DateTime.UtcNow, outcome.Source));
    }

    public Task UnloadModelAsync() => Task.CompletedTask;
}

/// <summary>
/// The rule set the runtime falls back to when no file is present on the volume.
/// </summary>
/// <remarks>
/// It is deliberately small and survival-first: enough to keep a character alive
/// while the operator authors the real set on the SSD, and never a pretence of a
/// complete strategy. <see cref="DecisionRuleLoader"/> reports when the file was
/// missing, so running on this set is visible rather than assumed.
/// </remarks>
public static class BuiltInRuleSet
{
    public const int SurvivalPriority = 2;
    public const int CombatPriority = 1;
    public const int RoutinePriority = 0;

    public static ImmutableArray<DecisionRule> Create() => ImmutableArray.Create(
        new DecisionRule("survival.flee", "ACTION_EMERGENCY_FLEE",
            ImmutableArray.Create(new RuleCondition("player.hp_ratio", ConditionOperator.LessThan, 0.15)),
            0.99, SurvivalPriority, "HP critico: disimpegno immediato"),

        new DecisionRule("survival.potion", "ACTION_USE_POTION",
            ImmutableArray.Create(new RuleCondition("player.hp_ratio", ConditionOperator.LessThan, 0.35)),
            0.95, SurvivalPriority, "HP basso: recupero prima di continuare"),

        new DecisionRule("survival.rest_mp", "ACTION_REST",
            ImmutableArray.Create(
                new RuleCondition("player.mp_ratio", ConditionOperator.LessThan, 0.15),
                new RuleCondition("player.in_combat", ConditionOperator.Equal, 0)),
            0.70, RoutinePriority, "MP esaurito fuori combattimento: riposo"),

        new DecisionRule("combat.finish_target", "ACTION_ATTACK_TARGET",
            ImmutableArray.Create(
                new RuleCondition("target.hp_ratio", ConditionOperator.GreaterThan, 0.0),
                new RuleCondition("target.hp_ratio", ConditionOperator.LessOrEqual, 0.25),
                new RuleCondition("player.hp_ratio", ConditionOperator.GreaterOrEqual, 0.35)),
            0.90, CombatPriority, "Bersaglio quasi morto: finirlo prima che rigeneri"),

        new DecisionRule("combat.engage", "ACTION_ATTACK_TARGET",
            ImmutableArray.Create(
                new RuleCondition("target.hp_ratio", ConditionOperator.GreaterThan, 0.0),
                new RuleCondition("player.hp_ratio", ConditionOperator.GreaterOrEqual, 0.50)),
            0.60, CombatPriority, "Bersaglio valido e vita sufficiente: attacco"),

        new DecisionRule("routine.loot", "ACTION_COLLECT_ITEM",
            ImmutableArray.Create(
                new RuleCondition("ground_items.count", ConditionOperator.GreaterThan, 0),
                new RuleCondition("player.in_combat", ConditionOperator.Equal, 0)),
            0.40, RoutinePriority, "Drop a terra fuori combattimento: raccolta"),

        new DecisionRule("routine.seek", "ACTION_SEEK_TARGET",
            ImmutableArray.Create(
                new RuleCondition("monsters.count", ConditionOperator.GreaterThan, 0),
                new RuleCondition("player.in_combat", ConditionOperator.Equal, 0),
                new RuleCondition("player.hp_ratio", ConditionOperator.GreaterOrEqual, 0.60)),
            0.30, RoutinePriority, "Nessun bersaglio ingaggiato ma mostri presenti: avvicinamento"));
}
