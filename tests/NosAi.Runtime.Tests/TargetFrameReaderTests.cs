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

    /// <summary>
    /// The invariant this whole reader exists for, and the reason the name breaks
    /// the file's convention: <c>scripts/verifica-obiettivo.ps1</c> searches for it
    /// literally to know that C1 was done.
    /// </summary>
    /// <remarks>
    /// Noise is pixels that say nothing, and "no target" is a statement. Returning
    /// Absent here would tell the planner the target is gone, and ADR-0016 would
    /// send the character walking to a waypoint in the middle of a fight.
    /// </remarks>
    [Fact]
    public void NoiseIsUnreadableNotAbsent()
    {
        byte[] bgra = Checkerboard(Width, Height);

        TargetFrameReading reading = TargetFrameReader.Read(bgra, Width, Height);

        AssertUnreadable(reading);
        Assert.NotEqual(TargetFrameState.Absent, reading.State);
    }

    // ------------------------------------------------- the shape of every answer

    /// <summary>
    /// Acceptance criterion 2, as a biconditional rather than as two examples: a
    /// reason means the read failed, and a failed read always says why. A state
    /// that carried both an answer and an excuse would let a caller believe
    /// whichever it looked at first.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryKindOfInput))]
    public void A_reason_is_present_exactly_when_the_reading_is_unreadable(
        byte[] bgra, int width, int height)
    {
        TargetFrameReading reading = TargetFrameReader.Read(bgra, width, height);

        Assert.Equal(
            reading.State == TargetFrameState.Unreadable,
            !string.IsNullOrWhiteSpace(reading.FailureReason));
    }

    /// <summary>
    /// Acceptance criterion 3. A ratio outside Present would be a health nobody
    /// measured, and one outside 0..1 is a measurement of something that is not a
    /// bar.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryKindOfInput))]
    public void A_ratio_is_present_exactly_when_the_frame_is(byte[] bgra, int width, int height)
    {
        TargetFrameReading reading = TargetFrameReader.Read(bgra, width, height);

        Assert.Equal(reading.State == TargetFrameState.Present, reading.HpRatio.HasValue);
        if (reading.HpRatio is { } ratio)
            Assert.InRange(ratio, 0.0, 1.0);
    }

    /// <summary>
    /// Acceptance criterion 1, including the input that used to reach the pixels.
    /// <c>width * height * 4</c> in <c>int</c> arithmetic overflows to zero for a
    /// width of 2^29 and a height of 2, so an empty buffer matched the expected
    /// length and was handed to the bar reader, which indexed it and threw. The
    /// size check has to be done in arithmetic that cannot wrap, or the guarantee
    /// is only true of inputs nobody thought to try.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryKindOfInput))]
    public void Read_never_throws_whatever_it_is_given(byte[] bgra, int width, int height)
    {
        TargetFrameReading reading = TargetFrameReader.Read(bgra, width, height);

        Assert.True(Enum.IsDefined(reading.State));
    }

    public static TheoryData<byte[], int, int> EveryKindOfInput() => new()
    {
        { SolidBar(Width, Height, fillThrough: Width), Width, Height },
        { SolidBar(Width, Height, fillThrough: Width / 2), Width, Height },
        { new byte[Width * Height * 4], Width, Height },
        { Checkerboard(Width, Height), Width, Height },
        { new byte[Width * Height * 4 - 1], Width, Height },
        { new byte[3 * Height * 4], 3, Height },
        { Array.Empty<byte>(), 0, 0 },
        { Array.Empty<byte>(), -1, -1 },
        { new byte[Width * Height * 4], Width, 1 },
        // The overflow: 2^29 * 2 * 4 wraps to zero in int arithmetic.
        { Array.Empty<byte>(), 1 << 29, 2 },
    };

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
