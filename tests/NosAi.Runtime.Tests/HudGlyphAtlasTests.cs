using System.Buffers.Binary;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The screen reader learning to read numbers, on the real client's numerals.
/// </summary>
/// <remarks>
/// Every screen reading this project has produced ended
/// <c>ocr_glyphs_not_trained</c>, because nothing ever taught the recogniser a
/// glyph. These tests are the training pass and the reading that follows it, both
/// over the crop T-03 took from the live HUD.
/// </remarks>
public sealed class HudGlyphAtlasTests
{
    /// <summary>
    /// What the world channel reported for this same session
    /// (<c>docs/PROTOCOLLO_NOSTALE.md</c>), and therefore what the numerals in the
    /// fixture say.
    /// </summary>
    private const int WireHp = 7305;
    private const int WireMaxHp = 7305;

    [Fact]
    public void Atlas_learns_the_real_numerals_from_the_wire_reading()
    {
        var atlas = new HudGlyphAtlas();
        (byte[] crop, int width, int height) = Fixture("nostale_hp_full_7305.bmp");

        HudGlyphTrainingResult result = atlas.Train(
            HudGlyphExtractor.Extract(crop, width, height),
            HudGlyphTraining.FormatVitalText(WireHp, WireMaxHp));

        Assert.True(result.Succeeded, result.FailureReason);
        // "7305/7305" is five distinct characters; the repeats hash to their own
        // entries because the client renders them at different sub-pixel offsets,
        // which is why the atlas keys on bitmaps and not on characters.
        Assert.True(atlas.Count >= 5, $"atlas learned only {atlas.Count} glyphs");
        Assert.Equal(['/', '0', '3', '5', '7'], atlas.KnownCharacters);
    }

    [Fact]
    public void Trained_reader_publishes_the_real_hp_as_derived_numbers()
    {
        CaptureFrame frame = FrameWithHpCrop();
        var atlas = new HudGlyphAtlas();

        HudGlyphTrainingResult training = HudGlyphTraining.TrainHpFromObservedVitals(
            atlas,
            frame,
            ClassifiedValue<int>.Live(WireHp, DateTime.UtcNow),
            ClassifiedValue<int>.Live(WireMaxHp, DateTime.UtcNow));
        Assert.True(training.Succeeded, training.FailureReason);

        ScreenVitalObservation observation = new ScreenVitalReader(atlas.ToOcrCache()).Read(frame);

        Assert.Null(observation.Hp.FailureReason);
        Assert.Equal(WireHp, observation.Hp.Current.Value);
        Assert.Equal(WireMaxHp, observation.Hp.Maximum.Value);
        // Off the screen, never LIVE (ADR-0012).
        Assert.Equal(DataSourceKind.Derived, observation.Hp.Current.Source);
    }

    [Fact]
    public void Untrained_reader_still_says_so_rather_than_guessing()
    {
        ScreenVitalObservation observation = new ScreenVitalReader().Read(FrameWithHpCrop());

        Assert.False(observation.Hp.Current.HasValue);
        Assert.Equal("ocr_glyphs_not_trained", observation.Hp.FailureReason);
    }

    [Fact]
    public void A_label_that_was_not_read_live_is_refused()
    {
        CaptureFrame frame = FrameWithHpCrop();

        // Simulated: not a reading of this HUD at all.
        Assert.StartsWith("label_not_live", HudGlyphTraining.TrainHpFromObservedVitals(
            new HudGlyphAtlas(), frame,
            ClassifiedValue<int>.Simulated(WireHp), ClassifiedValue<int>.Simulated(WireMaxHp)).FailureReason);

        // Derived: what the screen reader itself publishes. Training on it would
        // teach the screen to agree with the screen.
        Assert.StartsWith("label_not_live", HudGlyphTraining.TrainHpFromObservedVitals(
            new HudGlyphAtlas(), frame,
            ClassifiedValue<int>.Derived(WireHp, DateTime.UtcNow),
            ClassifiedValue<int>.Derived(WireMaxHp, DateTime.UtcNow)).FailureReason);

        // Unknown: there is no label.
        Assert.StartsWith("label_not_observed", HudGlyphTraining.TrainHpFromObservedVitals(
            new HudGlyphAtlas(), frame,
            ClassifiedValue<int>.Unknown("stat_not_seen"),
            ClassifiedValue<int>.Unknown("stat_not_seen")).FailureReason);
    }

    [Fact]
    public void A_label_that_does_not_fit_the_glyphs_teaches_nothing()
    {
        var atlas = new HudGlyphAtlas();
        (byte[] crop, int width, int height) = Fixture("nostale_hp_full_7305.bmp");
        IReadOnlyList<byte[]> glyphs = HudGlyphExtractor.Extract(crop, width, height);

        HudGlyphTrainingResult result = atlas.Train(glyphs, "1/2");

        Assert.False(result.Succeeded);
        Assert.StartsWith("glyph_count_mismatch", result.FailureReason);
        Assert.Equal(0, atlas.Count);
    }

