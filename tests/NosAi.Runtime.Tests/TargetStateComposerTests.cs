using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// How <c>HasTarget</c> is established, and every case in which it refuses.
/// </summary>
/// <remarks>
/// ADR-0018. The screen establishes the fact because it is the only source that
/// can say <i>no</i> — the wire has <c>ct</c> and <c>su</c> and no observed
/// counterpart that clears a target. The wire enters only as a contradiction, and
/// an uncalibrated ROI refuses before anything is read at all.
/// </remarks>
public sealed class TargetStateComposerTests
{
    private static readonly DateTime ScreenAt = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private static TargetRoiCalibration Calibrated() =>
        TargetRoiCalibration.Confirmed(0.40, 0.06, 0.20, 0.02, 1920, 1080, ScreenAt);

    private static TargetFrameObservation Screen(TargetFrameState state, string? reason = null) =>
        new(new TargetFrameReading(state, HpRatio: null, Confidence: 0.9, reason), ScreenAt);

    // ------------------------------------------------------ the calibration gate

    /// <summary>
    /// The precondition, enforced in code rather than written in a document. An
    /// unaimed reader over empty HUD background reports Absent, which is a
    /// confident wrong answer on every frame — worse than any failure that says so.
    /// </summary>
    [Fact]
    public void Without_a_calibration_nothing_is_read_and_the_reason_says_so()
    {
        ClassifiedValue<bool> result = TargetStateComposer.Compose(
            TargetRoiCalibration.Uncalibrated,
            Screen(TargetFrameState.Present),
            lastPlayerAttackAtUtc: null);

        Assert.False(result.HasValue);
        Assert.Equal(TargetRoiCalibration.NotCalibratedReason, result.FailureReason);
    }

    /// <summary>
    /// Including when the screen says Absent, which is the reading an uncalibrated
    /// region produces and the one that would have been believed.
    /// </summary>
    [Fact]
    public void Without_a_calibration_an_absent_reading_is_not_no_target()
    {
        ClassifiedValue<bool> result = TargetStateComposer.Compose(
            TargetRoiCalibration.Uncalibrated,
            Screen(TargetFrameState.Absent),
            lastPlayerAttackAtUtc: null);

        Assert.False(result.HasValue);
        Assert.Equal(TargetRoiCalibration.NotCalibratedReason, result.FailureReason);
    }

    // ----------------------------------------------- the screen establishes it

    [Fact]
    public void A_present_frame_is_a_target_derived_from_the_screen()
    {
        ClassifiedValue<bool> result = TargetStateComposer.Compose(
            Calibrated(), Screen(TargetFrameState.Present), lastPlayerAttackAtUtc: null);

        Assert.True(result.HasValue);
        Assert.True(result.Value);
        Assert.Equal(DataSourceKind.Derived, result.Source);
        Assert.Equal(ScreenAt, result.ObservedAtUtc);
    }

    /// <summary>The half the wire cannot supply: an observed <i>no</i>.</summary>
    [Fact]
    public void An_absent_frame_is_no_target_derived_from_the_screen()
    {
        ClassifiedValue<bool> result = TargetStateComposer.Compose(
            Calibrated(), Screen(TargetFrameState.Absent), lastPlayerAttackAtUtc: null);

        Assert.True(result.HasValue);
        Assert.False(result.Value);
        Assert.Equal(DataSourceKind.Derived, result.Source);
    }

    /// <summary>
    /// Never false. A false here sends ADR-0016's planner to an exploration
    /// waypoint in the middle of a fight, which is the case that ADR exists to
    /// prevent — and it is why the reader has three states rather than two.
    /// </summary>
    [Fact]
    public void An_unreadable_frame_is_unknown_and_never_no_target()
    {
        ClassifiedValue<bool> result = TargetStateComposer.Compose(
            Calibrated(), Screen(TargetFrameState.Unreadable, "crop_too_small"), null);

        Assert.False(result.HasValue);
        Assert.Equal("crop_too_small", result.FailureReason);
    }

    /// <summary>
    /// The reader's own reason is carried through, because "crop too small" and
    /// "ratio out of range" are different repairs.
    /// </summary>
    [Fact]
    public void An_unreadable_frame_without_a_reason_still_names_one()
    {
        ClassifiedValue<bool> result = TargetStateComposer.Compose(
            Calibrated(), Screen(TargetFrameState.Unreadable), null);

        Assert.False(result.HasValue);
        Assert.Equal(TargetStateComposer.UnreadableReason, result.FailureReason);
    }

    // ------------------------------------------- the wire only contradicts it

    /// <summary>
    /// A hit landed after the screen looked and the screen saw no frame. Neither
    /// source is chosen: the answer is that they do not agree.
    /// </summary>
    [Fact]
    public void A_hit_after_an_absent_reading_is_a_disagreement_not_a_target()
    {
        ClassifiedValue<bool> result = TargetStateComposer.Compose(
            Calibrated(),
            Screen(TargetFrameState.Absent),
            lastPlayerAttackAtUtc: ScreenAt.AddMilliseconds(200));

        Assert.False(result.HasValue);
        Assert.Equal(TargetStateComposer.SourcesDisagreeReason, result.FailureReason);
    }

    /// <summary>
    /// A hit before the reading is history. The target was dropped after that hit,
    /// which is the ordinary course of a fight and not a contradiction.
    /// </summary>
    [Fact]
    public void A_hit_before_an_absent_reading_leaves_the_screens_answer_alone()
    {
        ClassifiedValue<bool> result = TargetStateComposer.Compose(
            Calibrated(),
            Screen(TargetFrameState.Absent),
            lastPlayerAttackAtUtc: ScreenAt.AddMilliseconds(-200));

        Assert.True(result.HasValue);
        Assert.False(result.Value);
    }

    /// <summary>
    /// A hit while the screen sees the frame is agreement. The wire confirms and
    /// does not create: the answer is the screen's either way.
    /// </summary>
    [Fact]
    public void A_hit_with_a_present_reading_is_agreement_and_changes_nothing()
    {
        ClassifiedValue<bool> result = TargetStateComposer.Compose(
            Calibrated(),
            Screen(TargetFrameState.Present),
            lastPlayerAttackAtUtc: ScreenAt.AddMilliseconds(200));

        Assert.True(result.HasValue);
        Assert.True(result.Value);
        Assert.Equal(DataSourceKind.Derived, result.Source);
    }

    /// <summary>
    /// A hit cannot rescue an unreadable frame. The wire never establishes the
    /// fact, so a recent hit against a failed read is still UNKNOWN.
    /// </summary>
    [Fact]
    public void A_hit_does_not_turn_an_unreadable_frame_into_a_target()
    {
        ClassifiedValue<bool> result = TargetStateComposer.Compose(
            Calibrated(),
            Screen(TargetFrameState.Unreadable, "ratio_out_of_range"),
            lastPlayerAttackAtUtc: ScreenAt.AddMilliseconds(200));

        Assert.False(result.HasValue);
        Assert.Equal("ratio_out_of_range", result.FailureReason);
    }

    /// <summary>
    /// A hit at the same instant as the reading is not more recent than it. The
    /// comparison is strict, so the boundary resolves toward the screen's answer
    /// rather than manufacturing a disagreement out of a tie.
    /// </summary>
    [Fact]
    public void A_hit_at_the_same_instant_is_not_more_recent()
    {
        ClassifiedValue<bool> result = TargetStateComposer.Compose(
            Calibrated(), Screen(TargetFrameState.Absent), lastPlayerAttackAtUtc: ScreenAt);

        Assert.True(result.HasValue);
        Assert.False(result.Value);
    }
}
