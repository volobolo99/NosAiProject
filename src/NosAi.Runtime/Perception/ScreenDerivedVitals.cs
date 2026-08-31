using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Perception;

/// <summary>
/// HP/MP from the screen (ADR-0012). Always DERIVED or UNKNOWN — never LIVE.
/// A failed check does not reuse the last number as if it were current.
/// </summary>
public sealed record ScreenVitalPair(
    ClassifiedValue<int> Current,
    ClassifiedValue<int> Maximum,
    double Confidence,
    string? FailureReason);

public sealed class ScreenDerivedVitalGate
{
    public const double MinConfidence = 0.85;
    public const int MaxAbsoluteJump = 400;

    public ScreenVitalPair Publish(int? current, int? maximum, double confidence, ScreenVitalPair? previous)
    {
        if (confidence < MinConfidence)
            return Unknown(confidence, "confidence_below_threshold");

        if (maximum is not int max || max <= 0)
            return Unknown(confidence, "maximum_not_observed");

        if (current is not int now)
            return Unknown(confidence, "current_not_recognized");

        if (now < 0 || now > max)
            return Unknown(confidence, "current_outside_0_max");

        if (previous is { Current.HasValue: true, Current.Source: DataSourceKind.Derived }
            && Math.Abs(now - previous.Current.Value) > MaxAbsoluteJump)
        {
            return Unknown(confidence, "continuity_jump_rejected");
        }

        var observed = DateTime.UtcNow;
        return new ScreenVitalPair(
            ClassifiedValue<int>.Derived(now, observed),
            ClassifiedValue<int>.Derived(max, observed),
            confidence,
            null);
    }

    /// <summary>Previous DERIVED reading, now stale. Not a substitute for a fresh value.</summary>
    public static ScreenVitalPair AsCached(ScreenVitalPair last, string reason = "stale_not_current")
    {
        if (!last.Current.HasValue || last.Current.Source != DataSourceKind.Derived)
            return Unknown(last.Confidence, reason);

        var at = last.Current.ObservedAtUtc;
        return new ScreenVitalPair(
            ClassifiedValue<int>.Cached(last.Current.Value, at, reason),
            last.Maximum.HasValue
                ? ClassifiedValue<int>.Cached(last.Maximum.Value, last.Maximum.ObservedAtUtc, reason)
                : ClassifiedValue<int>.Unknown(reason),
            last.Confidence,
            reason);
    }

    public static ScreenVitalPair Unknown(double confidence, string reason) => new(
        ClassifiedValue<int>.Unknown(reason),
        ClassifiedValue<int>.Unknown(reason),
        confidence,
        reason);
}

/// <summary>
/// Bar fill 0..1 from the screen (ADR-0012). Always DERIVED or UNKNOWN — never LIVE.
/// A full empty region is not published as 0: that is indistinguishable from "not a bar".
/// </summary>
public sealed record ScreenBarFill(
    ClassifiedValue<double> Ratio,
    double Confidence,
    string? FailureReason);

public sealed class ScreenDerivedBarGate
{
    public const double MinConfidence = 0.85;
    public const double MaxAbsoluteJump = 0.40;

    public ScreenBarFill Publish(double? ratio, double confidence, ScreenBarFill? previous)
    {
        if (confidence < MinConfidence)
            return Unknown(confidence, "confidence_below_threshold");

        if (ratio is not double value)
            return Unknown(confidence, "ratio_not_observed");

        if (value is < 0 or > 1)
            return Unknown(confidence, "ratio_outside_0_1");

        if (previous is { Ratio.HasValue: true, Ratio.Source: DataSourceKind.Derived }
            && Math.Abs(value - previous.Ratio.Value) > MaxAbsoluteJump)
        {
            return Unknown(confidence, "continuity_jump_rejected");
        }

        return new ScreenBarFill(
            ClassifiedValue<double>.Derived(value, DateTime.UtcNow),
            confidence,
            null);
    }

    public static ScreenBarFill Unknown(double confidence, string reason) =>
        new(ClassifiedValue<double>.Unknown(reason), confidence, reason);
}
