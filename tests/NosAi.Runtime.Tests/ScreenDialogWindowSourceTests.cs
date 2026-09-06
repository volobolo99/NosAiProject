using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.Runtime.Tests;

public sealed class ScreenDialogWindowSourceTests
{
    private static readonly DateTime At = new(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);
    private static readonly PixelRect ClientArea = new(0, 0, 400, 400);

    private sealed class OneFrameSource : IFrameSource
    {
        private readonly CaptureFrame? _frame;
        public OneFrameSource(CaptureFrame? frame) => _frame = frame;
        public DataSourceKind Source => DataSourceKind.Live;

        public bool TryAcquire(out CaptureFrame frame)
        {
            frame = _frame!;
            return _frame is not null;
        }
    }

    private static CaptureFrame FlatFrame(int width, int height, byte b, byte g, byte r)
    {
        var bgra = new byte[width * height * 4];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = b;
            bgra[i + 1] = g;
            bgra[i + 2] = r;
            bgra[i + 3] = 255;
        }

        return new CaptureFrame(width, height, bgra, DataSourceKind.Live, At);
    }

    /// <summary>x=0.25 y=0.25 w=0.25 h=0.05 of a 400x400 client: 100,100 100x20.</summary>
    private static DialogRoiCalibration Calibrated(double baselineB = 40, double baselineG = 40, double baselineR = 40) =>
        DialogRoiCalibration.Confirmed(0.25, 0.25, 0.25, 0.05, 400, 400, At, baselineB, baselineG, baselineR);

    [Fact]
    public void A_region_still_matching_the_baseline_is_absent_with_the_frames_capture_time()
    {
        var source = new ScreenDialogWindowSource(
            new OneFrameSource(FlatFrame(400, 400, 40, 40, 40)),
            Calibrated(),
            () => ClientArea);

        DialogWindowObservation observation = source.Read();

        Assert.Equal(DialogWindowState.Absent, observation.Reading.State);
        Assert.Equal(At, observation.ObservedAtUtc);
    }

    [Fact]
    public void A_region_that_diverges_from_the_baseline_is_present()
    {
        var source = new ScreenDialogWindowSource(
            new OneFrameSource(FlatFrame(400, 400, 240, 240, 240)),
            Calibrated(),
            () => ClientArea);

        Assert.Equal(DialogWindowState.Present, source.Read().Reading.State);
    }

    [Fact]
    public void Without_a_calibration_nothing_is_captured_and_the_reason_is_the_calibration()
    {
        var source = new ScreenDialogWindowSource(
            new OneFrameSource(FlatFrame(400, 400, 240, 240, 240)),
            DialogRoiCalibration.Uncalibrated,
            () => ClientArea);

        DialogWindowObservation observation = source.Read();

        Assert.Equal(DialogWindowState.Unreadable, observation.Reading.State);
        Assert.Equal(DialogRoiCalibration.NotCalibratedReason, observation.Reading.FailureReason);
    }

    [Fact]
    public void A_window_that_cannot_be_located_is_a_refusal_not_a_full_screen_guess()
    {
        var source = new ScreenDialogWindowSource(
            new OneFrameSource(FlatFrame(400, 400, 240, 240, 240)), Calibrated(), () => null);

        DialogWindowObservation observation = source.Read();

        Assert.Equal(DialogWindowState.Unreadable, observation.Reading.State);
        Assert.Equal("client_window_not_located", observation.Reading.FailureReason);
    }

    [Fact]
    public void No_frame_is_a_refusal_with_its_own_reason()
    {
        var source = new ScreenDialogWindowSource(
            new OneFrameSource(null), Calibrated(), () => ClientArea);

        Assert.Equal("no_frame_pixels", source.Read().Reading.FailureReason);
    }

    [Fact]
    public void A_region_off_the_frame_is_refused_rather_than_clamped()
    {
        var source = new ScreenDialogWindowSource(
            new OneFrameSource(FlatFrame(120, 120, 40, 40, 40)),
            Calibrated(),
            () => ClientArea);

        DialogWindowObservation observation = source.Read();

        Assert.Equal(DialogWindowState.Unreadable, observation.Reading.State);
        Assert.Equal("dialog_roi_outside_frame", observation.Reading.FailureReason);
    }
}
