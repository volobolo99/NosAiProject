using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Perception;

/// <summary>
/// Certification for <see cref="HpMpBarReader"/> on synthetic frames.
/// </summary>
/// <remarks>
/// No desktop, no DXGI, no live client. The bars are painted at known
/// fractions of the <see cref="RoiSegmenter"/> regions so CI can say whether
/// the reader reports those fractions as DERIVED, and whether an
/// unrecognisable crop stays UNKNOWN instead of zero.
/// </remarks>
public static class HpMpBarReaderTestRunner
{
    public static bool RunAll()
    {
        Console.WriteLine("=== Screen HP/MP bar reader checks ===");

        bool allPassed = true;
        allPassed &= Run("An unknown frame is UNKNOWN, not a zero bar", TestUnknownFrameIsUnknown);
        allPassed &= Run("A capture with no pixels is UNKNOWN, not a zero bar", TestEmptyFrameIsUnknown);
        allPassed &= Run("An unavailable DXGI-shaped source is UNKNOWN", TestUnavailableSourceIsUnknown);
        allPassed &= Run("Painted bars at known fractions read DERIVED, never LIVE", TestPaintedBarsAreDerivedAtKnownFill);
        allPassed &= Run("A live-labelled frame still yields DERIVED fill, not LIVE", TestLiveFrameDoesNotPromoteTheGuess);
        allPassed &= Run("An unrecognisable bar is UNKNOWN, not zero", TestUnrecognisableBarIsUnknownNotZero);

        Console.WriteLine(allPassed
            ? "=== Screen HP/MP checks passed. Synthetic only: this is not real-environment verification. ==="
            : "=== Screen HP/MP checks FAILED. See the lines marked FAIL above. ===");
        return allPassed;
    }

    private static bool Run(string name, Func<bool> check)
    {
        try { return Report(name, check(), null); }
        catch (Exception ex) { return Report(name, false, $"{ex.GetType().Name}: {ex.Message}"); }
    }

