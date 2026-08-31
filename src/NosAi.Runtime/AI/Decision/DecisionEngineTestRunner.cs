// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Versione: 1.0 Beta
// AI — Suite di certificazione del motore decisionale
// ============================================================================

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.AI.Decision;

public static class DecisionEngineTestRunner
{
    /// <summary>
    /// Runs every decision-engine check and reports each one by name (same
    /// contract as the gate runners: no short-circuit, a throwing check is named).
    /// </summary>
    public static async Task<bool> RunAllTestsAsync()
    {
        Console.WriteLine("=== Decision engine checks ===");

        bool allPassed = true;
        allPassed &= Run("An unobserved fact skips the rule instead of reading zero", TestUnobservedFactSkipsRule);
        allPassed &= Run("Skips report why: false condition vs missing observation", TestSkipReasonsAreDistinct);
        allPassed &= Run("Survival priority outranks any combat utility", TestPriorityBeatsUtility);
        allPassed &= Run("Equal priority resolves by utility, then deterministically", TestDeterministicOrdering);
        allPassed &= Run("A decision is no more trusted than its weakest fact", TestProvenanceFolding);
        allPassed &= Run("No observation at all yields no decision", TestBlindContextDecidesNothing);
        allPassed &= Run("Built-in rule set keeps a dying character alive first", TestBuiltInSurvivalOrdering);
        allPassed &= Run("Rules load from a file with all operators", TestRuleFileRoundTrip);
        allPassed &= Run("A malformed rule file is refused, naming the rule", TestMalformedRuleFileRefused);
        allPassed &= Run("Duplicate rule ids are refused", TestDuplicateIdsRefused);
        allPassed &= Run("A missing rule file is reported, not silently empty", TestMissingRuleFileReported);
        allPassed &= Run("The rule set has no hard-coded size limit", TestUnboundedRuleSet);
        allPassed &= await RunAsync("Provider emits a decision with its provenance", TestProviderEmitsDecisionAsync).ConfigureAwait(false);
        allPassed &= await RunAsync("Provider blind: ACTION_NONE classified UNKNOWN", TestProviderBlindIsUnknownAsync).ConfigureAwait(false);

        Console.WriteLine(allPassed
            ? "=== Decision engine checks passed. Local only: not yet driven by observed game data. ==="
            : "=== Decision engine checks FAILED. See the lines marked FAIL above. ===");
        return allPassed;
    }

    private static bool Run(string name, Func<bool> check)
    {
        try { return Report(name, check(), null); }
        catch (Exception ex) { return Report(name, false, $"{ex.GetType().Name}: {ex.Message}"); }
    }

    private static async Task<bool> RunAsync(string name, Func<Task<bool>> check)
    {
        try { return Report(name, await check().ConfigureAwait(false), null); }
        catch (Exception ex) { return Report(name, false, $"{ex.GetType().Name}: {ex.Message}"); }
    }

