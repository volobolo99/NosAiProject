using NosAi.Core.Scheduling;
using Xunit;
using NosAi.Core.Hardware;

namespace NosAi.Core.Tests.Scheduling;

public sealed class LowestCostJobSelectorTests
{
    private const long Now = 10_000;

    private static InferenceJob Candidate(
        string id,
        InferenceTier tier,
        ResourceCost cost,
        long deadline = Now + 500,
        long durationMs = 10,
        double minConfidence = 0.5,
        JobPriority priority = JobPriority.Normal) =>
        new(id, tier, priority, deadline, durationMs, cost, minConfidence, FallbackStrategy.Reject());

    [Fact]
    public void SelectsTheLowestTierAmongQualifyingCandidates()
    {
        var cheap = Candidate("tier1", InferenceTier.Tier1LightweightLocalMl, new ResourceCost(1, 0, 1, 0));
        var expensive = Candidate("tier2", InferenceTier.Tier2GpuAcceleratedVision, new ResourceCost(1, 1, 1, 1));

        SelectionResult result = LowestCostJobSelector.SelectCheapest(
            new ResourceCost(100, 100, 100, 100), new[] { expensive, cheap }, requiredConfidence: 0.4, Now);

        Assert.True(result.Selected);
        Assert.Equal("tier1", result.Job!.Id);
    }

    [Fact]
    public void WithinTheSameTierSelectsLowerTotalResourceCost()
    {
        var costly = Candidate("costly", InferenceTier.Tier2GpuAcceleratedVision, new ResourceCost(50, 50, 0, 0));
        var lean = Candidate("lean", InferenceTier.Tier2GpuAcceleratedVision, new ResourceCost(5, 5, 0, 0));

        SelectionResult result = LowestCostJobSelector.SelectCheapest(
            new ResourceCost(100, 100, 100, 100), new[] { costly, lean }, requiredConfidence: 0.4, Now);

        Assert.Equal("lean", result.Job!.Id);
    }

    [Fact]
    public void CandidatesMissingTheDeadlineAreExcluded()
    {
        var tooSlow = Candidate("too-slow", InferenceTier.Tier1LightweightLocalMl, new ResourceCost(1, 0, 0, 0), deadline: Now + 5, durationMs: 50);
        var onTime = Candidate("on-time", InferenceTier.Tier2GpuAcceleratedVision, new ResourceCost(1, 1, 0, 0), deadline: Now + 500, durationMs: 10);

        SelectionResult result = LowestCostJobSelector.SelectCheapest(
            new ResourceCost(100, 100, 100, 100), new[] { tooSlow, onTime }, requiredConfidence: 0.4, Now);

        Assert.True(result.Selected);
        Assert.Equal("on-time", result.Job!.Id);
    }

    [Fact]
    public void CandidatesBelowRequiredConfidenceAreExcluded()
    {
        var lowConfidence = Candidate("low-conf", InferenceTier.Tier0DeterministicRules, new ResourceCost(0, 0, 0, 0), minConfidence: 0.2);
        var highConfidence = Candidate("high-conf", InferenceTier.Tier2GpuAcceleratedVision, new ResourceCost(1, 1, 0, 0), minConfidence: 0.9);

        SelectionResult result = LowestCostJobSelector.SelectCheapest(
            new ResourceCost(100, 100, 100, 100), new[] { lowConfidence, highConfidence }, requiredConfidence: 0.8, Now);

        Assert.True(result.Selected);
        Assert.Equal("high-conf", result.Job!.Id);
    }

    [Fact]
    public void CandidatesExceedingBudgetAreExcluded()
    {
        var tooExpensive = Candidate("too-expensive", InferenceTier.Tier0DeterministicRules, new ResourceCost(1000, 0, 0, 0));
        var affordable = Candidate("affordable", InferenceTier.Tier1LightweightLocalMl, new ResourceCost(5, 0, 0, 0));

        SelectionResult result = LowestCostJobSelector.SelectCheapest(
            new ResourceCost(10, 10, 10, 10), new[] { tooExpensive, affordable }, requiredConfidence: 0.4, Now);

        Assert.Equal("affordable", result.Job!.Id);
    }

