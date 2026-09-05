using NosAi.Core.WorldModel;
using Xunit;
using static NosAi.Core.Tests.WorldModel.WorldModelFixtures;

namespace NosAi.Core.Tests.WorldModel;

public sealed class WorldFactTests
{
    [Fact]
    public void DefaultStructIsUnknownNotLive()
    {
        WorldFact<int> fact = default;

        Assert.Equal(FactSource.Unknown, fact.Source);
        Assert.False(fact.HasValue);
        Assert.Equal(0f, fact.Confidence);
        Assert.Equal(SensorChannel.None, fact.Channel);
        Assert.Equal("uninitialized", fact.Reason);
        Assert.False(fact.IsActionable);
    }

    [Fact]
    public void UnknownRefusesToHandOutAValue()
    {
        var fact = WorldFact<int>.Unknown("no packet", T1);

        Assert.False(fact.HasValue);
        Assert.False(fact.TryGetValue(out var value));
        Assert.Equal(0, value);
        var ex = Assert.Throws<InvalidOperationException>(() => fact.Value);
        Assert.Contains("no packet", ex.Message);
        Assert.Null(fact.AgeAt(T2));
        Assert.Equal(FactFreshness.Unknown, fact.FreshnessAt(T2, 10_000));
    }

    [Fact]
    public void UnknownMustStateAReason()
    {
        Assert.Throws<ArgumentException>(() => WorldFact<int>.Unknown("", T1));
        Assert.Throws<ArgumentException>(() => WorldFact<int>.Unknown("   ", T1));
    }

    [Fact]
    public void UnknownCannotCarryValueChannelOrConfidence()
    {
        Assert.Throws<ArgumentException>(() => new WorldFact<int>(5, FactSource.Unknown, SensorChannel.None, 0f, T1, true, "x"));
        Assert.Throws<ArgumentException>(() => new WorldFact<int>(0, FactSource.Unknown, SensorChannel.Network, 0f, T1, false, "x"));
        Assert.Throws<ArgumentException>(() => new WorldFact<int>(0, FactSource.Unknown, SensorChannel.None, 0.5f, T1, false, "x"));
    }

    [Fact]
    public void KnownFactNeedsValueAndChannel()
    {
        Assert.Throws<ArgumentException>(() => new WorldFact<int>(5, FactSource.Live, SensorChannel.Network, 1f, T1, false, null));
        Assert.Throws<ArgumentException>(() => new WorldFact<int>(5, FactSource.Live, SensorChannel.None, 1f, T1, true, null));
    }

    [Theory]
    [InlineData(-0.0001f)]
    [InlineData(1.0001f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void ConfidenceOutsideUnitIntervalIsRejected(float confidence)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldFact<int>.Live(1, SensorChannel.Network, confidence, T1));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    [InlineData(0.5f)]
    public void ConfidenceBoundariesAreAccepted(float confidence)
    {
        var fact = WorldFact<int>.Live(1, SensorChannel.Network, confidence, T1);
        Assert.Equal(confidence, fact.Confidence);
    }

    [Fact]
    public void NegativeTimestampIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldFact<int>.Live(1, SensorChannel.Network, 1f, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldFact<int>.Unknown("x", -1));
    }

    [Fact]
    public void FreshnessBoundaryIsInclusive()
    {
        var fact = Net(10, at: T0);

        Assert.Equal(FactFreshness.Fresh, fact.FreshnessAt(T0, 0));
        Assert.Equal(FactFreshness.Fresh, fact.FreshnessAt(T0 + 500, 500));
        Assert.Equal(FactFreshness.Stale, fact.FreshnessAt(T0 + 501, 500));
        Assert.Equal(500, fact.AgeAt(T0 + 500));
        Assert.Throws<ArgumentOutOfRangeException>(() => fact.FreshnessAt(T0, -1));
    }

