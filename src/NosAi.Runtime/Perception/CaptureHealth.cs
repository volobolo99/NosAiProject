namespace NosAi.Runtime.Perception;

/// <summary>Operational health state of the asynchronous capture path.</summary>
public enum CaptureHealthState
{
    Healthy = 0,
    Degraded = 1,
    Unhealthy = 2,
}

/// <summary>
/// Point-in-time capture counters plus a deterministic health classification.
/// This is telemetry only: it never fabricates observations or overrides
/// Perception provenance.
/// </summary>
public sealed record CaptureHealthSnapshot(
    long SuccessfulAcquisitions,
    long PublishedFrames,
    long DroppedFrames,
    long AcquireFailures,
    double DropRatio,
    double FailureRatio,
    CaptureHealthState State,
    string Reason);

/// <summary>
/// Deterministic policy for classifying capture health from counters.
/// Thresholds intentionally tolerate occasional idle/no-frame returns while
/// surfacing sustained starvation or consumer backpressure.
/// </summary>
public sealed class CaptureHealthPolicy
{
    public int MinimumSamples { get; }
    public double DegradedDropRatio { get; }
    public double UnhealthyDropRatio { get; }
    public double DegradedFailureRatio { get; }
    public double UnhealthyFailureRatio { get; }

    public CaptureHealthPolicy(
        int minimumSamples = 20,
        double degradedDropRatio = 0.25,
        double unhealthyDropRatio = 0.60,
        double degradedFailureRatio = 0.50,
        double unhealthyFailureRatio = 0.90)
    {
        if (minimumSamples < 1) throw new ArgumentOutOfRangeException(nameof(minimumSamples));
        ValidateRatio(degradedDropRatio, nameof(degradedDropRatio));
        ValidateRatio(unhealthyDropRatio, nameof(unhealthyDropRatio));
        ValidateRatio(degradedFailureRatio, nameof(degradedFailureRatio));
        ValidateRatio(unhealthyFailureRatio, nameof(unhealthyFailureRatio));
        if (unhealthyDropRatio < degradedDropRatio)
            throw new ArgumentException("Unhealthy drop threshold must be >= degraded threshold.");
        if (unhealthyFailureRatio < degradedFailureRatio)
            throw new ArgumentException("Unhealthy failure threshold must be >= degraded threshold.");

        MinimumSamples = minimumSamples;
        DegradedDropRatio = degradedDropRatio;
        UnhealthyDropRatio = unhealthyDropRatio;
        DegradedFailureRatio = degradedFailureRatio;
        UnhealthyFailureRatio = unhealthyFailureRatio;
    }

    public CaptureHealthSnapshot Evaluate(
        long successfulAcquisitions,
        long publishedFrames,
        long droppedFrames,
        long acquireFailures)
    {
        if (successfulAcquisitions < 0 || publishedFrames < 0 || droppedFrames < 0 || acquireFailures < 0)
            throw new ArgumentOutOfRangeException("Capture counters cannot be negative.");

        long attempts = successfulAcquisitions + acquireFailures;
        double dropRatio = publishedFrames <= 0 ? 0.0 : Math.Clamp(droppedFrames / (double)publishedFrames, 0.0, 1.0);
        double failureRatio = attempts <= 0 ? 0.0 : Math.Clamp(acquireFailures / (double)attempts, 0.0, 1.0);

        if (attempts < MinimumSamples && publishedFrames < MinimumSamples)
        {
            return new CaptureHealthSnapshot(
                successfulAcquisitions, publishedFrames, droppedFrames, acquireFailures,
                dropRatio, failureRatio, CaptureHealthState.Healthy, "warming_up");
        }

        if (failureRatio >= UnhealthyFailureRatio)
            return Snapshot(CaptureHealthState.Unhealthy, "capture_starvation", successfulAcquisitions, publishedFrames, droppedFrames, acquireFailures, dropRatio, failureRatio);

        if (dropRatio >= UnhealthyDropRatio)
            return Snapshot(CaptureHealthState.Unhealthy, "consumer_backpressure_severe", successfulAcquisitions, publishedFrames, droppedFrames, acquireFailures, dropRatio, failureRatio);

        if (failureRatio >= DegradedFailureRatio)
            return Snapshot(CaptureHealthState.Degraded, "capture_failures_elevated", successfulAcquisitions, publishedFrames, droppedFrames, acquireFailures, dropRatio, failureRatio);

        if (dropRatio >= DegradedDropRatio)
            return Snapshot(CaptureHealthState.Degraded, "consumer_backpressure_elevated", successfulAcquisitions, publishedFrames, droppedFrames, acquireFailures, dropRatio, failureRatio);

        return Snapshot(CaptureHealthState.Healthy, "ok", successfulAcquisitions, publishedFrames, droppedFrames, acquireFailures, dropRatio, failureRatio);
    }

    private static CaptureHealthSnapshot Snapshot(
        CaptureHealthState state,
        string reason,
        long successfulAcquisitions,
        long publishedFrames,
        long droppedFrames,
        long acquireFailures,
        double dropRatio,
        double failureRatio)
        => new(successfulAcquisitions, publishedFrames, droppedFrames, acquireFailures, dropRatio, failureRatio, state, reason);

    private static void ValidateRatio(double value, string name)
    {
        if (double.IsNaN(value) || value < 0.0 || value > 1.0)
            throw new ArgumentOutOfRangeException(name, "Ratio threshold must be in [0,1].");
    }
}
