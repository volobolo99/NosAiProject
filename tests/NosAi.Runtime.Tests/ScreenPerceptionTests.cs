using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.Runtime.Tests;

public sealed class ScreenPerceptionTests
{
    [Fact]
    public void Half_red_bar_is_derived_near_half()
    {
        var crop = PaintBar(80, 8, fillColumns: 40, r: 220, g: 30, b: 30);
        var measure = HudBarFillReader.Measure(crop, 80, 8, HudFillHue.RedOrGreen);
        Assert.Null(measure.FailureReason);
        Assert.NotNull(measure.Ratio);
        Assert.InRange(measure.Ratio!.Value, 0.45, 0.55);
        Assert.True(measure.Confidence >= ScreenDerivedBarGate.MinConfidence);
    }

    [Fact]
    public void Full_red_bar_is_derived_one()
    {
        var crop = PaintBar(80, 8, fillColumns: 80, r: 220, g: 30, b: 30);
        var measure = HudBarFillReader.Measure(crop, 80, 8, HudFillHue.RedOrGreen);
        Assert.Equal(1.0, measure.Ratio);
        Assert.Null(measure.FailureReason);
    }

    [Fact]
    public void Dark_crop_is_unknown_not_zero()
    {
        var crop = PaintBar(80, 8, fillColumns: 0, r: 20, g: 20, b: 20);
        var measure = HudBarFillReader.Measure(crop, 80, 8, HudFillHue.RedOrGreen);
        Assert.Null(measure.Ratio);
        Assert.Equal("no_bar_signature", measure.FailureReason);
    }

    [Fact]
    public void Checkerboard_is_unknown()
    {
        var crop = new byte[80 * 8 * 4];
        for (var y = 0; y < 8; y++)
        for (var x = 0; x < 80; x++)
        {
            var i = (y * 80 + x) * 4;
            var on = ((x + y) & 1) == 0;
            crop[i] = 20;
            crop[i + 1] = on ? (byte)30 : (byte)20;
            crop[i + 2] = on ? (byte)220 : (byte)20;
            crop[i + 3] = 255;
        }

        var measure = HudBarFillReader.Measure(crop, 80, 8, HudFillHue.RedOrGreen);
        Assert.Null(measure.Ratio);
        Assert.Equal("noisy_bar_profile", measure.FailureReason);
    }

    [Fact]
    public void Blue_bar_is_ignored_as_hp()
    {
        var crop = PaintBar(80, 8, fillColumns: 40, r: 30, g: 40, b: 220);
        var asHp = HudBarFillReader.Measure(crop, 80, 8, HudFillHue.RedOrGreen);
        Assert.Equal("no_bar_signature", asHp.FailureReason);

        var asMp = HudBarFillReader.Measure(crop, 80, 8, HudFillHue.Blue);
        Assert.NotNull(asMp.Ratio);
        Assert.InRange(asMp.Ratio!.Value, 0.45, 0.55);
    }

    [Fact]
    public void Bar_gate_never_labels_live()
    {
        var gate = new ScreenDerivedBarGate();
        var published = gate.Publish(0.5, 0.90, null);
        Assert.Equal(DataSourceKind.Derived, published.Ratio.Source);
        Assert.NotEqual(DataSourceKind.Live, published.Ratio.Source);
    }

    [Fact]
    public void Bar_jump_is_unknown()
    {
        var gate = new ScreenDerivedBarGate();
        var previous = gate.Publish(0.90, 0.90, null);
        var next = gate.Publish(0.20, 0.90, previous);
        Assert.Equal("continuity_jump_rejected", next.FailureReason);
        Assert.False(next.Ratio.HasValue);
    }

    [Fact]
    public void Glyph_extractor_finds_white_column_and_ignores_red_bar()
    {
        var crop = PaintBar(60, 12, fillColumns: 60, r: 220, g: 30, b: 30);
        PaintWhiteRect(crop, 60, 12, 8, 2, 4, 8);
        var glyphs = HudGlyphExtractor.Extract(crop, 60, 12);
        Assert.Single(glyphs);
        Assert.Equal(HudGlyphExtractor.NormalizedWidth * HudGlyphExtractor.NormalizedHeight, glyphs[0].Length);
    }

    [Fact]
    public void Solid_bar_without_text_is_not_a_glyph_string()
    {
        var crop = PaintBar(60, 12, fillColumns: 60, r: 220, g: 30, b: 30);
        Assert.Empty(HudGlyphExtractor.Extract(crop, 60, 12));
    }

