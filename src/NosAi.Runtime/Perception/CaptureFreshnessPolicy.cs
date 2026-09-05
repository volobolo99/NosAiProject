using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Perception;

/// <summary>
/// Validates whether a captured frame is temporally safe to use.
/// Old or implausibly future-dated frames are rejected instead of entering
/// the canonical perception/world-state pipeline.
/// </summary>
public sealed class CaptureFreshnessPolicy
{
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan DefaultFutureTolerance = TimeSpan.FromMilliseconds(100);

    public TimeSpan MaxAge { get; }
    public TimeSpan FutureTolerance { get; }

    public CaptureFreshnessPolicy(TimeSpan? maxAge = null, TimeSpan? futureTolerance = null)
    {
        MaxAge = maxAge ?? DefaultMaxAge;
        FutureTolerance = futureTolerance ?? DefaultFutureTolerance;

        if (MaxAge <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maxAge), "MaxAge must be positive.");
        if (FutureTolerance < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(futureTolerance), "FutureTolerance cannot be negative.");
    }

    public CaptureFreshnessResult Evaluate(CaptureFrame frame, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (!frame.HasPixels)
            return CaptureFreshnessResult.Reject("frame_has_no_pixels", TimeSpan.Zero);

        var age = utcNow - frame.CapturedUtc;

        if (age > MaxAge)
            return CaptureFreshnessResult.Reject("stale_frame_rejected", age);

        if (age < -FutureTolerance)
            return CaptureFreshnessResult.Reject("future_timestamp_rejected", age);

        return CaptureFreshnessResult.Accept(age);
    }
}

public readonly record struct CaptureFreshnessResult(
    bool IsAccepted,
    string? RejectionReason,
    TimeSpan Age)
{
    public static CaptureFreshnessResult Accept(TimeSpan age) => new(true, null, age);
    public static CaptureFreshnessResult Reject(string reason, TimeSpan age) => new(false, reason, age);
}