    [Fact]
    public void ClockGoingBackwardsClampsAgeToZero()
    {
        var fact = Net(10, at: T1);

        Assert.Equal(0, fact.AgeAt(T0));
        Assert.Equal(FactFreshness.Fresh, fact.FreshnessAt(T0, 0));
    }

    [Fact]
    public void SimulatedIsNeverActionable()
    {
        var simulated = WorldFact<int>.Simulated(99, 1f, T1, "dry run");

        Assert.True(simulated.HasValue);
        Assert.True(simulated.IsSimulated);
        Assert.False(simulated.IsActionable);
        Assert.Equal(SensorChannel.Local, simulated.Channel);
        Assert.False(FactSource.Simulated.IsActionable());
        Assert.False(FactSource.Unknown.IsActionable());
        Assert.True(FactSource.Live.IsActionable());
        Assert.True(FactSource.Derived.IsActionable());
        Assert.True(FactSource.Cached.IsActionable());
    }

    [Fact]
    public void AsCachedKeepsValueChannelAndTimestamp()
    {
        var live = Net(42, at: T0, confidence: 0.9f);
        var cached = live.AsCached();

        Assert.Equal(FactSource.Cached, cached.Source);
        Assert.Equal(42, cached.Value);
        Assert.Equal(SensorChannel.Network, cached.Channel);
        Assert.Equal(0.9f, cached.Confidence);
        Assert.Equal(T0, cached.ObservedAtUnixMillis);
        Assert.Equal(FactSource.Live, live.Source);

        var unknown = WorldFact<int>.Unknown("x", T0);
        Assert.Equal(unknown, unknown.AsCached());
    }

    [Fact]
    public void WireTextRoundTripsEverySource()
    {
        foreach (var source in Enum.GetValues<FactSource>())
        {
            Assert.True(FactSourceText.TryParseWire(source.ToWire(), out var parsed));
            Assert.Equal(source, parsed);
        }

        Assert.Equal("LIVE", FactSource.Live.ToWire());
        Assert.Equal("DERIVED", FactSource.Derived.ToWire());
        Assert.Equal("CACHED", FactSource.Cached.ToWire());
        Assert.Equal("SIMULATED", FactSource.Simulated.ToWire());
        Assert.Equal("UNKNOWN", FactSource.Unknown.ToWire());
        Assert.False(FactSourceText.TryParseWire("live", out var lower));
        Assert.Equal(FactSource.Unknown, lower);
        Assert.False(FactSourceText.TryParseWire(null, out _));
    }

    [Fact]
    public void ValueEqualityHoldsForIdenticalObservations()
    {
        var a = Net(new Vital(10, 20), at: T1, confidence: 0.75f);
        var b = Net(new Vital(10, 20), at: T1, confidence: 0.75f);
        var c = Net(new Vital(11, 20), at: T1, confidence: 0.75f);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
        Assert.NotEqual(a, a.AsCached());
    }

    [Fact]
    public void ToStringIsInvariantAndShowsUnknownExplicitly()
    {
        var known = WorldFact<float>.Live(0.5f, SensorChannel.Screen, 0.25f, T0);
        var unknown = WorldFact<float>.Unknown("occluded", T0);

        Assert.Equal("LIVE[Screen] 0.5 c=0.25 t=1788602400000", known.ToString());
        Assert.Equal("UNKNOWN[None] ∅ c=0 t=1788602400000 (occluded)", unknown.ToString());
    }

    [Fact]
    public void TypeErasedViewExposesProvenance()
    {
        IWorldFact fact = Net("x", at: T1, confidence: 0.6f);

        Assert.Equal(FactSource.Live, fact.Source);
        Assert.Equal(SensorChannel.Network, fact.Channel);
        Assert.Equal(0.6f, fact.Confidence);
        Assert.Equal(T1, fact.ObservedAtUnixMillis);
        Assert.True(fact.HasValue);
        Assert.Null(fact.Reason);
        Assert.Equal(FactFreshness.Stale, fact.FreshnessAt(T2, 100));
    }
}
