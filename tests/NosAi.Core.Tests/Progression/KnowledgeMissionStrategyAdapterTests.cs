using NosAi.Core.Knowledge;
using NosAi.Core.Progression;
using Xunit;

namespace NosAi.Core.Tests.Progression;

public sealed class KnowledgeMissionStrategyAdapterTests
{
    [Fact]
    public async Task GetCandidatesAsync_RequiresExplicitClientSafeExecutionEvidence()
    {
        var memory = new StubStrategyMemory(new StrategyMemoryItem(
            "community-ts-1",
            "ts.fast",
            "ts.fast",
            "Fast route",
            0.9,
            0.8,
            KnowledgeLifecycle.Verified,
            "ruleset-2026",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["strategy_kind"] = nameof(MissionStrategyKind.TimeSpaceOptimization),
                ["human_plausible"] = "true",
                ["ordinary_client_only"] = "true",
                ["permitted_observation"] = "true",
                ["preconditions_satisfied"] = "true",
                ["execution_seconds"] = "60",
                ["success_probability"] = "0.9"
            }));

        var adapter = new KnowledgeMissionStrategyAdapter(memory);
        var objective = new MissionObjective("ts.fast", MissionStrategyKind.TimeSpace, "Fast TS", "ruleset-2026");

        var candidates = await adapter.GetCandidatesAsync(objective);

        var candidate = Assert.Single(candidates);
        Assert.True(candidate.IsExecutable);
        Assert.Equal(MissionStrategyKind.TimeSpaceOptimization, candidate.Kind);
        Assert.Equal(60, candidate.EstimatedExecutionSeconds);
    }

    [Fact]
    public async Task GetCandidatesAsync_DoesNotMakeUnprovenKnowledgeExecutable()
    {
        var memory = new StubStrategyMemory(new StrategyMemoryItem(
            "community-ts-unsafe",
            "ts.fast",
            "ts.fast",
            "Unproven route",
            0.9,
            0.8,
            KnowledgeLifecycle.Validated,
            "ruleset-2026",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["strategy_kind"] = nameof(MissionStrategyKind.TimeSpaceOptimization)
            }));

        var adapter = new KnowledgeMissionStrategyAdapter(memory);
        var objective = new MissionObjective("ts.fast", MissionStrategyKind.TimeSpace, "Fast TS", "ruleset-2026");

        var candidates = await adapter.GetCandidatesAsync(objective);

        var candidate = Assert.Single(candidates);
        Assert.False(candidate.IsExecutable);
        Assert.False(candidate.HumanPlausible);
        Assert.False(candidate.UsesOnlyPermittedObservation);
        Assert.False(candidate.PreconditionsSatisfied);
    }

    private sealed class StubStrategyMemory : IStrategyMemory
    {
        private readonly IReadOnlyList<StrategyMemoryItem> _items;

        public StubStrategyMemory(params StrategyMemoryItem[] items) => _items = items;

        public ValueTask<IReadOnlyList<StrategyMemoryItem>> QueryAsync(
            string objective,
            string? rulesetVersion = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_items);
        }
    }
}
