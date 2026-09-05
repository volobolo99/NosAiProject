using NosAi.Core.Progression;

namespace NosAi.Core.Tests.Progression;

public sealed class MissionStrategyOptimizerTests
{
    [Fact]
    public void SelectBest_PrefersLowerExpectedTimeWhenOtherFactorsMatch()
    {
        var objective = new MissionObjective("ts-001", MissionStrategyKind.TimeSpace, "Fast TS", "private-test-2026.09");
        var candidates = new[]
        {
            Candidate("slow", 120, 0.95),
            Candidate("fast", 60, 0.95)
        };

        var result = new DeterministicMissionStrategyOptimizer().SelectBest(objective, candidates);

        Assert.NotNull(result);
        Assert.Equal("fast", result!.Candidate.Id);
    }

    [Fact]
    public void SelectBest_RejectsUnsafeOrIncompatibleCandidates()
    {
        var objective = new MissionObjective("sp-001", MissionStrategyKind.SpecialistMission, "SP", "ruleset-a");
        var candidates = new[]
        {
            Candidate("privileged", 10, 1, humanPlausible: true, permittedObservation: false),
            Candidate("wrong-ruleset", 5, 1, ruleset: "ruleset-b"),
            Candidate("valid", 30, 0.8, ruleset: "ruleset-a")
        };

        var result = new DeterministicMissionStrategyOptimizer().SelectBest(objective, candidates);

        Assert.NotNull(result);
        Assert.Equal("valid", result!.Candidate.Id);
    }

    [Fact]
    public async Task OutcomeLedger_ReplacesDuplicateAndComputesSummary()
    {
        var ledger = new InMemoryOutcomeLedger();
        var now = DateTime.UtcNow;
        await ledger.RecordAsync(new MissionOutcome("o1", "ts", "fast", false, 100, 5, 2, now));
        await ledger.RecordAsync(new MissionOutcome("o1", "ts", "fast", true, 80, 3, 1, now.AddSeconds(1)));
        await ledger.RecordAsync(new MissionOutcome("o2", "ts", "fast", true, 60, 2, 1, now.AddSeconds(2)));

        var summary = await ledger.SummarizeAsync("ts", "fast");

        Assert.NotNull(summary);
        Assert.Equal(2, summary!.Samples);
        Assert.Equal(2, summary.Successes);
        Assert.Equal(1d, summary.SuccessRate);
        Assert.Equal(70d, summary.MeanDurationSeconds);
    }

    private static MissionStrategyCandidate Candidate(
        string id,
        double executionSeconds,
        double successProbability,
        bool humanPlausible = true,
        bool permittedObservation = true,
        string ruleset = "private-test-2026.09")
        => new(id, "ts-001", MissionStrategyKind.TimeSpaceOptimization, id,
            5, 5, executionSeconds, 0, 0, 0, 0, successProbability, 0.9,
            true, humanPlausible, permittedObservation, ruleset);
}
