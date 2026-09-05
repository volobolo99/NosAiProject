using NosAi.Core.Progression;

namespace NosAi.Core.Tests.Progression;

public sealed class MissionOutcomeLearningTests
{
    [Fact]
    public async Task Ranker_UsesObservedDurationAndSuccessRate()
    {
        var ledger = new InMemoryOutcomeLedger();
        var now = DateTime.UtcNow;
        await ledger.RecordAsync(new MissionOutcome("a1", "ts", "slow", true, 120, 1, 0, now));
        await ledger.RecordAsync(new MissionOutcome("a2", "ts", "slow", true, 120, 1, 0, now.AddSeconds(1)));
        await ledger.RecordAsync(new MissionOutcome("b1", "ts", "fast", true, 60, 1, 0, now));
        await ledger.RecordAsync(new MissionOutcome("b2", "ts", "fast", false, 60, 1, 1, now.AddSeconds(1)));

        var objective = new MissionObjective("ts", MissionStrategyKind.TimeSpaceOptimization, "TS", "ruleset");
        var candidates = new[]
        {
            Candidate("slow", 100, 0.9),
            Candidate("fast", 100, 0.9)
        };

        var result = await new OutcomeAwareMissionStrategyRanker(
            new DeterministicMissionStrategyOptimizer(), ledger)
            .SelectBestAsync(objective, candidates);

        Assert.NotNull(result);
        Assert.Equal("slow", result!.Candidate.Id);
    }

    [Fact]
    public async Task Ledger_RejectsInvalidMeasurements()
    {
        var ledger = new InMemoryOutcomeLedger();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await ledger.RecordAsync(new MissionOutcome("bad", "ts", "s", true, -1, 0, 0, DateTime.UtcNow)));
    }

    private static MissionStrategyCandidate Candidate(string id, double executionSeconds, double successProbability)
        => new(id, "ts", MissionStrategyKind.TimeSpaceOptimization, id,
            0, 0, executionSeconds, 0, 0, 0, 1, successProbability, 0.8,
            true, true, true, "ruleset");
}
