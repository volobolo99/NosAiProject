using NosAi.Core.WorldModel;
using Xunit;

namespace NosAi.Core.Tests.WorldModel;

public sealed class FactTests
{
    private const long T0 = 1_757_073_600_000; // 2026-09-05T12:00:00Z

    [Fact]
    public void Live_CarriesValueProvenanceConfidenceTimestamp()
    {
        Fact<int> hp = Fact<int>.Live(742, ObservationChannel.Network, 0.95f, T0, sensorId: 7);

        Assert.True(hp.HasValue);
        Assert.True(hp.IsReal);
        Assert.False(hp.IsSimulated);
        Assert.Equal(742, hp.Value);
        Assert.Equal(FactSourceKind.Live, hp.Source);
        Assert.Equal(ObservationChannel.Network, hp.Provenance.Channel);
        Assert.Equal((ushort)7, hp.Provenance.SensorId);
        Assert.Equal(0.95f, hp.Confidence);
        Assert.Equal(T0, hp.ObservedAtUnixMillis);
        Assert.Null(hp.FailureReason);
    }

    [Fact]
    public void Unknown_HasNoValueZeroConfidenceAndReason()
    {
        Fact<int> hp = Fact<int>.Unknown(UnknownReasons.SensorUnavailable, T0);

        Assert.False(hp.HasValue);
        Assert.True(hp.IsUnknown);
        Assert.False(hp.IsReal);
        Assert.Equal(0f, hp.Confidence);
        Assert.Equal(UnknownReasons.SensorUnavailable, hp.FailureReason);
        Assert.False(hp.TryGetValue(out int value));
        Assert.Equal(0, value);
        Assert.Null(hp.AgeAt(T0 + 10));
        Assert.Equal(Freshness.Unknown, hp.FreshnessAt(T0, new FreshnessPolicy(100, 1000)));
    }

