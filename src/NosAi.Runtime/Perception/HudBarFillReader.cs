using System;

namespace NosAi.Runtime.Perception;

/// <summary>
/// Expected fill colour of a HUD bar. Grey/white/black never count: those are
/// wallpaper, text, or an empty region, not a NosTale bar.
/// </summary>
public enum HudFillHue : byte
{
    RedOrGreen = 0,
    Blue = 1
}

/// <summary>Unsigned measure from pixels, before the classification gate.</summary>
public readonly record struct HudBarMeasure(double? Ratio, double Confidence, string? FailureReason);

/// <summary>
/// Reads a horizontal HUD bar from a BGRA crop. Publishes a fill ratio only when
/// the column profile looks like a bar (one fill/empty split, or uniformly full
/// in the expected hue). A dark or noisy crop is not 0% HP.
/// </summary>
public static class HudBarFillReader
{
    public const int MinWidth = 24;
    public const int MinHeight = 2;
    public const double FillColumnThreshold = 0.70;
    public const double MaxMixedColumnRatio = 0.25;

    public static HudBarMeasure Measure(ReadOnlySpan<byte> bgra, int width, int height, HudFillHue hue)
    {
        if (width < MinWidth || height < MinHeight)
            return new HudBarMeasure(null, 0, "crop_too_small");

        var expected = width * height * 4;
        if (bgra.Length < expected)
            return new HudBarMeasure(null, 0, "crop_truncated");

        var filled = new bool[width];
        var mixed = 0;
        var filledColumns = 0;

        for (var x = 0; x < width; x++)
        {
            var votes = 0;
            for (var y = 0; y < height; y++)
            {
                var i = (y * width + x) * 4;
                if (MatchesHue(bgra[i + 2], bgra[i + 1], bgra[i], hue))
                    votes++;
            }

            var score = votes / (double)height;
            if (score >= FillColumnThreshold)
            {
                filled[x] = true;
                filledColumns++;
            }

            if (score is > 0.20 and < 0.80)
                mixed++;
        }

        if (mixed > width * MaxMixedColumnRatio)
            return new HudBarMeasure(null, 0, "noisy_bar_profile");

        var transitions = 0;
        for (var x = 1; x < width; x++)
        {
            if (filled[x] != filled[x - 1])
                transitions++;
        }

        if (transitions > 1)
            return new HudBarMeasure(null, 0, "noisy_bar_profile");

        if (transitions == 0)
        {
            if (filledColumns == width)
            {
                var confidence = mixed == 0 ? 0.90 : 0.86;
                return new HudBarMeasure(1.0, confidence, null);
            }

            return new HudBarMeasure(null, 0, "no_bar_signature");
        }

        // One transition: classic left-fill / right-empty (or the reverse).
        var ratio = filledColumns / (double)width;
        if (ratio is < 0.02 or > 0.98)
            return new HudBarMeasure(null, 0, "degenerate_fill");

        var edgePenalty = Math.Min(0.04, mixed / (double)width);
        return new HudBarMeasure(ratio, 0.90 - edgePenalty, null);
    }

    internal static bool MatchesHue(byte r, byte g, byte b, HudFillHue hue)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        if (max - min < 40 || max < 80)
            return false;

        return hue switch
        {
            HudFillHue.RedOrGreen => (r >= g + 25 && r >= b + 25) || (g >= r + 20 && g >= b + 20),
            HudFillHue.Blue => b >= r + 20 && b >= g + 20,
            _ => false
        };
    }
}
