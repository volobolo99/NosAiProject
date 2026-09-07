using NosAi.Core.WorldModel;
using Xunit;

namespace NosAi.Core.Tests.WorldModel;

/// <summary>
/// <see cref="Resource.Fraction"/>: the third, independently classified view
/// of a pool, for the sources that state how full it is without stating
/// either bound.
/// </summary>
public sealed class ResourceFractionTests
{
    private static readonly DateTime Older = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Newer = new(2026, 9, 7, 12, 0, 30, DateTimeKind.Utc);

    /// <summary>
    /// The real <c>st</c> capture replayed elsewhere in this repository
    /// (<c>st 3 313816 8 0 66 100 198 52 310 52 0</c>): absolute 198/310 for
    /// monster 313816. The packet's own percentage field says 66, which is
    /// why the decoder ignores it -- 198/310 is 63.87%, not 66%.
    /// </summary>
    [Fact]
    public void BothBoundsObserved_DerivesTheFractionFromThem()
    {
        var hp = new Resource(
            ResourceKind.Health,
            WorldFact<double>.Live(198, 0.9, Older),
            WorldFact<double>.Live(310, 0.9, Older));

        Assert.True(hp.Fraction.HasValue);
        Assert.Equal(DataSourceKind.Derived, hp.Fraction.Source);
        Assert.Equal(198d / 310d, hp.Fraction.Value, precision: 10);
        Assert.Equal("current_over_maximum", hp.Fraction.Reason);
    }

    [Fact]
    public void DerivedFraction_TakesTheLowerConfidenceAndTheOlderInstant()
    {
        var hp = new Resource(
            ResourceKind.Health,
            WorldFact<double>.Live(50, 0.4, Newer),
            WorldFact<double>.Cached(100, 0.95, Older));

        Assert.Equal(0.4, hp.Fraction.Confidence);
        Assert.Equal(Older, hp.Fraction.ObservedAtUtc);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void AnyMissingBound_LeavesTheFractionUnknown(bool currentKnown, bool maximumKnown)
    {
        var hp = new Resource(
            ResourceKind.Health,
            currentKnown ? WorldFact<double>.Live(50, 1.0, Older) : WorldFact<double>.Unknown("hud_not_visible", Older),
            maximumKnown ? WorldFact<double>.Live(100, 1.0, Older) : WorldFact<double>.Unknown("hud_not_visible", Older));

        Assert.False(hp.Fraction.HasValue);
        Assert.Equal("bounds_not_both_observed", hp.Fraction.Reason);
        Assert.Equal(Older, hp.Fraction.ObservedAtUtc);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    public void NonPositiveMaximum_LeavesTheFractionUnknownRatherThanDividing(double maximum)
    {
        var hp = new Resource(
            ResourceKind.Health,
            WorldFact<double>.Live(50, 1.0, Older),
            WorldFact<double>.Live(maximum, 1.0, Older));

        Assert.False(hp.Fraction.HasValue);
        Assert.Equal("maximum_not_positive", hp.Fraction.Reason);
    }

    [Fact]
    public void NonFiniteQuotient_IsRefusedRatherThanCarriedAsAValue()
    {
        var hp = new Resource(
            ResourceKind.Health,
            WorldFact<double>.Live(double.PositiveInfinity, 1.0, Older),
            WorldFact<double>.Live(100, 1.0, Older));

        Assert.False(hp.Fraction.HasValue);
        Assert.Equal("non_finite_fraction", hp.Fraction.Reason);
    }

    /// <summary>
    /// A quotient outside [0, 1] is a disagreement between two observations,
    /// not something this contract silently folds back into range.
    /// </summary>
    [Fact]
    public void QuotientAboveOne_IsReportedAsObservedRatherThanClamped()
    {
        var hp = new Resource(
            ResourceKind.Health,
            WorldFact<double>.Live(120, 1.0, Older),
            WorldFact<double>.Live(100, 1.0, Older));

        Assert.Equal(1.2, hp.Fraction.Value, precision: 10);
    }

    /// <summary>
    /// The <c>in</c> packet case: <c>hp%</c> is the only health the wire
    /// stated, so the fraction is real and both bounds stay Unknown.
    /// </summary>
    [Fact]
    public void FromObservedFraction_KeepsTheFractionRealAndBothBoundsUnknown()
    {
        var hp = Resource.FromObservedFraction(
            ResourceKind.Health,
            WorldFact<double>.Live(0.66, 0.8, Older),
            "wire_states_percentage_only");

        Assert.True(hp.Fraction.HasValue);
        Assert.Equal(DataSourceKind.Live, hp.Fraction.Source);
        Assert.Equal(0.66, hp.Fraction.Value, precision: 10);

        Assert.False(hp.Current.HasValue);
        Assert.False(hp.Maximum.HasValue);
        Assert.Equal("wire_states_percentage_only", hp.Current.Reason);
        Assert.Equal("wire_states_percentage_only", hp.Maximum.Reason);
    }

    /// <summary>
    /// Purity: the Unknown bounds are stamped from the fraction's own
    /// instant, never from the wall clock, so two equal inputs stay equal
    /// (the determinism requirement AP-01 places on replay).
    /// </summary>
    [Fact]
    public void FromObservedFraction_IsDeterministic()
    {
        WorldFact<double> fraction = WorldFact<double>.Live(0.5, 1.0, Older);

        var first = Resource.FromObservedFraction(ResourceKind.Health, fraction, "wire_states_percentage_only");
        var second = Resource.FromObservedFraction(ResourceKind.Health, fraction, "wire_states_percentage_only");

        Assert.Equal(Older, first.Current.ObservedAtUtc);
        Assert.Equal(Older, first.Maximum.ObservedAtUtc);
        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void FromObservedFraction_WithoutAReasonForTheMissingBounds_Throws(string? reason)
    {
        Assert.ThrowsAny<ArgumentException>(() => Resource.FromObservedFraction(
            ResourceKind.Health,
            WorldFact<double>.Live(0.5, 1.0, Older),
            reason!));
    }

    [Fact]
    public void FromObservedFraction_CustomKindStillRequiresAName()
    {
        Assert.ThrowsAny<ArgumentException>(() => Resource.FromObservedFraction(
            ResourceKind.Custom,
            WorldFact<double>.Live(0.5, 1.0, Older),
            "wire_states_percentage_only"));
    }

    [Fact]
    public void NullBound_IsRefusedAtConstruction()
    {
        Assert.Throws<ArgumentNullException>(() => new Resource(
            ResourceKind.Health,
            null!,
            WorldFact<double>.Live(100, 1.0, Older)));
    }

    /// <summary>
    /// Two resources built from equal bounds are equal, fraction included:
    /// nothing in the derivation reaches for the wall clock.
    /// </summary>
    [Fact]
    public void EqualBounds_ProduceEqualResources()
    {
        var first = new Resource(ResourceKind.Mana, WorldFact<double>.Live(30, 1.0, Older), WorldFact<double>.Live(60, 1.0, Older));
        var second = new Resource(ResourceKind.Mana, WorldFact<double>.Live(30, 1.0, Older), WorldFact<double>.Live(60, 1.0, Older));

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }
}
