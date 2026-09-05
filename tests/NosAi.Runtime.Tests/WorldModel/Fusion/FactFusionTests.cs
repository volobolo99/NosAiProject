using NosAi.Core.WorldModel;
using NosAi.Runtime.WorldModel.Fusion;
using Xunit;

namespace NosAi.Runtime.Tests.WorldModel.Fusion;

public sealed class FactFusionTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(5);

    [Fact]
    public void NoCandidates_ResolvesToUnknown_NeverToADefaultValue()
    {
        FusionOutcome<int> outcome = FactFusion.Resolve(Array.Empty<FusionCandidate<int>>(), Now, MaxAge);

        Assert.False(outcome.Result.HasValue);
        Assert.False(outcome.Disagreement);
    }

    [Fact]
    public void OnlyStaleCandidates_ResolvesToUnknown()
    {
        var stale = new FusionCandidate<int>(WorldFact<int>.Live(1, 1.0, Now - TimeSpan.FromSeconds(30)), "network");

        FusionOutcome<int> outcome = FactFusion.Resolve(new[] { stale }, Now, MaxAge);

        Assert.False(outcome.Result.HasValue);
    }

    [Fact]
    public void SingleFreshCandidate_WinsUnchanged()
    {
        WorldFact<int> fact = WorldFact<int>.Live(42, 0.8, Now);
        var candidate = new FusionCandidate<int>(fact, "network");

        FusionOutcome<int> outcome = FactFusion.Resolve(new[] { candidate }, Now, MaxAge);

        Assert.Equal(fact, outcome.Result);
        Assert.False(outcome.Disagreement);
    }

    [Fact]
    public void HigherTrustSource_WinsOverLowerTrustSource_RegardlessOfListOrder()
    {
        var cached = new FusionCandidate<int>(WorldFact<int>.Cached(1, 1.0, Now), "memory");
        var live = new FusionCandidate<int>(WorldFact<int>.Live(2, 1.0, Now), "network");

        FusionOutcome<int> a = FactFusion.Resolve(new[] { cached, live }, Now, MaxAge);
        FusionOutcome<int> b = FactFusion.Resolve(new[] { live, cached }, Now, MaxAge);

        Assert.Equal(2, a.Result.Value);
        Assert.Equal(2, b.Result.Value);
        Assert.True(a.Disagreement);
        Assert.True(b.Disagreement);
    }

    [Fact]
    public void AgreeingCandidates_ReportNoDisagreement()
    {
        var network = new FusionCandidate<int>(WorldFact<int>.Live(7, 1.0, Now), "network");
        var memory = new FusionCandidate<int>(WorldFact<int>.Live(7, 0.9, Now), "memory");

        FusionOutcome<int> outcome = FactFusion.Resolve(new[] { network, memory }, Now, MaxAge);

        Assert.False(outcome.Disagreement);
        Assert.Equal(7, outcome.Result.Value);
    }

    [Fact]
    public void SameTrustAndConfidence_TieBreaksByMostRecentObservation()
    {
        var older = new FusionCandidate<int>(WorldFact<int>.Live(1, 1.0, Now - TimeSpan.FromSeconds(1)), "a");
        var newer = new FusionCandidate<int>(WorldFact<int>.Live(2, 1.0, Now), "b");

        FusionOutcome<int> outcome = FactFusion.Resolve(new[] { older, newer }, Now, MaxAge);

        Assert.Equal(2, outcome.Result.Value);
    }

    [Fact]
    public void FullTie_TieBreaksByChannelNameOrdinally_SoResolutionIsDeterministic()
    {
        var b = new FusionCandidate<int>(WorldFact<int>.Live(1, 1.0, Now), "b-channel");
        var a = new FusionCandidate<int>(WorldFact<int>.Live(2, 1.0, Now), "a-channel");

        FusionOutcome<int> result1 = FactFusion.Resolve(new[] { b, a }, Now, MaxAge);
        FusionOutcome<int> result2 = FactFusion.Resolve(new[] { a, b }, Now, MaxAge);

        Assert.Equal(result1.Result, result2.Result);
        Assert.Equal("a-channel", result1.Result.Value == 2 ? "a-channel" : "b-channel");
    }

    [Fact]
    public void DisagreementDetail_NamesTheLosingChannels()
    {
        var winner = new FusionCandidate<int>(WorldFact<int>.Live(1, 1.0, Now), "network");
        var loser = new FusionCandidate<int>(WorldFact<int>.Cached(2, 1.0, Now), "memory");

        FusionOutcome<int> outcome = FactFusion.Resolve(new[] { winner, loser }, Now, MaxAge);

        Assert.True(outcome.Disagreement);
        Assert.Contains("memory", outcome.DisagreementDetail);
    }

    [Fact]
    public void UnknownCandidates_NeverCompete_EvenWhenTheyAreTheOnlyOnesGiven()
    {
        var unknown = new FusionCandidate<int>(WorldFact<int>.Unknown("no_reader_bound"), "memory");

        FusionOutcome<int> outcome = FactFusion.Resolve(new[] { unknown }, Now, MaxAge);

        Assert.False(outcome.Result.HasValue);
    }
}
