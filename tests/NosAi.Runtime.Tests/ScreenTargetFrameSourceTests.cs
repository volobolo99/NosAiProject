using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The screen half of ADR-0018: every refusal is named, and none falls back to a
/// region nobody aimed.
/// </summary>
public sealed class ScreenTargetFrameSourceTests
{
    private static readonly DateTime At = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
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

    /// <summary>A frame with a solid bar across the whole calibrated region.</summary>
    private static CaptureFrame FrameWithBar(int width, int height, PixelRect bar)
    {
        var bgra = new byte[width * height * 4];
        for (int y = bar.Y; y < bar.Y + bar.Height; y++)
        {
            for (int x = bar.X; x < bar.X + bar.Width; x++)
            {
                int i = (y * width + x) * 4;
                bgra[i] = 0;
                bgra[i + 1] = 200;
                bgra[i + 2] = 40;
                bgra[i + 3] = 255;
            }
        }

        return new CaptureFrame(width, height, bgra, DataSourceKind.Live, At);
    }

    private static CaptureFrame BlankFrame(int width, int height)
        => new(width, height, new byte[width * height * 4], DataSourceKind.Live, At);

    /// <summary>x=0.25 y=0.25 w=0.25 h=0.05 of a 400x400 client: 100,100 100x20.</summary>
    private static TargetRoiCalibration Calibrated() =>
        TargetRoiCalibration.Confirmed(0.25, 0.25, 0.25, 0.05, 400, 400, At);

    private static readonly PixelRect CalibratedRoi = new(100, 100, 100, 20);

    [Fact]
    public void A_bar_in_the_calibrated_region_is_present_with_the_frames_capture_time()
    {
        var source = new ScreenTargetFrameSource(
            new OneFrameSource(FrameWithBar(400, 400, CalibratedRoi)),
            Calibrated(),
            () => ClientArea);

        TargetFrameObservation observation = source.Read();

        Assert.Equal(TargetFrameState.Present, observation.Reading.State);
        // The capture time, not the read time: the composer decides the wire's
        // contribution by which source is more recent.
        Assert.Equal(At, observation.ObservedAtUtc);
    }

    [Fact]
    public void An_empty_calibrated_region_is_absent()
    {
        var source = new ScreenTargetFrameSource(
            new OneFrameSource(BlankFrame(400, 400)), Calibrated(), () => ClientArea);

        Assert.Equal(TargetFrameState.Absent, source.Read().Reading.State);
    }

    /// <summary>
    /// Reported before the capture is even attempted, so an uncalibrated runtime
    /// says so rather than reporting whatever the capture happened to do.
    /// </summary>
    [Fact]
    public void Without_a_calibration_nothing_is_captured_and_the_reason_is_the_calibration()
    {
        var source = new ScreenTargetFrameSource(
            new OneFrameSource(FrameWithBar(400, 400, CalibratedRoi)),
            TargetRoiCalibration.Uncalibrated,
            () => ClientArea);

        TargetFrameObservation observation = source.Read();

        Assert.Equal(TargetFrameState.Unreadable, observation.Reading.State);
        Assert.Equal(TargetRoiCalibration.NotCalibratedReason, observation.Reading.FailureReason);
    }

    /// <summary>
    /// Without the client area the region would be a fraction of the whole
    /// desktop, which is the mistake T-03 made — the reader measured the editor
    /// behind the game.
    /// </summary>
    [Fact]
    public void A_window_that_cannot_be_located_is_a_refusal_not_a_full_screen_guess()
    {
        var source = new ScreenTargetFrameSource(
            new OneFrameSource(FrameWithBar(400, 400, CalibratedRoi)), Calibrated(), () => null);

        TargetFrameObservation observation = source.Read();

        Assert.Equal(TargetFrameState.Unreadable, observation.Reading.State);
        Assert.Equal("client_window_not_located", observation.Reading.FailureReason);
    }

    [Fact]
    public void No_frame_is_a_refusal_with_its_own_reason()
    {
        var source = new ScreenTargetFrameSource(
            new OneFrameSource(null), Calibrated(), () => ClientArea);

        Assert.Equal("no_frame_pixels", source.Read().Reading.FailureReason);
    }

    /// <summary>
    /// A clamped region is a different region from the calibrated one, so the
    /// reading would be of pixels nobody confirmed.
    /// </summary>
    [Fact]
    public void A_region_off_the_frame_is_refused_rather_than_clamped()
    {
        var source = new ScreenTargetFrameSource(
            new OneFrameSource(BlankFrame(120, 120)),
            Calibrated(),
            () => ClientArea);

        TargetFrameObservation observation = source.Read();

        Assert.Equal(TargetFrameState.Unreadable, observation.Reading.State);
        Assert.Equal("target_roi_outside_frame", observation.Reading.FailureReason);
    }
}