    private static bool Report(string name, bool passed, string? error)
    {
        string detail = error is null ? string.Empty : $" [{error}]";
        Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] {name}{detail}");
        return passed;
    }

    private static DecisionRule Rule(string id, string action, double utility, int priority, params RuleCondition[] conditions)
        => new(id, action, conditions.ToImmutableArray(), utility, priority);

    // ------------------------------------------------------------- unknown handling

    private static bool TestUnobservedFactSkipsRule()
    {
        var engine = new UtilityDecisionEngine(new[]
        {
            Rule("attack", "ACTION_ATTACK", 0.9, 0, new RuleCondition("player.hp_ratio", ConditionOperator.GreaterThan, 0.5)),
        });

        // HP unobserved: "greater than 0.5" is not false, it is unanswerable. The
        // rule must be skipped rather than evaluated against a defaulted zero.
        var blind = new DecisionContext().WithUnknown("player.hp_ratio", "no_provider");
        DecisionOutcome outcome = engine.Decide(blind);
        if (outcome.HasDecision) return false;
        if (outcome.Skipped.Single().Reason != RuleSkipReason.FactNotObserved) return false;

        // Observed and satisfied: the same rule now fires.
        var seeing = new DecisionContext().WithLive("player.hp_ratio", 0.8);
        return engine.Decide(seeing).Action == "ACTION_ATTACK";
    }

    private static bool TestSkipReasonsAreDistinct()
    {
        var engine = new UtilityDecisionEngine(new[]
        {
            Rule("needs_hp", "A", 0.9, 0, new RuleCondition("player.hp_ratio", ConditionOperator.LessThan, 0.2)),
            Rule("needs_mp", "B", 0.8, 0, new RuleCondition("player.mp_ratio", ConditionOperator.LessThan, 0.2)),
        });
        // HP observed but high (condition false); MP never observed (unanswerable).
        var context = new DecisionContext().WithLive("player.hp_ratio", 0.9);
        DecisionOutcome outcome = engine.Decide(context);

        SkippedRule hp = outcome.Skipped.Single(s => s.RuleId == "needs_hp");
        SkippedRule mp = outcome.Skipped.Single(s => s.RuleId == "needs_mp");
        return !outcome.HasDecision
            && hp.Reason == RuleSkipReason.ConditionFalse
            && mp.Reason == RuleSkipReason.FactNotObserved
            && mp.Detail == "player.mp_ratio";
    }

    // ------------------------------------------------------------- ordering

    private static bool TestPriorityBeatsUtility()
    {
        var engine = new UtilityDecisionEngine(new[]
        {
            Rule("greedy_attack", "ACTION_ATTACK", 1.0, 0, new RuleCondition("target.hp_ratio", ConditionOperator.GreaterThan, 0.0)),
            Rule("flee", "ACTION_FLEE", 0.1, 2, new RuleCondition("player.hp_ratio", ConditionOperator.LessThan, 0.2)),
        });
        var dying = new DecisionContext().WithLive("player.hp_ratio", 0.1).WithLive("target.hp_ratio", 0.9);
        // Survival is a class, not a weight: no utility can outrank it.
        return engine.Decide(dying).Action == "ACTION_FLEE";
    }

    private static bool TestDeterministicOrdering()
    {
        var rules = new[]
        {
            Rule("zzz", "ACTION_Z", 0.5, 0),
            Rule("aaa", "ACTION_A", 0.5, 0),
            Rule("mid", "ACTION_M", 0.7, 0),
        };
        // Same facts must always give the same answer, whatever the input order.
        var forward = new UtilityDecisionEngine(rules).Decide(new DecisionContext());
        var reversed = new UtilityDecisionEngine(rules.Reverse().ToArray()).Decide(new DecisionContext());
        return forward.RuleId == "mid" && reversed.RuleId == "mid";
    }

    // ------------------------------------------------------------- provenance

    private static bool TestProvenanceFolding()
    {
        var engine = new UtilityDecisionEngine(new[]
        {
            Rule("mixed", "ACTION_ATTACK", 0.9, 0,
                new RuleCondition("player.hp_ratio", ConditionOperator.GreaterThan, 0.5),
                new RuleCondition("target.hp_ratio", ConditionOperator.GreaterThan, 0.0)),
        });

        // One LIVE fact and one DERIVED: the decision inherits the weaker one.
        var mixed = new DecisionContext().WithLive("player.hp_ratio", 0.9).WithDerived("target.hp_ratio", 0.4);
        if (engine.Decide(mixed).Source != DataSourceKind.Derived) return false;

        var allLive = new DecisionContext().WithLive("player.hp_ratio", 0.9).WithLive("target.hp_ratio", 0.4);
        if (engine.Decide(allLive).Source != DataSourceKind.Live) return false;

        var simulated = new DecisionContext()
            .With("player.hp_ratio", ClassifiedValue<double>.Simulated(0.9))
            .WithLive("target.hp_ratio", 0.4);
        return engine.Decide(simulated).Source == DataSourceKind.Simulated;
    }

    private static bool TestBlindContextDecidesNothing()
    {
        var engine = new UtilityDecisionEngine(BuiltInRuleSet.Create());
        DecisionOutcome outcome = engine.Decide(new DecisionContext());
        // Every built-in rule needs at least one fact; with none observed the
        // engine must decide nothing rather than pick a default action.
        return !outcome.HasDecision
            && outcome.Action is null
            && outcome.Skipped.Length == engine.RuleCount
            && outcome.Skipped.All(s => s.Reason == RuleSkipReason.FactNotObserved);
    }

    private static bool TestBuiltInSurvivalOrdering()
    {
        var engine = new UtilityDecisionEngine(BuiltInRuleSet.Create());

        var critical = new DecisionContext()
            .WithLive("player.hp_ratio", 0.10).WithLive("target.hp_ratio", 0.05).WithLive("player.in_combat", 1);
        if (engine.Decide(critical).Action != "ACTION_EMERGENCY_FLEE") return false;

        var low = new DecisionContext()
            .WithLive("player.hp_ratio", 0.30).WithLive("target.hp_ratio", 0.05).WithLive("player.in_combat", 1);
        if (engine.Decide(low).Action != "ACTION_USE_POTION") return false;

        // Healthy with a nearly dead target: finish it.
        var healthy = new DecisionContext()
            .WithLive("player.hp_ratio", 0.90).WithLive("target.hp_ratio", 0.10).WithLive("player.in_combat", 1);
        return engine.Decide(healthy).RuleId == "combat.finish_target";
    }

    // ------------------------------------------------------------- rule files

    private static bool TestRuleFileRoundTrip()
    {
        const string json = """
        [
          { "id": "a", "action": "ACTION_FLEE", "utility": 0.99, "priority": 2,
            "when": [ { "fact": "player.hp_ratio", "op": "<", "value": 0.15 } ] },
          { "id": "b", "action": "ACTION_ATTACK", "utility": 0.6, "priority": 1,
            "when": [ { "fact": "target.hp_ratio", "op": "gt", "value": 0 },
                      { "fact": "player.hp_ratio", "op": ">=", "value": 0.5 } ] },
          { "id": "c", "action": "ACTION_REST", "utility": 0.3,
            "when": [ { "fact": "player.in_combat", "op": "==", "value": 0 },
                      { "fact": "player.mp_ratio", "op": "lte", "value": 0.2 } ] }
        ]
        """;
        ImmutableArray<DecisionRule> rules = DecisionRuleLoader.Parse(json);
        if (rules.Length != 3) return false;

        var engine = new UtilityDecisionEngine(rules);
        var dying = new DecisionContext().WithLive("player.hp_ratio", 0.1);
        if (engine.Decide(dying).Action != "ACTION_FLEE") return false;

        var resting = new DecisionContext().WithLive("player.in_combat", 0).WithLive("player.mp_ratio", 0.1);
        return engine.Decide(resting).Action == "ACTION_REST";
    }

    private static bool TestMalformedRuleFileRefused()
    {
        bool unknownOperatorRefused = false, missingActionRefused = false, badJsonRefused = false;
        try { DecisionRuleLoader.Parse("""[{"id":"x","action":"A","when":[{"fact":"f","op":"~~","value":1}]}]"""); }
        catch (InvalidDataException ex) { unknownOperatorRefused = ex.Message.Contains("'x'"); }

        try { DecisionRuleLoader.Parse("""[{"id":"y"}]"""); }
        catch (InvalidDataException ex) { missingActionRefused = ex.Message.Contains("'y'"); }

        try { DecisionRuleLoader.Parse("{ not json"); }
        catch (InvalidDataException) { badJsonRefused = true; }

        // A rule file that silently loses rules is worse than one that fails.
        return unknownOperatorRefused && missingActionRefused && badJsonRefused;
    }

    private static bool TestDuplicateIdsRefused()
    {
        try
        {
            _ = new UtilityDecisionEngine(new[] { Rule("dup", "A", 0.5, 0), Rule("dup", "B", 0.4, 0) });
            return false;
        }
        catch (ArgumentException ex) { return ex.Message.Contains("dup"); }
    }

    private static bool TestMissingRuleFileReported()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"nosai_rules_{Guid.NewGuid():N}.json");
        bool reported = !DecisionRuleLoader.TryLoadFile(missing, out var rules, out string? failure)
            && rules.IsEmpty
            && failure is not null
            && failure.StartsWith("rule_file_not_found:", StringComparison.Ordinal);

        // A real file loads and reports no failure.
        string present = Path.Combine(Path.GetTempPath(), $"nosai_rules_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(present, """[{"id":"a","action":"ACTION_REST","utility":0.5}]""");
            bool loaded = DecisionRuleLoader.TryLoadFile(present, out var real, out string? noFailure)
                && real.Length == 1 && noFailure is null;
            return reported && loaded;
        }
        finally { try { File.Delete(present); } catch { /* best-effort temp cleanup */ } }
    }

    private static bool TestUnboundedRuleSet()
    {
        // "No rule limit" has to mean something: build a large set and check the
        // engine still resolves deterministically and picks the true best.
        var many = Enumerable.Range(0, 5000)
            .Select(i => Rule($"rule_{i:D5}", $"ACTION_{i}", i / 10000.0, 0,
                new RuleCondition("player.hp_ratio", ConditionOperator.GreaterThan, 0.0)))
            .ToArray();
        var engine = new UtilityDecisionEngine(many);
        DecisionOutcome outcome = engine.Decide(new DecisionContext().WithLive("player.hp_ratio", 1.0));
        return engine.RuleCount == 5000 && outcome.RuleId == "rule_04999";
    }

    // ------------------------------------------------------------- provider

    private static async Task<bool> TestProviderEmitsDecisionAsync()
    {
        var context = new DecisionContext().WithDerived("player.hp_ratio", 0.10);
        var provider = new UtilityRuleProvider(new UtilityDecisionEngine(BuiltInRuleSet.Create()), () => context);

        var suggestion = await provider.GenerateDecisionAsync("tick").ConfigureAwait(false);
        return suggestion.ActionIntent == "ACTION_EMERGENCY_FLEE"
            && suggestion.Source == DataSourceKind.Derived      // screen-read HP is DERIVED, never LIVE
            && provider.LastOutcome?.RuleId == "survival.flee"
            && provider.RuleCount == BuiltInRuleSet.Create().Length;
    }

    private static async Task<bool> TestProviderBlindIsUnknownAsync()
    {
        var provider = new UtilityRuleProvider(new UtilityDecisionEngine(BuiltInRuleSet.Create()), () => new DecisionContext());
        var suggestion = await provider.GenerateDecisionAsync("tick").ConfigureAwait(false);
        // With nothing observed the provider must not emit an executable action
        // dressed as a decision: ACTION_NONE at UNKNOWN provenance, confidence 0.
        return suggestion.ActionIntent == "ACTION_NONE"
            && suggestion.Source == DataSourceKind.Unknown
            && suggestion.ConfidenceScore == 0f;
    }
}
