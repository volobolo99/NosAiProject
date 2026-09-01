using System.Buffers.Binary;
using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The bar reader against pixels from the real client, not against bars this
/// project painted for itself.
/// </summary>
/// <remarks>
/// <para>
/// Every other test of <see cref="HudBarFillReader"/> paints a flat rectangle and
/// asks the reader to measure it, which is why the reader could be green through
/// its whole suite and still refuse the actual HUD: nothing painted here has a
/// gradient, and nothing painted here has the current and maximum written across
/// the middle of it in white.
/// </para>
/// <para>
/// The fixtures are the crops T-03 produced on 1 Sep 2026 with the ROI on the
/// client window. Their contents are independently known — the world channel
/// reported <c>stat 7305 7305 1420 1420</c> in the same session
/// (<c>docs/PROTOCOLLO_NOSTALE.md</c>) — so a full bar is the correct reading and
/// not merely the one the reader happens to produce.
/// </para>
/// </remarks>
public sealed class HudBarOnRealClientTests
{
    [Fact]
    public void Real_hp_bar_reads_full_instead_of_refusing()
    {
        (byte[] bgra, int width, int height) = LoadFixture("nostale_hp_full_7305.bmp");

        HudBarMeasure measure = HudBarFillReader.Measure(bgra, width, height, HudFillHue.RedOrGreen);

        Assert.Null(measure.FailureReason);
        Assert.NotNull(measure.Ratio);
        // 7305/7305. The bar's own right-hand border costs the last column, so a
        // full bar measures a hair under one rather than exactly one.
        Assert.InRange(measure.Ratio!.Value, 0.97, 1.0);
        Assert.True(measure.Confidence >= ScreenDerivedBarGate.MinConfidence);
    }

    [Fact]
    public void Real_mp_bar_reads_full_instead_of_refusing()
    {
        (byte[] bgra, int width, int height) = LoadFixture("nostale_mp_full_1420.bmp");

        HudBarMeasure measure = HudBarFillReader.Measure(bgra, width, height, HudFillHue.Blue);

        Assert.Null(measure.FailureReason);
        Assert.NotNull(measure.Ratio);
        Assert.InRange(measure.Ratio!.Value, 0.97, 1.0);
        Assert.True(measure.Confidence >= ScreenDerivedBarGate.MinConfidence);
    }

    [Fact]
    public void Real_bars_publish_derived_through_the_gate()
    {
        (byte[] bgra, int width, int height) = LoadFixture("nostale_hp_full_7305.bmp");
        HudBarMeasure measure = HudBarFillReader.Measure(bgra, width, height, HudFillHue.RedOrGreen);

        ScreenBarFill published = new ScreenDerivedBarGate().Publish(measure.Ratio, measure.Confidence, null);

        Assert.True(published.Ratio.HasValue);
        Assert.Equal(NosAi.Runtime.Contracts.DataSourceKind.Derived, published.Ratio.Source);
    }

    [Fact]
    public void Real_hp_bar_is_not_read_as_mp_and_the_reverse()
    {
        (byte[] hp, int hpWidth, int hpHeight) = LoadFixture("nostale_hp_full_7305.bmp");
        (byte[] mp, int mpWidth, int mpHeight) = LoadFixture("nostale_mp_full_1420.bmp");

        Assert.NotNull(HudBarFillReader.Measure(hp, hpWidth, hpHeight, HudFillHue.Blue).FailureReason);
        Assert.NotNull(HudBarFillReader.Measure(mp, mpWidth, mpHeight, HudFillHue.RedOrGreen).FailureReason);
    }

    /// <summary>
    /// The numerals are what the previous model choked on, so the fact that they
    /// are still there — and still extractable as glyphs — is worth asserting
    /// rather than inferring from the bar reading.
    /// </summary>
    [Fact]
    public void Real_hp_crop_still_carries_the_numerals_as_glyphs()
    {
        (byte[] bgra, int width, int height) = LoadFixture("nostale_hp_full_7305.bmp");

        IReadOnlyList<byte[]> glyphs = HudGlyphExtractor.Extract(bgra, width, height);

        // "7305/7305" is nine glyphs. Extraction splits on ink columns, so a pair
        // of numerals printed without a gap between them would merge; the count is
        // asserted as a range rather than exactly nine so this test reports the
        // bar reading it exists for and not the extractor's kerning.
        Assert.InRange(glyphs.Count, 7, 9);
    }

    /// <summary>
    /// Reads one of the 32-bit BMPs <see cref="HudCropWriter"/> writes, back into
    /// the top-down BGRA the reader takes. Hand-rolled for the same reason the
    /// writer is: the format is a 54-byte header over the bytes already held.
    /// </summary>
    private static (byte[] Bgra, int Width, int Height) LoadFixture(string name)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        Assert.True(File.Exists(path), $"Fixture missing: {path}");

        byte[] file = File.ReadAllBytes(path);
        Assert.True(file.Length > 54, "Fixture is shorter than a BMP header.");
        Assert.Equal((byte)'B', file[0]);
        Assert.Equal((byte)'M', file[1]);

        int offset = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(10));
        int width = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(18));
        int height = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(22));
        short bits = BinaryPrimitives.ReadInt16LittleEndian(file.AsSpan(28));
        Assert.Equal(32, bits);

        int stride = width * 4;
        var bgra = new byte[stride * height];
        // BMP rows with a positive height are stored bottom-up.
        for (int y = 0; y < height; y++)
            file.AsSpan(offset + (height - 1 - y) * stride, stride).CopyTo(bgra.AsSpan(y * stride, stride));

        return (bgra, width, height);
    }
}
