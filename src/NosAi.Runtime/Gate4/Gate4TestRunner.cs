using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

namespace NosAi.Runtime.Gate4;

public static class Gate4TestRunner
{
    public static Task<bool> RunAllTestsAsync()
    {
        bool allPassed = true;
        allPassed &= Run("DAG prerequisites", TestQuestDagPrerequisites);
        allPassed &= Run("Beta-Binomial convergence", TestBetaBinomialBayesianConvergence);
        allPassed &= Run("UCB1 strategy selection", TestUcb1StrategySelection);
        allPassed &= Run("Strategy mastery lifecycle", TestStrategyMasteryLifecycle);
        allPassed &= Run("SP1 -> SP2 progression pipeline", TestSpecialistCardUnlockPipeline);
        allPassed &= Run("Deterministic pure evaluation", TestDeterministicPureEvaluation);
        Console.WriteLine(allPassed
            ? ">> Gate 4 test suite: PASS"
            : ">> Gate 4 test suite: FAIL");
        return Task.FromResult(allPassed);
    }

    private static bool Run(string name, Func<bool> test)
    {
        try
        {
            bool result = test();
            Console.WriteLine($"[{(result ? "PASS" : "FAIL")}] {name}");
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] {name}: {ex.Message}");
            return false;
        }
    }

    private static CharacterProgressionProfile CreateProfile(
        int combatLevel = 1,
        int jobLevel = 1,
        long gold = 1000,
        ImmutableHashSet<string>? completed = null) =>
        new(1, "TestHero", combatLevel, jobLevel, 1, 1,
            new ResourceInventory(gold, 20, 10, 5, 10, 20, 10),
            ImmutableHashSet<SpecialistCardType>.Empty,
            completed ?? ImmutableHashSet<string>.Empty);

    private static bool TestQuestDagPrerequisites()
    {
        var engine = new ProgressionEngineV2(new KnowledgeBaseManager());
        var initial = engine.GetAvailableQuests(CreateProfile());
        if (initial.Count != 1 || initial[0].QuestId != "ACT1_Q1_NOSVILLE_START") return false;

        var highLevel = engine.GetAvailableQuests(CreateProfile(40, 25, 50000));
        return highLevel.All(q => q.QuestId != "SP1_QUEST_UNLOCK");
    }

    private static bool TestBetaBinomialBayesianConvergence()
    {
        var prior = BetaBinomialEvidence.CreateUniformPrior();
        var posterior = prior;
        for (int i = 0; i < 10; i++) posterior = posterior.RecordTrial(true);
        return Math.Abs(posterior.ExpectedSuccessRate - (11.0 / 12.0)) < 0.001
            && posterior.Variance < prior.Variance
            && posterior.ConfidenceScore > 0.75f;
    }

    private static bool TestUcb1StrategySelection()
    {
        var selector = new Ucb1StrategySelector();
        var profile = CreateProfile(40, 20, 100000);
        var evidence = BetaBinomialEvidence.CreateUniformPrior();
        for (int i = 0; i < 9; i++) evidence = evidence.RecordTrial(true);
        evidence = evidence.RecordTrial(false);
        var a = new StrategyRecord(Guid.NewGuid(), "A", GoalType.MainQuest, "Q1", evidence,
            StrategyLifecycleStatus.Verified, 120000, 100, 0.85f, DateTime.UtcNow);
        var b = new StrategyRecord(Guid.NewGuid(), "B", GoalType.MainQuest, "Q1",
            BetaBinomialEvidence.CreateUniformPrior(), StrategyLifecycleStatus.Candidate,
            110000, 100, 0.50f, DateTime.UtcNow);
        return selector.SelectBestStrategy(new[] { a, b }, 10, profile) is not null;
    }

    private static bool TestStrategyMasteryLifecycle()
    {
        var kb = new KnowledgeBaseManager();
        var strategy = new StrategyRecord(Guid.NewGuid(), "TS_12_Optimal_Route", GoalType.TimeSpace,
            "ACT1_Q2_TS_12", BetaBinomialEvidence.CreateUniformPrior(), StrategyLifecycleStatus.Candidate,
            400000, 500, 0.5f, DateTime.UtcNow);
        kb.RegisterStrategy(strategy);
        StrategyRecord updated = strategy;
        for (int i = 0; i < 10; i++) updated = kb.UpdateStrategyEvidence(strategy.StrategyId, true);
        return updated.Status == StrategyLifecycleStatus.Mastered && updated.MasteryScore >= 0.90f;
    }

    private static bool TestSpecialistCardUnlockPipeline()
    {
        var engine = new ProgressionEngineV2(new KnowledgeBaseManager());
        var completed = ImmutableHashSet.Create("ACT1_Q1_NOSVILLE_START", "ACT1_Q2_TS_12");
        var profile = CreateProfile(38, 22, 50000, completed);
        var (quest, _) = engine.PlanNextProgressionStep(profile);
        return quest.QuestId == "SP1_QUEST_UNLOCK"
            && quest.UnlocksSpecialist == SpecialistCardType.SP1_Warrior_Ranger_RedMage;
    }

    private static bool TestDeterministicPureEvaluation()
    {
        var types = typeof(ProgressionEngineV2).Assembly.GetTypes()
            .Where(t => t.Namespace?.Contains("NosAi.Runtime.Gate4", StringComparison.Ordinal) == true);
        return types.All(t => !t.Name.Contains("DirectX", StringComparison.OrdinalIgnoreCase)
            && !t.Name.Contains("AntiCheat", StringComparison.OrdinalIgnoreCase)
            && !t.Name.Contains("MemoryHook", StringComparison.OrdinalIgnoreCase));
    }
}