    private static bool Report(string name, bool passed, string? error)
    {
        string detail = error is null ? string.Empty : $" [{error}]";
        Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] {name}{detail}");
        return passed;
    }

    private static bool TestUnknownFrameIsUnknown()
    {
        var frame = new CaptureFrame(0, 0, ReadOnlyMemory<byte>.Empty, DataSourceKind.Unknown, DateTime.UtcNow);
        HpMpBarReading reading = HpMpBarReader.Read(frame);
        return IsUnknown(reading.HpRatio, HpMpBarReader.FrameUnknownReason)
            && IsUnknown(reading.MpRatio, HpMpBarReader.FrameUnknownReason)
            && !reading.HpRatio.HasValue;
    }

    private static bool TestEmptyFrameIsUnknown()
    {
        var frame = new CaptureFrame(64, 64, ReadOnlyMemory<byte>.Empty, DataSourceKind.Simulated, DateTime.UtcNow);
        HpMpBarReading reading = HpMpBarReader.Read(frame);
        return IsUnknown(reading.HpRatio, HpMpBarReader.NoPixelsReason)
            && IsUnknown(reading.MpRatio, HpMpBarReader.NoPixelsReason)
            && !reading.HpRatio.HasValue;
    }

    private static bool TestUnavailableSourceIsUnknown()
    {
        HpMpBarReading reading = HpMpBarReader.Read(new UnavailableFrameSource());
        return IsUnknown(reading.HpRatio, HpMpBarReader.FrameUnknownReason)
            && IsUnknown(reading.MpRatio, HpMpBarReader.FrameUnknownReason)
            && !reading.HpRatio.HasValue;
    }

    private static bool TestPaintedBarsAreDerivedAtKnownFill()
    {
        const double hpFill = 0.50;
        const double mpFill = 0.75;
        CaptureFrame frame = PaintHud(1024, 768, hpFill, mpFill, DataSourceKind.Simulated);
        HpMpBarReading reading = HpMpBarReader.Read(frame);

        return reading.HpRatio.HasValue
            && reading.HpRatio.Source == DataSourceKind.Derived
            && reading.HpRatio.Source != DataSourceKind.Live
            && Math.Abs(reading.HpRatio.Value - hpFill) < 0.02
            && reading.MpRatio.HasValue
            && reading.MpRatio.Source == DataSourceKind.Derived
            && Math.Abs(reading.MpRatio.Value - mpFill) < 0.02;
    }

    private static bool TestLiveFrameDoesNotPromoteTheGuess()
    {
        CaptureFrame frame = PaintHud(1024, 768, 1.0, 1.0, DataSourceKind.Live);
        HpMpBarReading reading = HpMpBarReader.Read(frame);
        return reading.HpRatio.HasValue
            && reading.HpRatio.Source == DataSourceKind.Derived
            && reading.MpRatio.HasValue
            && reading.MpRatio.Source == DataSourceKind.Derived;
    }

    private static bool TestUnrecognisableBarIsUnknownNotZero()
    {
        CaptureFrame frame = PaintHud(1024, 768, hpFill: 0.50, mpFill: 0.75, DataSourceKind.Simulated);
        byte[] pixels = frame.Bgra.ToArray();
        var regions = RoiSegmenter.Segment(frame.Width, frame.Height);
        foreach (RegionOfInterest region in regions)
        {
            if (region.Kind is RoiKind.PlayerHpBar or RoiKind.PlayerMpBar)
                PaintCheckerboard(pixels, frame.Width, region.Rect);
        }

        var noisy = new CaptureFrame(frame.Width, frame.Height, pixels, DataSourceKind.Simulated, frame.CapturedUtc);
        HpMpBarReading reading = HpMpBarReader.Read(noisy);

        return IsUnknownPrefixed(reading.HpRatio, HpMpBarReader.BarNotRecognizedReason)
            && IsUnknownPrefixed(reading.MpRatio, HpMpBarReader.BarNotRecognizedReason)
            && !reading.HpRatio.HasValue
            && !reading.MpRatio.HasValue;
    }

    private static bool IsUnknown(ClassifiedValue<double> value, string reason)
        => !value.HasValue
           && value.Source == DataSourceKind.Unknown
           && value.FailureReason == reason;

    private static bool IsUnknownPrefixed(ClassifiedValue<double> value, string prefix)
        => !value.HasValue
           && value.Source == DataSourceKind.Unknown
           && value.FailureReason is { } reason
           && reason.StartsWith(prefix, StringComparison.Ordinal);

    private static CaptureFrame PaintHud(
        int width, int height, double hpFill, double mpFill, DataSourceKind source)
    {
        var bgra = new byte[width * height * 4];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = 16;
            bgra[i + 1] = 16;
            bgra[i + 2] = 16;
            bgra[i + 3] = 255;
        }

        foreach (RegionOfInterest region in RoiSegmenter.Segment(width, height))
        {
            if (region.Kind == RoiKind.PlayerHpBar)
                PaintRoi(bgra, width, region.Rect, hpFill, r: 220, g: 30, b: 30);
            else if (region.Kind == RoiKind.PlayerMpBar)
                PaintRoi(bgra, width, region.Rect, mpFill, r: 30, g: 40, b: 220);
        }

        return new CaptureFrame(width, height, bgra, source, DateTime.UtcNow);
    }

    private static void PaintRoi(
        byte[] bgra, int frameWidth, PixelRect rect, double fill, byte r, byte g, byte b)
    {
        int fillColumns = (int)Math.Round(rect.Width * fill);
        for (int y = 0; y < rect.Height; y++)
        for (int x = 0; x < rect.Width; x++)
        {
            int i = ((rect.Y + y) * frameWidth + rect.X + x) * 4;
            bool filled = x < fillColumns;
            bgra[i] = filled ? b : (byte)24;
            bgra[i + 1] = filled ? g : (byte)24;
            bgra[i + 2] = filled ? r : (byte)24;
            bgra[i + 3] = 255;
        }
    }

    /// <summary>
    /// Alternating bright colours so every column has more vertical runs than a
    /// bar is allowed. Speckle, not a fill edge.
    /// </summary>
    private static void PaintCheckerboard(byte[] bgra, int frameWidth, PixelRect rect)
    {
        for (int y = 0; y < rect.Height; y++)
        for (int x = 0; x < rect.Width; x++)
        {
            int i = ((rect.Y + y) * frameWidth + rect.X + x) * 4;
            bool on = ((x + y) & 1) == 0;
            bgra[i] = on ? (byte)200 : (byte)40;
            bgra[i + 1] = on ? (byte)40 : (byte)200;
            bgra[i + 2] = on ? (byte)200 : (byte)40;
            bgra[i + 3] = 255;
        }
    }
}
