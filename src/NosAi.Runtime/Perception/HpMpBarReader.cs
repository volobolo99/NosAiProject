using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Perception;

/// <summary>
/// HP and MP fill ratios from the HUD bars in a captured frame.
/// </summary>
/// <remarks>
/// <para>
/// A second source for the same fact Phase 2 reads from memory, and a
/// different representation: pixels, not uint32s. The classification is
/// always <see cref="DataSourceKind.Derived"/> when the bars parse, never
/// <see cref="DataSourceKind.Live"/> — a fill edge is a guess about a
/// gradient, not a number the client stored. A live DXGI frame does not
/// promote the guess.
/// </para>
/// <para>
/// Consumes <see cref="CaptureFrame"/> and <see cref="RoiSegmenter"/> as they
/// stand. The fill itself is <see cref="HudBarFillReader"/>'s: this type
/// classifies that measure and refuses to turn a missing bar into zero.
/// </para>
/// </remarks>
public sealed record HpMpBarReading(
    ClassifiedValue<double> HpRatio,
    ClassifiedValue<double> MpRatio);

/// <summary>
/// Estimates HP/MP bar fill from HUD pixels.
/// </summary>
public static class HpMpBarReader
{
    public const string FrameUnknownReason = "hpmp_frame_unknown";
    public const string NoPixelsReason = "hpmp_no_frame_pixels";
    public const string RoiEmptyReason = "hpmp_roi_empty";
    public const string BarNotRecognizedReason = "hpmp_bar_not_recognized";

    /// <summary>
    /// Reads the next frame from <paramref name="source"/>. A source that
    /// yields nothing is UNKNOWN, not an invented bar.
    /// </summary>
    public static HpMpBarReading Read(IFrameSource source, PixelRect? clientArea = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!source.TryAcquire(out CaptureFrame frame))
        {
            string reason = source.Source == DataSourceKind.Unknown
                ? FrameUnknownReason
                : NoPixelsReason;
            return BothUnknown(reason);
        }

        return Read(frame, clientArea);
    }

    /// <summary>
    /// Estimates HP and MP fill on <paramref name="frame"/>.
    /// </summary>
    /// <param name="clientArea">
    /// Where the game sits inside the frame. Null means the client fills the
    /// frame, which is only true of a fullscreen capture.
    /// </param>
    public static HpMpBarReading Read(CaptureFrame frame, PixelRect? clientArea = null)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.Source == DataSourceKind.Unknown)
            return BothUnknown(FrameUnknownReason);

        if (!frame.HasPixels)
            return BothUnknown(NoPixelsReason);

        var regions = RoiSegmenter.Segment(frame.Width, frame.Height, clientArea);
        return new HpMpBarReading(
            ReadBar(frame, regions, RoiKind.PlayerHpBar, HudFillHue.RedOrGreen),
            ReadBar(frame, regions, RoiKind.PlayerMpBar, HudFillHue.Blue));
    }

    private static ClassifiedValue<double> ReadBar(
        CaptureFrame frame,
        IReadOnlyList<RegionOfInterest> regions,
        RoiKind kind,
        HudFillHue hue)
    {
        if (!TryFind(regions, kind, out PixelRect roi) || !roi.IsWithin(frame.Width, frame.Height))
            return ClassifiedValue<double>.Unknown(RoiEmptyReason);

        byte[] crop = Crop(frame, roi);
        if (crop.Length == 0)
            return ClassifiedValue<double>.Unknown(RoiEmptyReason);

        HudBarMeasure measure = HudBarFillReader.Measure(crop, roi.Width, roi.Height, hue);
        if (measure.FailureReason is not null || measure.Ratio is not { } ratio)
        {
            string inner = measure.FailureReason ?? "ratio_not_observed";
            return ClassifiedValue<double>.Unknown($"{BarNotRecognizedReason}:{inner}");
        }

        if (ratio is < 0 or > 1)
            return ClassifiedValue<double>.Unknown($"{BarNotRecognizedReason}:ratio_outside_0_1");

        // DERIVED even when the frame is LIVE: the pixels were captured, the
        // percentage was inferred. LIVE would claim the client stated this number.
        return ClassifiedValue<double>.Derived(ratio, frame.CapturedUtc);
    }

    private static bool TryFind(
        IReadOnlyList<RegionOfInterest> regions, RoiKind kind, out PixelRect rect)
    {
        foreach (RegionOfInterest region in regions)
        {
            if (region.Kind == kind)
            {
                rect = region.Rect;
                return true;
            }
        }

        rect = default;
        return false;
    }

    private static byte[] Crop(CaptureFrame frame, PixelRect rect)
    {
        if (!rect.IsWithin(frame.Width, frame.Height) || frame.Bgra.Length < frame.Width * frame.Height * 4)
            return Array.Empty<byte>();

        var dest = new byte[rect.Width * rect.Height * 4];
        ReadOnlySpan<byte> src = frame.Bgra.Span;
        for (int y = 0; y < rect.Height; y++)
        {
            int srcRow = ((rect.Y + y) * frame.Width + rect.X) * 4;
            src.Slice(srcRow, rect.Width * 4).CopyTo(dest.AsSpan(y * rect.Width * 4, rect.Width * 4));
        }

        return dest;
    }

    private static HpMpBarReading BothUnknown(string reason) => new(
        ClassifiedValue<double>.Unknown(reason),
        ClassifiedValue<double>.Unknown(reason));
}
