using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.Runtime.Tests;

public sealed class DetectionBoundaryTests
{
    [Fact]
    public void Pipeline_accepts_production_detector_contract()
    {
        var now = new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);
        var source = new SingleFrameSource(Frame(now));
        var detector = new RecordingDetector();
        var tracker = new RecordingTracker();

        var pipeline = new PerceptionPipeline(
            source,
            detector,
            tracker,
            freshnessPolicy: new CaptureFreshnessPolicy(TimeSpan.FromSeconds(1)),
            clock: () => now);

        var result = pipeline.ProcessNext();

        Assert.True(result.FrameAcquired);
        Assert.Equal(1, detector.Calls);
        Assert.Equal(1, tracker.Calls);
        Assert.Single(result.Entities);
        Assert.Equal("Monster", result.Entities[0].Kind);
    }

    [Fact]
    public void Delegate_detector_remains_backward_compatible()
    {
        var now = new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);
        var source = new SingleFrameSource(Frame(now));
        var calls = 0;

        var pipeline = new PerceptionPipeline(
            source,
            frame =>
            {
                calls++;
                return new[] { new Detection("Npc", 20, 30, 1.0) };
            },
            freshnessPolicy: new CaptureFreshnessPolicy(TimeSpan.FromSeconds(1)),
            clock: () => now);

        var result = pipeline.ProcessNext();

        Assert.True(result.FrameAcquired);
        Assert.Equal(1, calls);
        Assert.Single(result.Entities);
        Assert.Equal("Npc", result.Entities[0].Kind);
    }

    [Fact]
    public void Null_detector_fails_closed_with_zero_entities()
    {
        var now = new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);
        var source = new SingleFrameSource(Frame(now));

        var pipeline = new PerceptionPipeline(
            source,
            new NullObjectDetector(),
            freshnessPolicy: new CaptureFreshnessPolicy(TimeSpan.FromSeconds(1)),
            clock: () => now);

        var result = pipeline.ProcessNext();

        Assert.True(result.FrameAcquired);
        Assert.Empty(result.Entities);
    }

    [Fact]
    public void Temporal_tracker_implements_production_tracker_contract()
    {
        IObjectTracker tracker = new TemporalEntityTracker();
        var result = tracker.Track(
            new[] { new Detection("Monster", 100, 100, 0.8) },
            deltaSeconds: 0.016);

        Assert.Single(result);
        Assert.Equal(1, tracker.ActiveTrackCount);
        Assert.Equal("Monster", result[0].Kind);
    }

    private static CaptureFrame Frame(DateTime capturedUtc)
    {
        const int width = 16;
        const int height = 16;
        return new CaptureFrame(
            width,
            height,
            new byte[width * height * 4],
            DataSourceKind.Simulated,
            capturedUtc);
    }

    private sealed class SingleFrameSource : IFrameSource
    {
        private readonly CaptureFrame _frame;
        private bool _served;

        public SingleFrameSource(CaptureFrame frame) => _frame = frame;

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

    private sealed class RecordingDetector : IObjectDetector
    {
        public string Name => "recording-detector";
        public int Calls { get; private set; }

        public IReadOnlyList<Detection> Detect(CaptureFrame frame)
        {
            Calls++;
            return new[] { new Detection("Monster", 10, 20, 0.75) };
        }
    }

    private sealed class RecordingTracker : IObjectTracker
    {
        public int Calls { get; private set; }
        public int ActiveTrackCount { get; private set; }

        public IReadOnlyList<TrackedEntity> Track(IReadOnlyList<Detection> detections, double deltaSeconds)
        {
            Calls++;
            ActiveTrackCount = detections.Count;
            return detections
                .Select((d, i) => new TrackedEntity(
                    i + 1,
                    d.Kind,
                    d.X,
                    d.Y,
                    0,
                    0,
                    d.HpRatio,
                    0))
                .ToArray();
        }
    }
}
