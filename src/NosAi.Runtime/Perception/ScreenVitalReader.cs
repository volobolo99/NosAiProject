using System;
using System.Collections.Generic;

namespace NosAi.Runtime.Perception;

/// <summary>
/// One screen-derived observation. Bar fill may be DERIVED; numeric HP/MP stay
/// UNKNOWN until a trained glyph atlas recognises both current and maximum.
/// Nothing here is LIVE, and nothing is written to the Gate 1 snapshot.
/// </summary>
public sealed record ScreenVitalObservation(
    PixelRect HpRoi,
    PixelRect MpRoi,
    ScreenBarFill HpBar,
    ScreenBarFill MpBar,
    ScreenVitalPair Hp,
    ScreenVitalPair Mp,
    int HpGlyphs,
    int MpGlyphs,
    int TrainedGlyphs);

public sealed class ScreenVitalReader
{
    private readonly ScreenDerivedBarGate _barGate = new();
    private readonly ScreenDerivedVitalGate _vitalGate = new();
    private readonly GlyphHashOcrCache? _ocr;

    public ScreenVitalReader(GlyphHashOcrCache? ocr = null)
    {
        _ocr = ocr;
    }

    public ScreenVitalObservation Read(
        CaptureFrame frame,
        ScreenBarFill? previousHpBar = null,
        ScreenBarFill? previousMpBar = null,
        ScreenVitalPair? previousHp = null,
        ScreenVitalPair? previousMp = null)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (!frame.HasPixels)
        {
            var missing = ScreenDerivedBarGate.Unknown(0, "no_frame_pixels");
            var missingVitals = ScreenDerivedVitalGate.Unknown(0, "no_frame_pixels");
            return new ScreenVitalObservation(
                new PixelRect(0, 0, 0, 0),
                new PixelRect(0, 0, 0, 0),
                missing,
                missing,
                missingVitals,
                missingVitals,
                0,
                0,
                _ocr?.TrainedGlyphCount ?? 0);
        }

        var regions = RoiSegmenter.Segment(frame.Width, frame.Height);
        var hpRoi = Find(regions, RoiKind.PlayerHpBar).Rect;
        var mpRoi = Find(regions, RoiKind.PlayerMpBar).Rect;

        var hpCrop = Crop(frame, hpRoi);
        var mpCrop = Crop(frame, mpRoi);

        var hpBar = ClassifyBar(
            HudBarFillReader.Measure(hpCrop, hpRoi.Width, hpRoi.Height, HudFillHue.RedOrGreen),
            previousHpBar);
        var mpBar = ClassifyBar(
            HudBarFillReader.Measure(mpCrop, mpRoi.Width, mpRoi.Height, HudFillHue.Blue),
            previousMpBar);

        var hpGlyphs = HudGlyphExtractor.Extract(hpCrop, hpRoi.Width, hpRoi.Height);
        var mpGlyphs = HudGlyphExtractor.Extract(mpCrop, mpRoi.Width, mpRoi.Height);

        return new ScreenVitalObservation(
            hpRoi,
            mpRoi,
            hpBar,
            mpBar,
            Recognize(hpGlyphs, previousHp),
            Recognize(mpGlyphs, previousMp),
            hpGlyphs.Count,
            mpGlyphs.Count,
            _ocr?.TrainedGlyphCount ?? 0);
    }

    private ScreenBarFill ClassifyBar(HudBarMeasure measure, ScreenBarFill? previous)
    {
        if (measure.FailureReason is not null)
            return ScreenDerivedBarGate.Unknown(measure.Confidence, measure.FailureReason);

        return _barGate.Publish(measure.Ratio, measure.Confidence, previous);
    }

    private ScreenVitalPair Recognize(IReadOnlyList<byte[]> glyphs, ScreenVitalPair? previous)
    {
        if (_ocr is null || _ocr.TrainedGlyphCount == 0)
            return ScreenDerivedVitalGate.Unknown(0, "ocr_glyphs_not_trained");

        if (glyphs.Count == 0)
            return ScreenDerivedVitalGate.Unknown(0, "no_glyphs_in_roi");

        var text = _ocr.Recognize(glyphs);
        if (text.Contains('?', StringComparison.Ordinal))
            return ScreenDerivedVitalGate.Unknown(0, "unrecognized_glyph");

        if (!TryParseCurrentMax(text, out var current, out var maximum))
            return ScreenDerivedVitalGate.Unknown(0, "numeric_text_not_parsed");

        return _vitalGate.Publish(current, maximum, 0.90, previous);
    }

    public static bool TryParseCurrentMax(string text, out int current, out int maximum)
    {
        current = 0;
        maximum = 0;
        var slash = text.IndexOf('/');
        if (slash <= 0 || slash == text.Length - 1)
            return false;
        return int.TryParse(text.AsSpan(0, slash), out current)
            && int.TryParse(text.AsSpan(slash + 1), out maximum);
    }

    private static RegionOfInterest Find(IReadOnlyList<RegionOfInterest> regions, RoiKind kind)
    {
        foreach (var region in regions)
        {
            if (region.Kind == kind)
                return region;
        }

        throw new InvalidOperationException($"ROI {kind} missing from segmenter.");
    }

    public static byte[] Crop(CaptureFrame frame, PixelRect rect)
    {
        if (!rect.IsWithin(frame.Width, frame.Height) || frame.Bgra.Length < frame.Width * frame.Height * 4)
            return Array.Empty<byte>();

        var dest = new byte[rect.Width * rect.Height * 4];
        var src = frame.Bgra.Span;
        for (var y = 0; y < rect.Height; y++)
        {
            var srcRow = ((rect.Y + y) * frame.Width + rect.X) * 4;
            src.Slice(srcRow, rect.Width * 4).CopyTo(dest.AsSpan(y * rect.Width * 4, rect.Width * 4));
        }

        return dest;
    }
}
