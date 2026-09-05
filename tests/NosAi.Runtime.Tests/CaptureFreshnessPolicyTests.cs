using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.Runtime.Tests;

public sealed class CaptureFreshnessPolicyTests
{
    private static readonly DateTime Now = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Fresh_frame_is_accepted()
    {
        var frame = FrameAt(Now - TimeSpan.FromMilliseconds(50));
        var policy = new CaptureFreshnessPolicy(
            maxAge: TimeSpan.FromMilliseconds(500),
            futureTolerance: TimeSpan.FromMilliseconds(100));

        var result = policy.Evaluate(frame, Now);

        Assert.True(result.IsAccepted);
        Assert.Null(result.RejectionReason);
    }

    [Fact]
    public void Stale_frame_is_rejected()
    {
        var frame = FrameAt(Now - TimeSpan.FromSeconds(2));
        var policy = new CaptureFreshnessPolicy(maxAge: TimeSpan.FromMilliseconds(500));

        var result = policy.Evaluate(frame, Now);

        Assert.False(result.IsAccepted);
        Assert.Equal("stale_frame_rejected", result.RejectionReason);
    }

    [Fact]
    public void Implausible_future_timestamp_is_rejected()
    {
        var frame = FrameAt(Now + TimeSpan.FromSeconds(1));
        var policy = new CaptureFreshnessPolicy(
            maxAge: TimeSpan.FromMilliseconds(500),
            futureTolerance: TimeSpan.FromMilliseconds(100));

        var result = policy.Evaluate(frame, Now);

        Assert.False(result.IsAccepted);
        Assert.Equal("future_timestamp_rejected", result.RejectionReason);
    }

    [Fact]
    public void Pipeline_does_not_run_detector_on_stale_frame()
    {
        var source = new FixedFrameSource(FrameAt(Now - TimeSpan.FromSeconds(2)));
        var detectorCalls = 0;
        var pipeline = new PerceptionPipeline(
            source,
            _ =>
            {
                detectorCalls++;
                return Array.Empty<Detection>();
            },
            freshnessPolicy: new CaptureFreshnessPolicy(maxAge: TimeSpan.FromMilliseconds(500)),
            clock: () => Now);

        var result = pipeline.ProcessNext();

        Assert.False(result.FrameAcquired);
        Assert.Equal(DataSourceKind.Unknown, result.Source);
        Assert.Equal("stale_frame_rejected", result.UnavailableReason);
        Assert.Equal(0, detectorCalls);
    }

    [Fact]
    public void Pipeline_processes_fresh_frame_normally()
    {
        var source = new FixedFrameSource(FrameAt(Now - TimeSpan.FromMilliseconds(20)));
        var detectorCalls = 0;
        var pipeline = new PerceptionPipeline(
            source,
            _ =>
            {
                detectorCalls++;
                return new[] { new Detection("mob", 10, 20, 1.0) };
            },
            freshnessPolicy: new CaptureFreshnessPolicy(maxAge: TimeSpan.FromMilliseconds(500)),
            clock: () => Now);

        var result = pipeline.ProcessNext();

        Assert.True(result.FrameAcquired);
        Assert.Equal(DataSourceKind.Simulated, result.Source);
        Assert.Null(result.UnavailableReason);
        Assert.Equal(1, detectorCalls);
        Assert.Single(result.Entities);
    }

    private static CaptureFrame FrameAt(DateTime capturedUtc)
    {
        const int width = 8;
        const int height = 8;
        return new CaptureFrame(
            width,
            height,
            new byte[width * height * 4],
            DataSourceKind.Simulated,
            capturedUtc);
    }

    private sealed class FixedFrameSource : IFrameSource
    {
        private readonly CaptureFrame _frame;
        private bool _served;

        public FixedFrameSource(CaptureFrame frame)
        {
            _frame = frame;
        }

        public DataSourceKind Source => _frame.Source;

        public bool TryAcquire(out CaptureFrame frame)
        {
            if (_served)
            {
                frame = null!;
                return false;
            }

            _served = true;
            frame = _frame;
            return true;
        }
    }
}
