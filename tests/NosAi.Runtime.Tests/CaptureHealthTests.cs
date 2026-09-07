using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.Runtime.Tests;

public sealed class CaptureHealthTests
{
    [Fact]
    public void Healthy_when_failures_and_drops_are_low()
    {
        var policy = new CaptureHealthPolicy(minimumSamples: 10);
        var snapshot = policy.Evaluate(
            successfulAcquisitions: 95,
            publishedFrames: 95,
            droppedFrames: 5,
            acquireFailures: 5);

        Assert.Equal(CaptureHealthState.Healthy, snapshot.State);
        Assert.Equal("ok", snapshot.Reason);
        Assert.InRange(snapshot.DropRatio, 0.05, 0.06);
        Assert.InRange(snapshot.FailureRatio, 0.05, 0.06);
    }

    [Fact]
    public void Degraded_when_backpressure_is_elevated()
    {
        var policy = new CaptureHealthPolicy(
            minimumSamples: 10,
            degradedDropRatio: 0.25,
            unhealthyDropRatio: 0.60);

        var snapshot = policy.Evaluate(
            successfulAcquisitions: 100,
            publishedFrames: 100,
            droppedFrames: 30,
            acquireFailures: 0);

        Assert.Equal(CaptureHealthState.Degraded, snapshot.State);
        Assert.Equal("consumer_backpressure_elevated", snapshot.Reason);
    }

    [Fact]
    public void Unhealthy_when_capture_is_starved()
    {
        var policy = new CaptureHealthPolicy(
            minimumSamples: 10,
            degradedFailureRatio: 0.50,
            unhealthyFailureRatio: 0.90);

        var snapshot = policy.Evaluate(
            successfulAcquisitions: 5,
            publishedFrames: 5,
            droppedFrames: 0,
            acquireFailures: 95);

        Assert.Equal(CaptureHealthState.Unhealthy, snapshot.State);
        Assert.Equal("capture_starvation", snapshot.Reason);
        Assert.Equal(0.95, snapshot.FailureRatio, 3);
    }

    [Fact]
    public void Warmup_does_not_raise_false_alarm()
    {
        var policy = new CaptureHealthPolicy(minimumSamples: 20);
        var snapshot = policy.Evaluate(
            successfulAcquisitions: 1,
            publishedFrames: 1,
            droppedFrames: 1,
            acquireFailures: 4);

        Assert.Equal(CaptureHealthState.Healthy, snapshot.State);
        Assert.Equal("warming_up", snapshot.Reason);
    }

    /// <summary>
    /// The health snapshot reports the same numbers the capture's own counters
    /// report.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This test used to fail intermittently in full runs and pass in isolation,
    /// which reads like host contention but is not. <c>BurstFrameSource</c>
    /// hands out 50 frames and then refuses forever, and
    /// <c>TripleBufferedCapture</c>'s loop counts every refusal: after the burst
    /// is exhausted <c>AcquireFailures</c> grows by one roughly every
    /// millisecond, for as long as the object lives. The assertions compare a
    /// snapshot taken at one instant with counters read a few instructions
    /// later, so the last one held only when both reads landed in the same
    /// millisecond -- likely on an idle machine, a coin flip under load.
    /// </para>
    /// <para>
    /// Disposing first ends the loop and joins its thread, so the counters are
    /// final before anything reads them. Nothing about what is asserted has
    /// changed; the values are simply no longer moving while being compared.
    /// </para>
    /// </remarks>
    [Fact]
    public void Triple_buffered_capture_exposes_live_counters()
    {
        var capture = new TripleBufferedCapture(
            new BurstFrameSource(frameCount: 50),
            startImmediately: true,
            healthPolicy: new CaptureHealthPolicy(minimumSamples: 5));

        for (var i = 0; i < 200 && capture.SuccessfulAcquisitions < 20; i++)
            Thread.Sleep(2);

        // The counters must stop moving before two of them can be compared.
        // Dispose cancels the loop and joins the thread, so every read below is
        // of a value nothing can still change. See the remarks above for why
        // this is not merely tidiness.
        capture.Dispose();

        var snapshot = capture.GetHealthSnapshot();

        Assert.True(snapshot.SuccessfulAcquisitions > 0);
        Assert.True(snapshot.PublishedFrames > 0);
        Assert.Equal(capture.SuccessfulAcquisitions, snapshot.SuccessfulAcquisitions);
        Assert.Equal(capture.Buffer.PublishedCount, snapshot.PublishedFrames);
        Assert.Equal(capture.Buffer.DroppedCount, snapshot.DroppedFrames);
        Assert.Equal(capture.AcquireFailures, snapshot.AcquireFailures);
    }

    private sealed class BurstFrameSource : IFrameSource
    {
        private readonly int _frameCount;
        private int _index;

        public BurstFrameSource(int frameCount)
        {
            _frameCount = frameCount;
        }

        public DataSourceKind Source => DataSourceKind.Simulated;

        public bool TryAcquire(out CaptureFrame frame)
        {
            var index = Interlocked.Increment(ref _index);
            if (index > _frameCount)
            {
                frame = null!;
                Thread.Sleep(1);
                return false;
            }

            const int width = 4;
            const int height = 4;
            frame = new CaptureFrame(
                width,
                height,
                new byte[width * height * 4],
                DataSourceKind.Simulated,
                DateTime.UtcNow);
            return true;
        }
    }
}
