namespace NosAi.Runtime.Perception;

/// <summary>Cosa il riquadro del bersaglio ha detto di sé.</summary>
public enum TargetFrameState : byte
{
    /// <summary>I pixel non erano leggibili. Non è "nessun bersaglio".</summary>
    Unreadable = 0,
    /// <summary>Riquadro presente: c'è una barra bersaglio con un riempimento misurabile.</summary>
    Present = 1,
    /// <summary>Riquadro assente: la regione è leggibile e non contiene una barra.</summary>
    Absent = 2
}

public readonly record struct TargetFrameReading(
    TargetFrameState State,
    double? HpRatio,
    double Confidence,
    string? FailureReason);

/// <summary>
/// Reads the target-frame ROI. Unreadable is not absence: the TargetHpBar
/// fractions have never been calibrated on a live client, and treating a
/// failed read as "no target" would send the planner walking during combat
/// (ADR-0016).
/// </summary>
public static class TargetFrameReader
{
    /// <summary>
    /// A measured fill below this is not Present. Successful
    /// <see cref="HudBarFillReader"/> readings start here.
    /// </summary>
    public const double MinPresentConfidence = 0.86;

    /// <summary>Legge la regione del riquadro bersaglio da un buffer BGRA.</summary>
    /// <param name="bgra">Pixel della sola ROI, quattro byte per pixel.</param>
    public static TargetFrameReading Read(ReadOnlySpan<byte> bgra, int width, int height)
    {
        if (width < HudBarFillReader.MinWidth || height < HudBarFillReader.MinHeight)
            return Unreadable("crop_too_small");

        // In long arithmetic, not int. width * height * 4 wraps for a width of
        // 2^29 and a height of 2, and the wrapped zero matched an empty buffer's
        // length — so the crop passed this check and HudBarFillReader indexed a
        // buffer that had nothing in it and threw. A size check that can overflow
        // is a size check that holds only for the sizes somebody thought to try.
        long expected = (long)width * height * 4;
        if (bgra.Length != expected)
            return Unreadable("crop_truncated");

        HudBarMeasure measure = HudBarFillReader.Measure(bgra, width, height, HudFillHue.RedOrGreen);

        if (measure.FailureReason is { } reason)
        {
            // No family pixel after the crop passed size and length: the region
            // is readable and contains no bar. Speckle and broken coverage stay
            // Unreadable — that is not the absence of a target.
            if (reason == "no_bar_signature")
                return new TargetFrameReading(TargetFrameState.Absent, null, measure.Confidence, null);

            return Unreadable(reason);
        }

        if (measure.Ratio is not double ratio || measure.Confidence < MinPresentConfidence)
            return Unreadable("measure_without_confidence");

        if (ratio is < 0 or > 1)
            return Unreadable("ratio_out_of_range");

        return new TargetFrameReading(TargetFrameState.Present, ratio, measure.Confidence, null);
    }

    private static TargetFrameReading Unreadable(string reason)
        => new(TargetFrameState.Unreadable, HpRatio: null, Confidence: 0, reason);
}
