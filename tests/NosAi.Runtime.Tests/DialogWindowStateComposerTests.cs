using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.Runtime.Tests;

public sealed class DialogWindowStateComposerTests
{
    private static readonly DateTime At = new(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);

    private static DialogRoiCalibration Calibrated() =>
        DialogRoiCalibration.Confirmed(0.3, 0.4, 0.4, 0.3, 1920, 1080, At, 40, 40, 40);

    [Fact]
    public void An_uncalibrated_roi_reports_the_calibration_reason_before_inspecting_the_reading()
    {
        var observation = new DialogWindowObservation(
            new DialogWindowReading(DialogWindowState.Present, 500, null), At);

        var result = DialogWindowStateComposer.Compose(DialogRoiCalibration.Uncalibrated, observation);

        Assert.False(result.HasValue);
        Assert.Equal(DialogRoiCalibration.NotCalibratedReason, result.FailureReason);
    }

    [Fact]
    public void A_present_reading_composes_to_a_derived_true()
    {
        var observation = new DialogWindowObservation(
            new DialogWindowReading(DialogWindowState.Present, 500, null), At);

        var result = DialogWindowStateComposer.Compose(Calibrated(), observation);

        Assert.True(result.HasValue);
        Assert.True(result.Value);
        Assert.Equal(At, result.ObservedAtUtc);
    }

    [Fact]
    public void An_absent_reading_composes_to_a_derived_false()
    {
        var observation = new DialogWindowObservation(
            new DialogWindowReading(DialogWindowState.Absent, 2, null), At);

        var result = DialogWindowStateComposer.Compose(Calibrated(), observation);

        Assert.True(result.HasValue);
        Assert.False(result.Value);
    }

    [Fact]
    public void An_unreadable_reading_composes_to_unknown_with_its_own_reason()
    {
        var observation = new DialogWindowObservation(
            new DialogWindowReading(DialogWindowState.Unreadable, null, "crop_truncated"), At);

        var result = DialogWindowStateComposer.Compose(Calibrated(), observation);

        Assert.False(result.HasValue);
        Assert.Equal("crop_truncated", result.FailureReason);
    }

    [Fact]
    public void An_unreadable_reading_without_a_named_reason_falls_back_to_the_composers_own()
    {
        var observation = new DialogWindowObservation(
            new DialogWindowReading(DialogWindowState.Unreadable, null, null), At);

        var result = DialogWindowStateComposer.Compose(Calibrated(), observation);

        Assert.False(result.HasValue);
        Assert.Equal(DialogWindowStateComposer.UnreadableReason, result.FailureReason);
    }
}
