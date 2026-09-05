using NosAi.Core.WorldModel;
using Xunit;

namespace NosAi.Core.Tests.WorldModel;

public sealed class WorldFactTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Unknown_HasNoValue_AndCarriesReason()
    {
        WorldFact<int> fact = WorldFact<int>.Unknown("no_observation_channel_available", Now);

        Assert.False(fact.HasValue);
        Assert.Equal(DataSourceKind.Unknown, fact.Source);
        Assert.Equal("no_observation_channel_available", fact.Reason);
        Assert.Equal(0d, fact.Confidence);
    }

    [Fact]
    public void Unknown_NeverCollapsesIntoDefaultAsIfObserved()
    {
        // The invariant under test: reading .Value on an Unknown int fact
        // returns default(int) == 0, but HasValue is false -- a caller that
        // checks HasValue first can never mistake "unknown" for "observed
        // zero" (docs/ROADMAP_ESECUTIVA.md: "Unknown is not zero, false or empty").
        WorldFact<int> fact = WorldFact<int>.Unknown("reason");

        Assert.Equal(0, fact.Value);
        Assert.False(fact.HasValue);
    }

    [Theory]
    [InlineData(DataSourceKind.Live)]
    [InlineData(DataSourceKind.Derived)]
    [InlineData(DataSourceKind.Cached)]
    [InlineData(DataSourceKind.Simulated)]
    public void ObservedFactories_ProduceHasValueTrue_WithMatchingSource(DataSourceKind expectedSource)
    {
        WorldFact<int> fact = expectedSource switch
        {
            DataSourceKind.Live => WorldFact<int>.Live(42, 0.9, Now),
            DataSourceKind.Derived => WorldFact<int>.Derived(42, 0.9, Now),
            DataSourceKind.Cached => WorldFact<int>.Cached(42, 0.9, Now),
            DataSourceKind.Simulated => WorldFact<int>.Simulated(42, 0.9, Now),
            _ => throw new ArgumentOutOfRangeException(nameof(expectedSource))
        };

        Assert.True(fact.HasValue);
        Assert.Equal(expectedSource, fact.Source);
        Assert.Equal(42, fact.Value);
    }

    [Theory]
    [InlineData(-1.0, 0.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(0.5, 0.5)]
    [InlineData(1.0, 1.0)]
    [InlineData(2.5, 1.0)]
    public void Confidence_IsAlwaysClampedToZeroOneRange(double input, double expected)
    {
        WorldFact<int> fact = WorldFact<int>.Live(1, input, Now);
        Assert.Equal(expected, fact.Confidence);
    }

    [Fact]
    public void IsFresh_TrueWithinMaxAge_FalseBeyondIt()
    {
        WorldFact<int> fact = WorldFact<int>.Live(1, 1.0, Now);

        Assert.True(fact.IsFresh(TimeSpan.FromSeconds(5), Now + TimeSpan.FromSeconds(5)));
        Assert.False(fact.IsFresh(TimeSpan.FromSeconds(5), Now + TimeSpan.FromSeconds(5.001)));
    }

    [Fact]
    public void IsFresh_IsAlwaysFalseForAnUnknownFact()
    {
        WorldFact<int> fact = WorldFact<int>.Unknown("reason", Now);

        Assert.False(fact.IsFresh(TimeSpan.MaxValue, Now));
    }

    [Fact]
    public void TwoFactsBuiltFromTheSameInputs_AreEqual()
    {
        WorldFact<int> a = WorldFact<int>.Derived(7, 0.8, Now, "fused");
        WorldFact<int> b = WorldFact<int>.Derived(7, 0.8, Now, "fused");

        Assert.Equal(a, b);
    }
}
