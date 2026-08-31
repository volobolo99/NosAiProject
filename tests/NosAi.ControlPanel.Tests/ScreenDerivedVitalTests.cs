using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.ControlPanel.Tests;

public sealed class ScreenDerivedVitalTests
{
    private readonly ScreenDerivedVitalGate _gate = new();

    [Fact]
    public void Honest_read_is_derived_never_live()
    {
        var published = _gate.Publish(412, 800, 0.92, previous: null);
        Assert.Null(published.FailureReason);
        Assert.True(published.Current.HasValue);
        Assert.Equal(412, published.Current.Value);
        Assert.Equal(DataSourceKind.Derived, published.Current.Source);
        Assert.NotEqual(DataSourceKind.Live, published.Current.Source);
    }

    [Fact]
    public void Low_confidence_is_unknown_not_last_value()
    {
        var previous = _gate.Publish(412, 800, 0.95, null);
        var next = _gate.Publish(400, 800, 0.40, previous);
        Assert.False(next.Current.HasValue);
        Assert.Equal("confidence_below_threshold", next.FailureReason);
    }

    [Fact]
    public void Outside_range_is_unknown()
    {
        var next = _gate.Publish(900, 800, 0.95, null);
        Assert.Equal("current_outside_0_max", next.FailureReason);
        Assert.False(next.Current.HasValue);
    }

    [Fact]
    public void Impossible_jump_is_unknown()
    {
        var previous = _gate.Publish(412, 800, 0.95, null);
        var next = _gate.Publish(10, 800, 0.95, previous);
        Assert.Equal("continuity_jump_rejected", next.FailureReason);
    }

    [Fact]
    public void Cached_is_stale_not_current()
    {
        var fresh = _gate.Publish(412, 800, 0.95, null);
        var stale = ScreenDerivedVitalGate.AsCached(fresh);
        Assert.Equal(DataSourceKind.Cached, stale.Current.Source);
        Assert.Equal(412, stale.Current.Value);
        Assert.Equal("stale_not_current", stale.FailureReason);
    }

    [Fact]
    public void Bar_fill_is_derived_never_live()
    {
        var gate = new ScreenDerivedBarGate();
        var published = gate.Publish(0.5, 0.90, previous: null);
        Assert.True(published.Ratio.HasValue);
        Assert.Equal(DataSourceKind.Derived, published.Ratio.Source);
        Assert.NotEqual(DataSourceKind.Live, published.Ratio.Source);
    }

    [Fact]
    public void Empty_bar_region_is_not_published_as_zero()
    {
        var crop = new byte[80 * 8 * 4];
        var measure = HudBarFillReader.Measure(crop, 80, 8, HudFillHue.RedOrGreen);
        Assert.Null(measure.Ratio);
        Assert.Equal("no_bar_signature", measure.FailureReason);
    }
}
