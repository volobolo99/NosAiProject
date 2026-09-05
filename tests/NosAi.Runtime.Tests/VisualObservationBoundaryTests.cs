using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// AP-02/A5 independent audit of <see cref="VisualObservation.Unobserved"/>
/// (docs/agents/phases/AP-02/AP-02_A5_AUDIT.md item 2): checks that no field
/// is "quasi-Unknown" -- a value that looks like a real observation
/// (<c>HasValue == true</c>) purely by construction accident.
/// </summary>
public sealed class VisualObservationBoundaryTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Unobserved_EveryClassifiedField_HasValueIsFalse_NoneSlipsThroughAsAnObservedFalseOrZero()
    {
        VisualObservation observation = VisualObservation.Unobserved("reason", Now);

        Assert.False(observation.Frame.FrameAcquired);
        Assert.Empty(observation.Frame.Regions);
        Assert.Empty(observation.Frame.Entities);
        Assert.Equal(DataSourceKind.Unknown, observation.Frame.Source);

        Assert.False(observation.Vitals.Hp.Current.HasValue);
        Assert.False(observation.Vitals.Hp.Maximum.HasValue);
        Assert.False(observation.Vitals.Mp.Current.HasValue);
        Assert.False(observation.Vitals.Mp.Maximum.HasValue);
        Assert.False(observation.Vitals.HpBar.Ratio.HasValue);
        Assert.False(observation.Vitals.MpBar.Ratio.HasValue);
        Assert.Equal(0.0, observation.Vitals.HpBar.Confidence);
        Assert.Equal(0.0, observation.Vitals.MpBar.Confidence);
        Assert.Equal(0.0, observation.Vitals.Hp.Confidence);
        Assert.Equal(0.0, observation.Vitals.Mp.Confidence);

        Assert.False(observation.HasTarget.HasValue);
    }

    /// <summary>
    /// <see cref="ScreenVitalObservation.HpGlyphs"/>/<c>MpGlyphs</c>/<c>TrainedGlyphs</c>
    /// are plain <see cref="int"/> counts, not <see cref="ClassifiedValue{T}"/>
    /// -- a design already present on the pre-existing (Gate-era)
    /// <see cref="ScreenVitalObservation"/> record itself, not introduced by
    /// AP-02/A1's <see cref="VisualObservation"/> wrapper, and therefore out
    /// of this audit's ownership to change. <see cref="VisualObservation.Unobserved"/>
    /// sets all three to 0 in its fallback.
    /// <para>
    /// Checked whether this is ambiguous with a real reading of "zero
    /// glyphs": for <c>TrainedGlyphs</c> specifically, it is NOT -- the one
    /// existing consumer, <c>NosAi.ControlPanel.PerceptionProbe</c>, already
    /// treats <c>TrainedGlyphs == 0</c> as equivalent to UNKNOWN for display
    /// (<c>observation.TrainedGlyphs == 0 ? "UNKNOWN" : "DERIVED"</c>),
    /// confirming 0 is the established sentinel for "no glyphs trained yet"
    /// in both a real reading and this fallback -- consistent, not a defect.
    /// </para>
    /// <para>
    /// For <c>HpGlyphs</c>/<c>MpGlyphs</c> specifically, no such
    /// zero-is-unknown convention exists at their one real consumer
    /// (<c>PerceptionProbe</c> displays the raw count tagged "DERIVED"
    /// unconditionally) -- so a real read that finds zero glyphs in a HUD
    /// crop and this <c>Unobserved</c> fallback are, in principle,
    /// indistinguishable by value alone. Verified this is not consequential
    /// today: no code in this repository branches on
    /// <c>VisualObservation.Vitals.HpGlyphs</c>/<c>MpGlyphs</c> at all (the
    /// only reader, <c>PerceptionProbe</c>, reads them from a real
    /// <see cref="ScreenVitalReader"/> call, never from
    /// <see cref="VisualObservation.Unobserved"/>). Documented as an
    /// inherited, pre-existing design limitation, not a new AP-02 defect --
    /// any future consumer of these two specific fields should gate on
    /// <c>Vitals.Hp.Current.HasValue</c>/<c>Mp.Current.HasValue</c> first,
    /// exactly as it already must for the numeric HP/MP reading itself.
    /// </para>
    /// </summary>
    [Fact]
    public void Unobserved_GlyphCounts_AreZero_ConsistentWithTheOneRealConsumersOwnZeroMeansUnknownConvention()
    {
        VisualObservation observation = VisualObservation.Unobserved("reason", Now);

        Assert.Equal(0, observation.Vitals.HpGlyphs);
        Assert.Equal(0, observation.Vitals.MpGlyphs);
        Assert.Equal(0, observation.Vitals.TrainedGlyphs);

        // The surrounding classified fields DO correctly say "not observed",
        // so a consumer that gates on them first (as every existing
        // consumer of Vitals does) never mistakes this 0 for a real reading.
        Assert.False(observation.Vitals.Hp.Current.HasValue);
        Assert.False(observation.Vitals.Mp.Current.HasValue);
    }

    [Fact]
    public void Unobserved_PixelRois_AreZeroSized_NotAPlausibleFakeRegion()
    {
        VisualObservation observation = VisualObservation.Unobserved("reason", Now);

        Assert.Equal(new PixelRect(0, 0, 0, 0), observation.Vitals.HpRoi);
        Assert.Equal(new PixelRect(0, 0, 0, 0), observation.Vitals.MpRoi);
    }

    [Fact]
    public void Unobserved_ObservedAtUtc_DefaultsToRealWallClock_WhenNotProvided_ButNeverFabricatesAValue()
    {
        DateTime before = DateTime.UtcNow;
        VisualObservation observation = VisualObservation.Unobserved("reason");
        DateTime after = DateTime.UtcNow;

        Assert.InRange(observation.ObservedAtUtc, before, after);
        Assert.False(observation.Vitals.Hp.Current.HasValue);
    }

    [Fact]
    public void Unobserved_ReasonPropagates_IdenticallyToEveryClassifiedField()
    {
        const string reason = "capture_backend_not_attached";
        VisualObservation observation = VisualObservation.Unobserved(reason, Now);

        Assert.Equal(reason, observation.Frame.UnavailableReason);
        Assert.Equal(reason, observation.Vitals.Hp.FailureReason);
        Assert.Equal(reason, observation.Vitals.Mp.FailureReason);
        Assert.Equal(reason, observation.Vitals.HpBar.FailureReason);
        Assert.Equal(reason, observation.Vitals.MpBar.FailureReason);
        Assert.Equal(reason, observation.Vitals.Hp.Current.FailureReason);
        Assert.Equal(reason, observation.Vitals.Hp.Maximum.FailureReason);
        Assert.Equal(reason, observation.HasTarget.FailureReason);
    }
}
