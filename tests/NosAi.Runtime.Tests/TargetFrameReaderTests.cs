using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.Runtime.Tests;

public sealed class TargetFrameReaderTests
{
    private const int Width = 40;
    private const int Height = 8;
    private const double HalfBarTolerance = 0.02;

    [Fact]
    public void Full_bar_is_present_at_ratio_one()
    {
        byte[] bgra = SolidBar(Width, Height, fillThrough: Width);

        TargetFrameReading reading = TargetFrameReader.Read(bgra, Width, Height);

        AssertPresent(reading);
        Assert.Equal(1.0, reading.HpRatio!.Value, precision: 2);
    }

    [Fact]
    public void Half_bar_is_present_at_ratio_one_half()
    {
        byte[] bgra = SolidBar(Width, Height, fillThrough: Width / 2);

        TargetFrameReading reading = TargetFrameReader.Read(bgra, Width, Height);

        AssertPresent(reading);
        Assert.InRange(reading.HpRatio!.Value, 0.5 - HalfBarTolerance, 0.5 + HalfBarTolerance);
    }

    [Fact]
    public void Black_region_is_absent_not_unreadable()
    {
        byte[] bgra = new byte[Width * Height * 4];

        TargetFrameReading reading = TargetFrameReader.Read(bgra, Width, Height);

        Assert.Equal(TargetFrameState.Absent, reading.State);
        Assert.Null(reading.HpRatio);
        Assert.Null(reading.FailureReason);
    }

    [Fact]
    public void Truncated_buffer_is_unreadable_without_reading_pixels()
    {
        byte[] bgra = new byte[Width * Height * 4 - 1];

        TargetFrameReading reading = TargetFrameReader.Read(bgra, Width, Height);

        AssertUnreadable(reading);
    }

    [Fact]
    public void Width_below_minimum_is_unreadable()
    {
        const int width = 3;
        byte[] bgra = new byte[width * Height * 4];

        TargetFrameReading reading = TargetFrameReader.Read(bgra, width, Height);

        AssertUnreadable(reading);
        Assert.Equal("crop_too_small", reading.FailureReason);
    }

    [Fact]
    public void Speckle_noise_is_unreadable_not_the_absence_of_a_target()
    {
        byte[] bgra = Checkerboard(Width, Height);

        TargetFrameReading reading = TargetFrameReader.Read(bgra, Width, Height);

        AssertUnreadable(reading);
        Assert.NotEqual(TargetFrameState.Absent, reading.State);
    }

    private static void AssertPresent(TargetFrameReading reading)
    {
        Assert.Equal(TargetFrameState.Present, reading.State);
        Assert.Null(reading.FailureReason);
        Assert.NotNull(reading.HpRatio);
        Assert.InRange(reading.HpRatio!.Value, 0.0, 1.0);
    }

    private static void AssertUnreadable(TargetFrameReading reading)
    {
        Assert.Equal(TargetFrameState.Unreadable, reading.State);
        Assert.False(string.IsNullOrWhiteSpace(reading.FailureReason));
        Assert.Null(reading.HpRatio);
    }

    private static byte[] SolidBar(int width, int height, int fillThrough)
    {
        var bgra = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < fillThrough; x++)
                WriteFill(bgra, width, x, y);
        }

        return bgra;
    }

    /// <summary>
    /// Alternating fill every row: four vertical runs in an eight-row crop, which
    /// is speckle to <see cref="HudBarFillReader"/>, not an empty target frame.
    /// </summary>
    private static byte[] Checkerboard(int width, int height)
    {
        var bgra = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (((x + y) & 1) == 0)
                    WriteFill(bgra, width, x, y);
            }
        }

        return bgra;
    }

    private static void WriteFill(byte[] bgra, int width, int x, int y)
    {
        var i = (y * width + x) * 4;
        bgra[i] = 0;
        bgra[i + 1] = 200;
        bgra[i + 2] = 40;
        bgra[i + 3] = 255;
    }
}
