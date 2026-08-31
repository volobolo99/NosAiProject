using System;
using System.Collections.Generic;

namespace NosAi.Runtime.Perception;

/// <summary>
/// Splits a HUD crop into normalized glyph bitmaps. Unknown ink is still a
/// glyph: recognition, not extraction, decides whether it is a digit.
/// </summary>
public static class HudGlyphExtractor
{
    public const int NormalizedWidth = 12;
    public const int NormalizedHeight = 16;

    public static IReadOnlyList<byte[]> Extract(ReadOnlySpan<byte> bgra, int width, int height)
    {
        if (width <= 0 || height <= 0 || bgra.Length < width * height * 4)
            return Array.Empty<byte[]>();

        var ink = new bool[width * height];
        var inkColumns = 0;
        for (var x = 0; x < width; x++)
        {
            var columnHasInk = false;
            for (var y = 0; y < height; y++)
            {
                var i = (y * width + x) * 4;
                if (IsInk(bgra[i + 2], bgra[i + 1], bgra[i]))
                {
                    ink[y * width + x] = true;
                    columnHasInk = true;
                }
            }

            if (columnHasInk)
                inkColumns++;
        }

        // A solid bar with no text is not a string of glyphs.
        if (inkColumns == 0 || inkColumns > width * 0.85)
            return Array.Empty<byte[]>();

        var glyphs = new List<byte[]>();
        var x0 = 0;
        while (x0 < width)
        {
            while (x0 < width && !ColumnHasInk(ink, width, height, x0))
                x0++;
            if (x0 >= width)
                break;

            var x1 = x0 + 1;
            while (x1 < width && ColumnHasInk(ink, width, height, x1))
                x1++;

            var glyph = Normalize(ink, width, height, x0, x1);
            if (glyph is not null)
                glyphs.Add(glyph);
            x0 = x1;
        }

        return glyphs;
    }

    /// <summary>White / near-white HUD numerals, not the saturated bar fill.</summary>
    internal static bool IsInk(byte r, byte g, byte b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        return max >= 200 && max - min <= 50;
    }

    private static bool ColumnHasInk(bool[] ink, int width, int height, int x)
    {
        for (var y = 0; y < height; y++)
        {
            if (ink[y * width + x])
                return true;
        }

        return false;
    }

    private static byte[]? Normalize(bool[] ink, int width, int height, int x0, int x1)
    {
        var gw = x1 - x0;
        if (gw < 1 || gw > width / 2)
            return null;

        var y0 = height;
        var y1 = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                if (!ink[y * width + x])
                    continue;
                if (y < y0) y0 = y;
                if (y + 1 > y1) y1 = y + 1;
            }
        }

        var gh = y1 - y0;
        if (gh < 2)
            return null;

        var dest = new byte[NormalizedWidth * NormalizedHeight];
        for (var ny = 0; ny < NormalizedHeight; ny++)
        {
            var sy = y0 + ny * gh / NormalizedHeight;
            for (var nx = 0; nx < NormalizedWidth; nx++)
            {
                var sx = x0 + nx * gw / NormalizedWidth;
                dest[ny * NormalizedWidth + nx] = ink[sy * width + sx] ? (byte)255 : (byte)0;
            }
        }

        return dest;
    }
}