    [Fact]
    public void Current_max_text_parses_and_rejects_bare_number()
    {
        Assert.True(ScreenVitalReader.TryParseCurrentMax("412/800", out var current, out var max));
        Assert.Equal(412, current);
        Assert.Equal(800, max);
        Assert.False(ScreenVitalReader.TryParseCurrentMax("412", out _, out _));
    }

    [Fact]
    public void Reader_on_painted_hud_derives_bar_and_keeps_numbers_unknown()
    {
        var frame = PaintHudFrame(1920, 1080, hpFill: 0.5, mpFill: 0.25);
        var observation = new ScreenVitalReader().Read(frame);

        Assert.True(observation.HpBar.Ratio.HasValue);
        Assert.Equal(DataSourceKind.Derived, observation.HpBar.Ratio.Source);
        Assert.InRange(observation.HpBar.Ratio.Value, 0.40, 0.60);
        Assert.True(observation.MpBar.Ratio.HasValue);
        Assert.InRange(observation.MpBar.Ratio.Value, 0.15, 0.35);
        Assert.False(observation.Hp.Current.HasValue);
        Assert.Equal("ocr_glyphs_not_trained", observation.Hp.FailureReason);
        Assert.Equal(0, observation.TrainedGlyphs);
    }

    [Fact]
    public void Trained_ocr_publishes_derived_numbers()
    {
        var slash = new byte[] { 1, 2, 3 };
        var four = new byte[] { 4, 4, 4 };
        var one = new byte[] { 1, 1, 1 };
        var two = new byte[] { 2, 2, 2 };
        var eight = new byte[] { 8, 8, 8 };
        var zero = new byte[] { 0, 9, 0 };
        var ocr = new GlyphHashOcrCache();
        ocr.Train('/', slash);
        ocr.Train('4', four);
        ocr.Train('1', one);
        ocr.Train('2', two);
        ocr.Train('8', eight);
        ocr.Train('0', zero);

        Assert.Equal(412, ocr.RecognizeInteger([four, one, two]));
        Assert.True(ScreenVitalReader.TryParseCurrentMax(ocr.Recognize([four, one, two, slash, eight, zero, zero]), out var current, out var max));
        Assert.Equal(412, current);
        Assert.Equal(800, max);

        var gate = new ScreenDerivedVitalGate();
        var published = gate.Publish(current, max, 0.90, null);
        Assert.Equal(DataSourceKind.Derived, published.Current.Source);
    }

    private static byte[] PaintBar(int width, int height, int fillColumns, byte r, byte g, byte b)
    {
        var bgra = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var i = (y * width + x) * 4;
            var filled = x < fillColumns;
            bgra[i] = filled ? b : (byte)24;
            bgra[i + 1] = filled ? g : (byte)24;
            bgra[i + 2] = filled ? r : (byte)24;
            bgra[i + 3] = 255;
        }

        return bgra;
    }

    private static void PaintWhiteRect(byte[] bgra, int width, int height, int x0, int y0, int w, int h)
    {
        for (var y = y0; y < y0 + h && y < height; y++)
        for (var x = x0; x < x0 + w && x < width; x++)
        {
            var i = (y * width + x) * 4;
            bgra[i] = 240;
            bgra[i + 1] = 240;
            bgra[i + 2] = 240;
            bgra[i + 3] = 255;
        }
    }

    private static CaptureFrame PaintHudFrame(int width, int height, double hpFill, double mpFill)
    {
        var bgra = new byte[width * height * 4];
        for (var i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = 16;
            bgra[i + 1] = 16;
            bgra[i + 2] = 16;
            bgra[i + 3] = 255;
        }

        var regions = RoiSegmenter.Segment(width, height);
        foreach (var region in regions)
        {
            if (region.Kind == RoiKind.PlayerHpBar)
                PaintRoi(bgra, width, region.Rect, hpFill, 220, 30, 30);
            else if (region.Kind == RoiKind.PlayerMpBar)
                PaintRoi(bgra, width, region.Rect, mpFill, 30, 40, 220);
        }

        return new CaptureFrame(width, height, bgra, DataSourceKind.Simulated, DateTime.UtcNow);
    }

    private static void PaintRoi(byte[] bgra, int frameWidth, PixelRect rect, double fill, byte r, byte g, byte b)
    {
        var fillColumns = (int)Math.Round(rect.Width * fill);
        for (var y = 0; y < rect.Height; y++)
        for (var x = 0; x < rect.Width; x++)
        {
            var i = ((rect.Y + y) * frameWidth + rect.X + x) * 4;
            var filled = x < fillColumns;
            bgra[i] = filled ? b : (byte)24;
            bgra[i + 1] = filled ? g : (byte)24;
            bgra[i + 2] = filled ? r : (byte)24;
            bgra[i + 3] = 255;
        }
    }
}