    /// <summary>
    /// The measured case the count guard exists for: on the real client
    /// <c>1420/1420</c> prints its <c>0</c> and <c>/</c> with no gap, so the
    /// extractor yields eight bitmaps for nine characters.
    /// </summary>
    [Fact]
    public void Merged_numerals_are_refused_rather_than_paired_by_position()
    {
        var atlas = new HudGlyphAtlas();
        (byte[] crop, int width, int height) = Fixture("nostale_mp_full_1420.bmp");

        HudGlyphTrainingResult result = atlas.Train(
            HudGlyphExtractor.Extract(crop, width, height),
            HudGlyphTraining.FormatVitalText(1420, 1420));

        Assert.False(result.Succeeded);
        Assert.Equal("glyph_count_mismatch:8_for_9_characters", result.FailureReason);
        Assert.Equal(0, atlas.Count);
    }

    [Fact]
    public void A_contradicting_lesson_leaves_the_atlas_untouched()
    {
        var atlas = new HudGlyphAtlas();
        (byte[] crop, int width, int height) = Fixture("nostale_hp_full_7305.bmp");
        IReadOnlyList<byte[]> glyphs = HudGlyphExtractor.Extract(crop, width, height);
        Assert.True(atlas.Train(glyphs, "7305/7305").Succeeded);
        int learned = atlas.Count;

        HudGlyphTrainingResult second = atlas.Train(glyphs, "1305/7305");

        Assert.StartsWith("glyph_conflicts_with_atlas", second.FailureReason);
        Assert.Equal(learned, atlas.Count);
    }

    [Fact]
    public void Atlas_round_trips_through_its_file()
    {
        var atlas = new HudGlyphAtlas();
        (byte[] crop, int width, int height) = Fixture("nostale_hp_full_7305.bmp");
        Assert.True(atlas.Train(HudGlyphExtractor.Extract(crop, width, height), "7305/7305").Succeeded);

        string path = Path.Combine(Path.GetTempPath(), $"nosai-atlas-{Guid.NewGuid():N}.atlas");
        try
        {
            atlas.Save(path);
            HudGlyphAtlas reloaded = HudGlyphAtlas.Load(path, out string? failure);

            Assert.Null(failure);
            Assert.Equal(atlas.Count, reloaded.Count);
            Assert.Equal(atlas.KnownCharacters, reloaded.KnownCharacters);
            Assert.Equal("7305/7305", reloaded.ToOcrCache().Recognize(HudGlyphExtractor.Extract(crop, width, height)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_missing_atlas_is_reported_as_untrained_not_as_broken()
    {
        HudGlyphAtlas atlas = HudGlyphAtlas.Load(
            Path.Combine(Path.GetTempPath(), $"nosai-absent-{Guid.NewGuid():N}.atlas"), out string? failure);

        Assert.Equal("atlas_not_trained_yet", failure);
        Assert.Equal(0, atlas.Count);
    }

    [Fact]
    public void An_atlas_hashed_under_a_different_normalisation_is_refused()
    {
        string path = Path.Combine(Path.GetTempPath(), $"nosai-atlas-{Guid.NewGuid():N}.atlas");
        try
        {
            File.WriteAllText(path, "nosai-glyph-atlas 1 99 99\n0000000000000001 7\n");

            HudGlyphAtlas atlas = HudGlyphAtlas.Load(path, out string? failure);

            Assert.Equal("atlas_normalisation_changed:99x99", failure);
            Assert.Equal(0, atlas.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// A frame whose HP region is exactly the fixture crop.
    /// </summary>
    /// <remarks>
    /// Sized so <see cref="RoiSegmenter"/>'s measured fractions land on a
    /// 124x12 region — the size the real client produced — so the test exercises
    /// segmentation, cropping and extraction rather than being handed the crop.
    /// </remarks>
    private static CaptureFrame FrameWithHpCrop()
    {
        const int frameWidth = 1025;
        const int frameHeight = 800;
        (byte[] crop, int cropWidth, int cropHeight) = Fixture("nostale_hp_full_7305.bmp");

        PixelRect roi = RoiSegmenter.Segment(frameWidth, frameHeight)
            .First(region => region.Kind == RoiKind.PlayerHpBar).Rect;
        Assert.Equal(cropWidth, roi.Width);
        Assert.Equal(cropHeight, roi.Height);

        var bgra = new byte[frameWidth * frameHeight * 4];
        for (int i = 3; i < bgra.Length; i += 4)
            bgra[i] = 255;

        for (int y = 0; y < cropHeight; y++)
        {
            crop.AsSpan(y * cropWidth * 4, cropWidth * 4)
                .CopyTo(bgra.AsSpan(((roi.Y + y) * frameWidth + roi.X) * 4, cropWidth * 4));
        }

        return new CaptureFrame(frameWidth, frameHeight, bgra, DataSourceKind.Simulated, DateTime.UtcNow);
    }

    private static (byte[] Bgra, int Width, int Height) Fixture(string name)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        Assert.True(File.Exists(path), $"Fixture missing: {path}");

        byte[] file = File.ReadAllBytes(path);
        int offset = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(10));
        int width = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(18));
        int height = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(22));

        int stride = width * 4;
        var bgra = new byte[stride * height];
        for (int y = 0; y < height; y++)
            file.AsSpan(offset + (height - 1 - y) * stride, stride).CopyTo(bgra.AsSpan(y * stride, stride));

        return (bgra, width, height);
    }
}
