namespace NosAi.Runtime.Perception;

/// <summary>
/// Production boundary for visual object detectors.
/// Implementations may use ONNX Runtime, DirectML, CUDA/TensorRT or another
/// backend, but the perception pipeline depends only on this contract.
/// </summary>
public interface IObjectDetector
{
    string Name { get; }

    IReadOnlyList<Detection> Detect(CaptureFrame frame);
}

/// <summary>
/// Production boundary for temporal trackers.
/// A future ByteTrack adapter can implement this without changing the pipeline.
/// </summary>
public interface IObjectTracker
{
    int ActiveTrackCount { get; }

    IReadOnlyList<TrackedEntity> Track(IReadOnlyList<Detection> detections, double deltaSeconds);
}

/// <summary>
/// Compatibility adapter for deterministic/test delegates and lightweight
/// detector implementations that do not need their own class.
/// </summary>
public sealed class DelegateObjectDetector : IObjectDetector
{
    private readonly Func<CaptureFrame, IReadOnlyList<Detection>> _detect;

    public string Name { get; }

    public DelegateObjectDetector(
        Func<CaptureFrame, IReadOnlyList<Detection>> detect,
        string name = "delegate-detector")
    {
        _detect = detect ?? throw new ArgumentNullException(nameof(detect));
        Name = string.IsNullOrWhiteSpace(name) ? "delegate-detector" : name;
    }

    public IReadOnlyList<Detection> Detect(CaptureFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return _detect(frame) ?? Array.Empty<Detection>();
    }
}

/// <summary>
/// No-op detector used for fail-closed startup or when a production model has
/// not yet been provisioned. It emits no detections instead of fabricating them.
/// </summary>
public sealed class NullObjectDetector : IObjectDetector
{
    public string Name => "null-detector";

    public IReadOnlyList<Detection> Detect(CaptureFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return Array.Empty<Detection>();
    }
}
