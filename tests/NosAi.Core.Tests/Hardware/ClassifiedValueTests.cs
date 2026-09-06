using NosAi.Core.Hardware;
using Xunit;

namespace NosAi.Core.Tests.Hardware;

public sealed class ClassifiedValueTests
{
    [Fact]
    public void Unknown_NeverExposesAUsableValue()
    {
        var unknown = ClassifiedValue<long>.Unknown("probe_failed");

        Assert.False(unknown.HasValue);
        Assert.False(unknown.HasObservedValue);
        Assert.Equal(DataSourceKind.Unknown, unknown.Source);
        Assert.Equal("probe_failed", unknown.FailureReason);
    }

    [Fact]
    public void Unknown_UnderlyingValueDefaultsButHasValueMustGateReads()
    {
        // The record still stores default(T) internally (there is nothing
        // else it could store), but HasValue is false, so any caller that
        // checks HasValue before reading Value can never confuse "unknown"
        // with a real, observed zero.
        var unknownLong = ClassifiedValue<long>.Unknown("no_reading");
        var liveZero = ClassifiedValue<long>.Live(0);

        Assert.Equal(0, unknownLong.Value);
        Assert.False(unknownLong.HasValue);

        Assert.Equal(0, liveZero.Value);
        Assert.True(liveZero.HasValue);

        // The two must never be treated as equivalent by a correct caller,
        // even though naive "Value == 0" logic would conflate them. This is
        // precisely why HasValue exists as a mandatory gate.
        Assert.NotEqual(unknownLong.HasValue, liveZero.HasValue);
    }

    [Fact]
    public void Unknown_RequiresAReason()
    {
        var unknown = ClassifiedValue<string>.Unknown("gpu_not_reported");
        Assert.Equal("gpu_not_reported", unknown.FailureReason);
    }

    [Theory]
    [InlineData(DataSourceKind.Live, true)]
    [InlineData(DataSourceKind.Derived, true)]
    [InlineData(DataSourceKind.Cached, true)]
    [InlineData(DataSourceKind.Simulated, false)]
    [InlineData(DataSourceKind.Unknown, false)]
    public void IsTrustedForScheduling_ExcludesSimulatedAndUnknown(DataSourceKind kind, bool expected)
    {
        Assert.Equal(expected, kind.IsTrustedForScheduling());
    }

    [Fact]
    public void Live_Derived_Cached_Simulated_AllProduceAUsableValue()
    {
        var now = DateTime.UtcNow;

        var live = ClassifiedValue<int>.Live(4, now);
        var derived = ClassifiedValue<int>.Derived(8, now);
        var cached = ClassifiedValue<int>.Cached(2, now);
        var simulated = ClassifiedValue<int>.Simulated(1, now);

        Assert.True(live.HasValue);
        Assert.True(derived.HasValue);
        Assert.True(cached.HasValue);
        Assert.True(simulated.HasValue);

        Assert.Equal(DataSourceKind.Live, live.Source);
        Assert.Equal(DataSourceKind.Derived, derived.Source);
        Assert.Equal(DataSourceKind.Cached, cached.Source);
        Assert.Equal(DataSourceKind.Simulated, simulated.Source);
    }

    [Fact]
    public void EqualClassifiedValues_CompareEqualByValueSemantics()
    {
        var now = new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);
        var a = ClassifiedValue<long>.Live(4096, now);
        var b = ClassifiedValue<long>.Live(4096, now);

        Assert.Equal(a, b);
    }
}