    [Fact]
    public void Unknown_WithoutReason_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => Fact<int>.Unknown(""));
        Assert.Throws<ArgumentException>(() => new Fact<int>(1, FactProvenance.Unknown, 0f, T0, null));
    }

    [Fact]
    public void Unknown_WithNonZeroConfidence_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Fact<int>(0, FactProvenance.Unknown, 0.5f, T0, "reason"));
    }

    [Fact]
    public void KnownFact_WithFailureReason_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new Fact<int>(1, FactProvenance.Live(ObservationChannel.Screen), 1f, T0, "reason"));
    }

    [Theory]
    [InlineData(-0.01f)]
    [InlineData(1.01f)]
    [InlineData(float.NaN)]
    public void Confidence_OutsideUnitInterval_IsRejected(float confidence)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Fact<int>.Live(1, ObservationChannel.Memory, confidence, T0));
    }

    [Fact]
    public void Simulated_HasValueButIsNeverActionable()
    {
        Fact<int> hp = Fact<int>.Simulated(500, 1f, T0);

        Assert.True(hp.HasValue);
        Assert.True(hp.IsSimulated);
        Assert.False(hp.IsReal);
        Assert.Equal(ObservationChannel.Unknown, hp.Provenance.Channel);
        Assert.True(hp.IsFresh(T0, 1000));
        Assert.False(hp.IsActionable(T0, 1000));
    }

    [Fact]
    public void Freshness_ClassifiesAgeAgainstPolicy()
    {
        FreshnessPolicy policy = new(200, 2000);
        Fact<int> hp = Fact<int>.Live(1, ObservationChannel.Network, 1f, T0);

        Assert.Equal(Freshness.Fresh, hp.FreshnessAt(T0 + 200, policy));
        Assert.Equal(Freshness.Aging, hp.FreshnessAt(T0 + 201, policy));
        Assert.Equal(Freshness.Aging, hp.FreshnessAt(T0 + 2000, policy));
        Assert.Equal(Freshness.Stale, hp.FreshnessAt(T0 + 2001, policy));
    }

    [Fact]
    public void FutureTimestamp_IsNotFresh()
    {
        Fact<int> hp = Fact<int>.Live(1, ObservationChannel.Network, 1f, T0 + 5000);

        Assert.False(hp.IsFresh(T0, 10_000));
        Assert.Equal(Freshness.Unknown, hp.FreshnessAt(T0, new FreshnessPolicy(200, 2000)));
        Assert.False(hp.IsActionable(T0, 10_000));
    }

    [Fact]
    public void IsActionable_RequiresRealFreshAndConfident()
    {
        Fact<int> live = Fact<int>.Live(1, ObservationChannel.Network, 0.6f, T0);
        Fact<int> cached = Fact<int>.Cached(1, ObservationChannel.Network, 0.6f, T0);
        Fact<int> derived = Fact<int>.Derived(1, ObservationChannel.Screen, 0.6f, T0);

        Assert.True(live.IsActionable(T0 + 100, 200, 0.5f));
        Assert.True(cached.IsActionable(T0 + 100, 200, 0.5f));
        Assert.True(derived.IsActionable(T0 + 100, 200, 0.5f));
        Assert.False(live.IsActionable(T0 + 100, 200, 0.7f));
        Assert.False(live.IsActionable(T0 + 300, 200, 0.5f));
        Assert.False(live.IsActionable(T0 + 100, -1, 0.5f));
    }

    [Fact]
    public void FreshnessPolicy_RejectsInvertedThresholds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FreshnessPolicy(-1, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FreshnessPolicy(100, 50));
    }

    [Fact]
    public void AsCached_PreservesValueTimestampAndChannel_ChangesOnlyKind()
    {
        Fact<int> live = Fact<int>.Live(9, ObservationChannel.Memory, 0.8f, T0, sensorId: 3);
        Fact<int> cached = live.AsCached();

        Assert.Equal(FactSourceKind.Cached, cached.Source);
        Assert.Equal(9, cached.Value);
        Assert.Equal(T0, cached.ObservedAtUnixMillis);
        Assert.Equal(ObservationChannel.Memory, cached.Provenance.Channel);
        Assert.Equal((ushort)3, cached.Provenance.SensorId);
        Assert.Equal(0.8f, cached.Confidence);
    }

    [Fact]
    public void AsCached_IsIdentityForSimulatedAndUnknown()
    {
        Fact<int> simulated = Fact<int>.Simulated(1, 1f, T0);
        Fact<int> unknown = Fact<int>.Unknown("r", T0);

        Assert.Equal(simulated, simulated.AsCached());
        Assert.Equal(unknown, unknown.AsCached());
    }

    [Fact]
    public void WithConfidenceScaled_ScalesKnownAndLeavesUnknown()
    {
        Fact<int> live = Fact<int>.Live(1, ObservationChannel.Screen, 0.8f, T0);
        Fact<int> unknown = Fact<int>.Unknown("r", T0);

        Assert.Equal(0.4f, live.WithConfidenceScaled(0.5f).Confidence, 5);
        Assert.Equal(unknown, unknown.WithConfidenceScaled(0.5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => live.WithConfidenceScaled(1.5f));
    }

    [Fact]
    public void Equality_IgnoresDefaultValueOfUnknownAndComparesKnownValues()
    {
        Fact<int> a = Fact<int>.Live(1, ObservationChannel.Screen, 0.8f, T0);
        Fact<int> b = Fact<int>.Live(1, ObservationChannel.Screen, 0.8f, T0);
        Fact<int> c = Fact<int>.Live(2, ObservationChannel.Screen, 0.8f, T0);
        Fact<int> u1 = Fact<int>.Unknown("r", T0);
        Fact<int> u2 = Fact<int>.Unknown("r", T0);
        Fact<int> u3 = Fact<int>.Unknown("other", T0);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
        Assert.Equal(u1, u2);
        Assert.NotEqual(u1, u3);
        Assert.True(a == b);
        Assert.True(a != c);
    }

    [Fact]
    public void Default_FactIsLiveWithZeroConfidence_NotUnknown()
    {
        // A default(Fact<T>) is a construction error the consumer must guard against;
        // the contract makes it visible: it is not UNKNOWN and carries no reason.
        Fact<int> value = default;

        Assert.Equal(FactSourceKind.Live, value.Source);
        Assert.True(value.HasValue);
        Assert.Equal(0f, value.Confidence);
        Assert.Null(value.FailureReason);
    }

    [Fact]
    public void WireText_RoundTripsAllKinds()
    {
        foreach (FactSourceKind kind in Enum.GetValues<FactSourceKind>())
        {
            Assert.True(FactSourceKindText.TryParseWire(kind.ToWire(), out FactSourceKind parsed));
            Assert.Equal(kind, parsed);
        }

        Assert.False(FactSourceKindText.TryParseWire("live", out FactSourceKind fallback));
        Assert.Equal(FactSourceKind.Unknown, fallback);
    }

    [Fact]
    public void WireText_MatchesRuntimeDataSourceKindOrdinals()
    {
        Assert.Equal(0, (int)FactSourceKind.Live);
        Assert.Equal(1, (int)FactSourceKind.Derived);
        Assert.Equal(2, (int)FactSourceKind.Cached);
        Assert.Equal(3, (int)FactSourceKind.Simulated);
        Assert.Equal(4, (int)FactSourceKind.Unknown);
    }

    [Fact]
    public void ToString_ShowsUnknownReasonOrWireKind()
    {
        Assert.Equal("UNKNOWN(r)", Fact<int>.Unknown("r").ToString());
        Assert.Contains("LIVE/Network", Fact<int>.Live(5, ObservationChannel.Network, 1f, T0).ToString());
    }
}
