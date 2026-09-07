// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// AI — Certification suite for the decision engine
// ============================================================================

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.LowLevel;

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
        allPassed &= Run("A ready skill (cooldown+MP observed) is preferred over the basic attack", TestSkillPreferredWhenReady);
        allPassed &= Run("An unknown cooldown falls back to the basic attack, never a blind cast", TestUnknownCooldownFallsBackToBasicAttack);
        allPassed &= Run("No MP skips the skill even when the cooldown is ready", TestNoMpSkipsTheSkill);
        allPassed &= Run("Vitals adapter mirrors the phase: not-established vitals are UNKNOWN facts", TestVitalsAdapterUnknownWhileNotEstablished);
        allPassed &= Run("Vitals adapter carries the phase's refusal reason, never a value", TestVitalsAdapterRefusalCarriesReason);
        allPassed &= Run("Vitals ratio flows with provenance once the phase trusts it", TestVitalsRatioFlowsWhenTrusted);
        allPassed &= Run("A trusted vitals fact actually drives a decision end to end", TestTrustedVitalsDrivesDecision);
        allPassed &= Run("Actuation gate refuses when there is no decision", TestActuationRefusedNoDecision);
        allPassed &= Run("Actuation gate refuses an untrusted decision even under valid authority", TestActuationRefusedUntrustedSource);
        allPassed &= Run("Actuation gate refuses without a usable authority", TestActuationRefusedNoAuthority);
        allPassed &= Run("A trusted decision under a usable authority is cleared to actuate", TestActuationClearedWhenTrustedAndAuthorised);
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

    // ------------------------------------------------------------- consuming Fase 2 (MP) + Fase 3 (cooldown)

    /// <summary>A full-HP target so combat.finish_target (0.90) does not pre-empt the skill rule.</summary>
    private static DecisionContext CombatContext() => new DecisionContext()
        .WithLive("player.hp_ratio", 0.90)
        .WithLive("target.hp_ratio", 0.80)
        .WithLive("player.in_combat", 1);

    private static bool TestSkillPreferredWhenReady()
    {
        var engine = new UtilityDecisionEngine(BuiltInRuleSet.Create());
        var ctx = CombatContext()
            .WithLive("player.mp_ratio", 0.60)
            .WithLive("skill.primary.cooldown_ready", 1);   // Fase 3 observed the skill off cooldown

        DecisionOutcome outcome = engine.Decide(ctx);
        // A ready skill (0.80) beats the basic attack (combat.engage, 0.60).
        return outcome.Action == "ACTION_CAST_SKILL" && outcome.RuleId == "combat.skill_ready";
    }

    private static bool TestUnknownCooldownFallsBackToBasicAttack()
    {
        var engine = new UtilityDecisionEngine(BuiltInRuleSet.Create());
        var ctx = CombatContext()
            .WithLive("player.mp_ratio", 0.60)
            .WithUnknown("skill.primary.cooldown_ready", "cooldown_not_established");   // Fase 3's honest default

        DecisionOutcome outcome = engine.Decide(ctx);
        // The skill rule is skipped for the unobserved cooldown, not evaluated as
        // ready: the basic attack fires instead. A blind cast is what Verify would
        // fail one by one.
        if (outcome.Action != "ACTION_ATTACK_TARGET" || outcome.RuleId != "combat.engage") return false;
        return outcome.Skipped.Any(s => s.RuleId == "combat.skill_ready" && s.Reason == RuleSkipReason.FactNotObserved);
    }

    private static bool TestNoMpSkipsTheSkill()
    {
        var engine = new UtilityDecisionEngine(BuiltInRuleSet.Create());
        var ctx = CombatContext()
            .WithLive("player.mp_ratio", 0.05)              // observed, but too low to cast
            .WithLive("skill.primary.cooldown_ready", 1);

        DecisionOutcome outcome = engine.Decide(ctx);
        // MP is observed and fails the condition (a false condition, not unknown),
        // so the skill rule does not fire and the basic attack does.
        if (outcome.Action != "ACTION_ATTACK_TARGET") return false;
        return outcome.Skipped.Any(s => s.RuleId == "combat.skill_ready" && s.Reason == RuleSkipReason.ConditionFalse);
    }

    // ------------------------------------------------------------- wiring: Fase 2 vitals -> facts

    private static bool TestVitalsAdapterUnknownWhileNotEstablished()
    {
        // The only candidate the phase can hand out today: numbers present
        // (HasValue), but Source == Unknown (not yet established via concordance).
        var vitals = new PlayerVitalsCandidate(80, 100, 30, 60, default, 0, PlayerVitalsCandidate.NotEstablishedReason);
        if (!vitals.HasValue || vitals.Source != DataSourceKind.Unknown) return false;   // guard the premise

        var ctx = new DecisionContext();
        GameplayVitalsAdapter.Populate(ctx, vitals);

        // Both facts must be present-as-UNKNOWN, never a readable number: the
        // runtime must not act on vitals the phase has not established.
        bool hpReadable = ctx.TryRead(GameplayVitalsAdapter.PlayerHpRatioFact, out _, out DataSourceKind hpSrc);
        bool mpReadable = ctx.TryRead(GameplayVitalsAdapter.PlayerMpRatioFact, out _, out DataSourceKind mpSrc);
        return !hpReadable && hpSrc == DataSourceKind.Unknown
            && !mpReadable && mpSrc == DataSourceKind.Unknown
            && ctx.FactNames.Contains(GameplayVitalsAdapter.PlayerHpRatioFact);
    }

    private static bool TestVitalsAdapterRefusalCarriesReason()
    {
        // A refused read (predicate failure) is not a value: it maps to UNKNOWN
        // carrying the phase's own reason, and never the last good number.
        var refused = PlayerVitalsCandidate.Missing(PlayerVitalsBlock.MaxMpZeroReason);
        ClassifiedValue<double> mp = GameplayVitalsAdapter.MapMp(refused);
        return !mp.HasValue
            && mp.Source == DataSourceKind.Unknown
            && mp.FailureReason == PlayerVitalsBlock.MaxMpZeroReason;
    }

    private static bool TestVitalsRatioFlowsWhenTrusted()
    {
        // When the phase eventually grants trust (Source != Unknown), the ratio
        // flows with that provenance -- and out-of-range inputs are clamped, not
        // passed through. Exercised through the core because the candidate type
        // cannot yet express a trusted Source.
        ClassifiedValue<double> derived = GameplayVitalsAdapter.RatioCore(50, 200, DataSourceKind.Derived, hasValue: true, reason: "x");
        if (!derived.HasValue || derived.Source != DataSourceKind.Derived || Math.Abs(derived.Value - 0.25) > 1e-9) return false;

        ClassifiedValue<double> clamped = GameplayVitalsAdapter.RatioCore(250, 200, DataSourceKind.Live, hasValue: true, reason: "x");
        if (Math.Abs(clamped.Value - 1.0) > 1e-9) return false;

        // A zero max is refused rather than dividing, even when hasValue is set.
        ClassifiedValue<double> zeroMax = GameplayVitalsAdapter.RatioCore(10, 0, DataSourceKind.Derived, hasValue: true, reason: "x");
        return !zeroMax.HasValue && zeroMax.FailureReason == GameplayVitalsAdapter.MaxZeroReason;
    }

    private static bool TestTrustedVitalsDrivesDecision()
    {
        // Proof that the wiring closes observe->decide: once a vitals fact is
        // trusted, it drives a real rule. Critical HP (0.10) must trigger the
        // flee, exactly as it would from any other observed source.
        var engine = new UtilityDecisionEngine(BuiltInRuleSet.Create());
        var ctx = new DecisionContext()
            .With("player.hp_ratio", GameplayVitalsAdapter.RatioCore(10, 100, DataSourceKind.Derived, hasValue: true, reason: "x"));

        DecisionOutcome outcome = engine.Decide(ctx);
        return outcome.Action == "ACTION_EMERGENCY_FLEE"
            && outcome.RuleId == "survival.flee"
            && outcome.Source == DataSourceKind.Derived;
    }

    // ------------------------------------------------------------- decide -> act gate

    /// <summary>A real Derived decision: critical HP drives survival.flee at Derived provenance.</summary>
    private static DecisionOutcome DerivedFlee()
    {
        var engine = new UtilityDecisionEngine(BuiltInRuleSet.Create());
        return engine.Decide(new DecisionContext().WithDerived("player.hp_ratio", 0.10));
    }

    private static bool TestActuationRefusedNoDecision()
    {
        // Nothing observed -> no decision -> nothing to actuate.
        var engine = new UtilityDecisionEngine(BuiltInRuleSet.Create());
        DecisionOutcome none = engine.Decide(new DecisionContext());
        ActuationVerdict verdict = DecisionActuationPolicy.Evaluate(none, ActuationAuthority.Commanded("test"), DateTime.UtcNow);
        return !verdict.ShouldActuate && verdict.RefusalReason == DecisionActuationPolicy.NoDecisionReason;
    }

    private static bool TestActuationRefusedUntrustedSource()
    {
        // The safety heart of the gate: a decision taken on a SIMULATED fact is
        // refused for actuation even when the authority is perfectly valid. This
        // is what stops the bot acting on the real client from unverified data --
        // and today the memory phases are UNKNOWN, so this branch is what holds.
        var engine = new UtilityDecisionEngine(BuiltInRuleSet.Create());
        DecisionOutcome simulated = engine.Decide(
            new DecisionContext().With("player.hp_ratio", ClassifiedValue<double>.Simulated(0.10)));
        if (simulated.Source != DataSourceKind.Simulated) return false;   // guard the premise

        ActuationVerdict verdict = DecisionActuationPolicy.Evaluate(simulated, ActuationAuthority.Commanded("test"), DateTime.UtcNow);
        return !verdict.ShouldActuate
            && verdict.Action == "ACTION_EMERGENCY_FLEE"
            && verdict.RefusalReason == $"{DecisionActuationPolicy.UntrustedSourcePrefix}:SIMULATED";
    }

    private static bool TestActuationRefusedNoAuthority()
    {
        // A trusted decision is still refused with no authority: an unattributable
        // act does not proceed (ADR-0020). default(ActuationAuthority) is Kind None.
        ActuationVerdict verdict = DecisionActuationPolicy.Evaluate(DerivedFlee(), default, DateTime.UtcNow);
        return !verdict.ShouldActuate
            && verdict.RefusalReason == ActuationAuthority.MissingReason;
    }

    private static bool TestActuationClearedWhenTrustedAndAuthorised()
    {
        // Trusted decision + usable authority -> cleared to actuate, carrying the
        // authority forward for the actuation layer to emit under.
        DecisionOutcome flee = DerivedFlee();
        if (flee.Source != DataSourceKind.Derived) return false;   // guard the premise

        var authority = ActuationAuthority.Commanded("operator_test");
        ActuationVerdict verdict = DecisionActuationPolicy.Evaluate(flee, authority, DateTime.UtcNow);
        return verdict.ShouldActuate
            && verdict.RefusalReason is null
            && verdict.Action == "ACTION_EMERGENCY_FLEE"
            && verdict.Authority.Kind == ActuationAuthorityKind.Commanded;
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
