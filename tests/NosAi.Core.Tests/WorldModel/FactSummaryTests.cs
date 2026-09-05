using NosAi.Core.WorldModel;
using Xunit;

namespace NosAi.Core.Tests.WorldModel;

public sealed class FactSummaryTests
{
    private const long T0 = 1_757_073_600_000;

    [Fact]
    public void Empty_HasNoFactsAndIsNotActionable()
    {
        FactSummary summary = FactSummary.Empty;

        Assert.Equal(0, summary.TotalCount);
        Assert.False(summary.HasKnownFacts);
        Assert.Equal(1f, summary.MinConfidence);
        Assert.Null(summary.OldestObservedAtUnixMillis);
        Assert.Null(summary.OldestAgeAt(T0));
        Assert.False(summary.IsActionable(T0, 1000));
    }

    [Fact]
    public void Add_TracksCountsExtremesAndMinimumConfidence()
    {
        FactSummary summary = FactSummary.Empty;
        summary.Add(Fact<int>.Live(1, ObservationChannel.Network, 0.9f, T0 + 50));
        summary.Add(Fact<int>.Cached(2, ObservationChannel.Screen, 0.4f, T0 - 300));
        summary.Add(Fact<int>.Unknown("r"));
        summary.Add(Fact<bool>.Derived(true, ObservationChannel.Memory, 0.7f, T0 + 100));

        Assert.Equal(3, summary.KnownCount);
        Assert.Equal(1, summary.UnknownCount);
        Assert.Equal(1, summary.CachedCount);
        Assert.Equal(0, summary.SimulatedCount);
        Assert.Equal(T0 - 300, summary.OldestObservedAtUnixMillis);
        Assert.Equal(T0 + 100, summary.NewestObservedAtUnixMillis);
        Assert.Equal(0.4f, summary.MinConfidence);
        Assert.Equal(400, summary.OldestAgeAt(T0 + 100));
    }

    [Fact]
    public void OneSimulatedFact_MakesWholeSetNonActionable()
    {
        FactSummary summary = FactSummary.Empty;
        summary.Add(Fact<int>.Live(1, ObservationChannel.Network, 1f, T0));
        summary.Add(Fact<int>.Simulated(2, 1f, T0));

        Assert.True(summary.ContainsSimulated);
        Assert.False(summary.IsActionable(T0, 1000));
    }

    [Fact]
    public void IsActionable_IsBoundByOldestFact()
    {
        FactSummary summary = FactSummary.Empty;
        summary.Add(Fact<int>.Live(1, ObservationChannel.Network, 1f, T0));
        summary.Add(Fact<int>.Live(2, ObservationChannel.Network, 1f, T0 - 900));

        Assert.True(summary.IsActionable(T0, 1000));
        Assert.False(summary.IsActionable(T0 + 200, 1000));
        Assert.False(summary.IsActionable(T0, -1));
    }

    [Fact]
    public void IsActionable_RejectsFutureFactsAndLowConfidence()
    {
        FactSummary future = FactSummary.Empty;
        future.Add(Fact<int>.Live(1, ObservationChannel.Network, 1f, T0 + 10));
        Assert.False(future.IsActionable(T0, 1000));

        FactSummary weak = FactSummary.Empty;
        weak.Add(Fact<int>.Live(1, ObservationChannel.Network, 0.3f, T0));
        Assert.False(weak.IsActionable(T0, 1000, 0.5f));
        Assert.True(weak.IsActionable(T0, 1000, 0.3f));
    }

    [Fact]
    public void Merge_CombinesAsIfAddedIndividually()
    {
        FactSummary left = FactSummary.Empty;
        left.Add(Fact<int>.Live(1, ObservationChannel.Network, 0.9f, T0));
        left.Add(Fact<int>.Unknown("r"));

        FactSummary right = FactSummary.Empty;
        right.Add(Fact<int>.Simulated(1, 0.2f, T0 - 500));
        right.Add(Fact<int>.Cached(1, ObservationChannel.Local, 0.5f, T0 + 700));

        FactSummary merged = left;
        merged.Merge(in right);

        Assert.Equal(3, merged.KnownCount);
        Assert.Equal(1, merged.UnknownCount);
        Assert.Equal(1, merged.SimulatedCount);
        Assert.Equal(1, merged.CachedCount);
        Assert.Equal(0.2f, merged.MinConfidence);
        Assert.Equal(T0 - 500, merged.OldestObservedAtUnixMillis);
        Assert.Equal(T0 + 700, merged.NewestObservedAtUnixMillis);

        FactSummary emptyIntoLeft = left;
        emptyIntoLeft.Merge(FactSummary.Empty);
        Assert.Equal(left.KnownCount, emptyIntoLeft.KnownCount);
        Assert.Equal(left.MinConfidence, emptyIntoLeft.MinConfidence);
    }

    [Fact]
    public void AddAll_SummarizesCarriersInSpan()
    {
        CooldownState[] cooldowns =
        {
            new(new SkillId(1), Fact<long>.Live(T0 + 100, ObservationChannel.Network, 1f, T0)),
            CooldownState.Unknown(new SkillId(2), "r"),
            new(new SkillId(3), Fact<long>.Simulated(T0 + 100, 0.5f, T0 - 10))
        };

        FactSummary summary = FactSummary.Empty;
        summary.AddAll<CooldownState>(cooldowns);

        Assert.Equal(2, summary.KnownCount);
        Assert.Equal(1, summary.UnknownCount);
        Assert.Equal(1, summary.SimulatedCount);
        Assert.Equal(T0 - 10, summary.OldestObservedAtUnixMillis);
    }
}
