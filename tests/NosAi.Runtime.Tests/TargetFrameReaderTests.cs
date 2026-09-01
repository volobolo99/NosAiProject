using System;
using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The reader that lets the runtime know whether it has a target.
/// </summary>
/// <remarks>
/// The property under test throughout is the one ADR-0016 turns on: an
/// unreadable region must never be reported as an absent target. Reporting it
/// that way makes the planner run the exploration rule — walking the character to
/// a waypoint — on the strength of pixels nobody could read.
/// </remarks>
public sealed class TargetFrameReaderTests
{
    // Wide enough to clear HudBarFillReader.MinWidth, tall enough for a run of
    // ink to be splittable, and small enough to build by hand in a test.
    private const int Width = 32;
    private const int Height = 8;

    /// <summary>
    /// A pixel in the bar's colour family: green leads, blue is well behind.
    /// </summary>
    private static readonly (byte R, byte G, byte B) Fill = (100, 200, 30);

    [Fact]
    public void AFullBarIsPresentAtFullRatio()
    {
        TargetFrameReading reading = TargetFrameReader.Read(BarOf(Width), Width, Height);

        Assert.Equal(TargetFrameState.Present, reading.State);
        Assert.Equal(1.0, reading.HpRatio!.Value, 3);
        Assert.Null(reading.FailureReason);
    }

    [Fact]
    public void AHalfBarIsPresentAtHalfRatio()
    {
        TargetFrameReading reading = TargetFrameReader.Read(BarOf(Width / 2), Width, Height);

        Assert.Equal(TargetFrameState.Present, reading.State);
        // The edge is measured by its right-hand column, so sixteen filled columns
        // of thirty-two read as exactly one half.
        Assert.Equal(0.5, reading.HpRatio!.Value, 3);
    }

    /// <summary>
    /// The region is readable and holds no bar. This is the answer that makes the
    /// exploration rule plannable at all, and it is why the reader cannot simply
    /// refuse everything it is unsure of.
    /// </summary>
    [Fact]
    public void AnEmptyRegionIsAbsentWithNoFailure()
    {
        TargetFrameReading reading = TargetFrameReader.Read(Black(), Width, Height);

        Assert.Equal(TargetFrameState.Absent, reading.State);
        Assert.Null(reading.HpRatio);
        Assert.Null(reading.FailureReason);
    }

    /// <summary>
    /// The binding test for card C1.
    /// </summary>
    /// <remarks>
    /// Speckle is what the region looks like when it frames something that is not
    /// the HUD — foliage, a dithered background, the wrong part of the window
    /// while the ROI proportions stay uncalibrated. Reading that as an absent
    /// target would report "there is nothing to fight" with full confidence while
    /// pointed somewhere else entirely, and ADR-0016 would act on it.
    /// </remarks>
    [Fact]
    public void NoiseIsUnreadableNotAbsent()
    {
        TargetFrameReading reading = TargetFrameReader.Read(Speckle(), Width, Height);

        Assert.Equal(TargetFrameState.Unreadable, reading.State);
        Assert.NotEqual(TargetFrameState.Absent, reading.State);
        Assert.False(string.IsNullOrWhiteSpace(reading.FailureReason));
        Assert.Null(reading.HpRatio);
    }

    [Fact]
    public void ATruncatedBufferIsUnreadable()
    {
        byte[] full = BarOf(Width);
        var truncated = new byte[full.Length - 4];
        Array.Copy(full, truncated, truncated.Length);

        TargetFrameReading reading = TargetFrameReader.Read(truncated, Width, Height);

        Assert.Equal(TargetFrameState.Unreadable, reading.State);
        Assert.False(string.IsNullOrWhiteSpace(reading.FailureReason));
    }

    [Fact]
    public void ARegionTooSmallToHoldABarIsUnreadable()
    {
        const int narrow = 3;
        TargetFrameReading reading = TargetFrameReader.Read(new byte[narrow * Height * 4], narrow, Height);

        Assert.Equal(TargetFrameState.Unreadable, reading.State);
        Assert.False(string.IsNullOrWhiteSpace(reading.FailureReason));
    }

    /// <summary>
    /// The three fields agree with the state, on every input the other tests
    /// exercise. Checked as one property because a caller reads the state and the
    /// ratio together, and a ratio that outlived its state would be believed.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryShape))]
    public void RatioAndReasonBelongToTheirOwnStates(byte[] pixels, int width, int height)
    {
        TargetFrameReading reading = TargetFrameReader.Read(pixels, width, height);

        Assert.Equal(reading.State == TargetFrameState.Present, reading.HpRatio.HasValue);
        Assert.Equal(reading.State == TargetFrameState.Unreadable, reading.FailureReason is not null);

        if (reading.HpRatio is { } ratio)
        {
            Assert.InRange(ratio, 0.0, 1.0);
        }
    }

    public static TheoryData<byte[], int, int> EveryShape() => new()
    {
        { BarOf(Width), Width, Height },
        { BarOf(Width / 2), Width, Height },
        { BarOf(1), Width, Height },
        { Black(), Width, Height },
        { Speckle(), Width, Height },
        { new byte[4], Width, Height },
        { Array.Empty<byte>(), 0, 0 },
        { new byte[3 * Height * 4], 3, Height },
    };

    /// <summary>No input throws: a malformed region is an answer, not an exception.</summary>
    [Theory]
    [MemberData(nameof(EveryShape))]
    public void ReadNeverThrows(byte[] pixels, int width, int height)
    {
        TargetFrameReading reading = TargetFrameReader.Read(pixels, width, height);
        Assert.True(Enum.IsDefined(reading.State));
    }

    // -- costruzione dei pixel --------------------------------------------------

    /// <summary>A bar filled for the first <paramref name="filledColumns"/> columns.</summary>
    private static byte[] BarOf(int filledColumns)
    {
        var pixels = new byte[Width * Height * 4];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < filledColumns; x++)
            {
                Set(pixels, x, y, Fill.R, Fill.G, Fill.B);
            }
        }
        return pixels;
    }

    private static byte[] Black() => new byte[Width * Height * 4];

    /// <summary>
    /// Family pixels on alternating rows, so every column breaks into four runs —
    /// past HudBarFillReader.MaxRunsPerColumn, which is what dithering and foliage
    /// do and a bar never does.
    /// </summary>
    private static byte[] Speckle()
    {
        var pixels = new byte[Width * Height * 4];
        for (var y = 0; y < Height; y += 2)
        {
            for (var x = 0; x < Width; x++)
            {
                Set(pixels, x, y, Fill.R, Fill.G, Fill.B);
            }
        }
        return pixels;
    }

    private static void Set(byte[] pixels, int x, int y, byte r, byte g, byte b)
    {
        int i = (y * Width + x) * 4;
        pixels[i] = b;
        pixels[i + 1] = g;
        pixels[i + 2] = r;
        pixels[i + 3] = 255;
    }
}