    [Fact]
    public void NoQualifyingCandidateReturnsExplicitNonSelectionNotANullTreatedAsFine()
    {
        var impossible = Candidate("impossible", InferenceTier.Tier3ExpensiveLocalReasoning, new ResourceCost(1000, 1000, 1000, 1000), minConfidence: 0.99);

        SelectionResult result = LowestCostJobSelector.SelectCheapest(
            new ResourceCost(1, 1, 1, 1), new[] { impossible }, requiredConfidence: 0.5, Now);

        Assert.False(result.Selected);
        Assert.Equal(SelectionOutcome.NoCandidateSatisfiesRequirements, result.Outcome);
        Assert.Null(result.Job);
        Assert.False(string.IsNullOrWhiteSpace(result.Explanation));
    }

    [Fact]
    public void EqualCostTiesFavorHigherPriority()
    {
        var low = Candidate("low", InferenceTier.Tier1LightweightLocalMl, new ResourceCost(1, 1, 0, 0), priority: JobPriority.Low);
        var critical = Candidate("critical", InferenceTier.Tier1LightweightLocalMl, new ResourceCost(1, 1, 0, 0), priority: JobPriority.Critical);

        SelectionResult result = LowestCostJobSelector.SelectCheapest(
            new ResourceCost(100, 100, 100, 100), new[] { low, critical }, requiredConfidence: 0.4, Now);

        Assert.Equal("critical", result.Job!.Id);
    }

    [Fact]
    public void SelectionIsDeterministicRegardlessOfInputOrder()
    {
        var a = Candidate("a", InferenceTier.Tier1LightweightLocalMl, new ResourceCost(3, 0, 0, 0));
        var b = Candidate("b", InferenceTier.Tier1LightweightLocalMl, new ResourceCost(1, 0, 0, 0));
        var c = Candidate("c", InferenceTier.Tier2GpuAcceleratedVision, new ResourceCost(0, 0, 0, 0));

        var budget = new ResourceCost(100, 100, 100, 100);
        SelectionResult forward = LowestCostJobSelector.SelectCheapest(budget, new[] { a, b, c }, 0.4, Now);
        SelectionResult reversed = LowestCostJobSelector.SelectCheapest(budget, new[] { c, b, a }, 0.4, Now);
        SelectionResult shuffled = LowestCostJobSelector.SelectCheapest(budget, new[] { b, c, a }, 0.4, Now);

        Assert.Equal(forward.Job!.Id, reversed.Job!.Id);
        Assert.Equal(forward.Job!.Id, shuffled.Job!.Id);
        Assert.Equal("b", forward.Job!.Id);
    }

    [Fact]
    public void RepeatedCallsWithIdenticalInputsProduceIdenticalOutput()
    {
        var a = Candidate("a", InferenceTier.Tier1LightweightLocalMl, new ResourceCost(3, 0, 0, 0));
        var b = Candidate("b", InferenceTier.Tier1LightweightLocalMl, new ResourceCost(1, 0, 0, 0));
        var budget = new ResourceCost(100, 100, 100, 100);
        var candidates = new[] { a, b };

        SelectionResult first = LowestCostJobSelector.SelectCheapest(budget, candidates, 0.4, Now);
        SelectionResult second = LowestCostJobSelector.SelectCheapest(budget, candidates, 0.4, Now);

        Assert.Equal(first.Outcome, second.Outcome);
        Assert.Equal(first.Job!.Id, second.Job!.Id);
    }

    [Fact]
    public void RequiredConfidenceOutsideUnitIntervalThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LowestCostJobSelector.SelectCheapest(ResourceCost.Zero, Array.Empty<InferenceJob>(), 1.5, Now));
    }
}
