using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.Runtime.Tests;

public sealed class DialogWindowReaderTests
{
    private static byte[] FlatColor(int width, int height, byte b, byte g, byte r)
    {
        var bgra = new byte[width * height * 4];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            bgra[i] = b;
            bgra[i + 1] = g;
            bgra[i + 2] = r;
            bgra[i + 3] = 255;
        }

        return bgra;
    }

    [Fact]
    public void A_crop_smaller_than_the_minimum_is_unreadable()
    {
        byte[] tiny = FlatColor(2, 2, 10, 10, 10);

        DialogWindowReading reading = DialogWindowReader.Read(tiny, 2, 2, 10, 10, 10);

        Assert.Equal(DialogWindowState.Unreadable, reading.State);
        Assert.Equal("crop_too_small", reading.FailureReason);
    }

    [Fact]
    public void A_buffer_that_does_not_match_the_declared_dimensions_is_unreadable()
    {
        byte[] wrongLength = FlatColor(4, 4, 10, 10, 10);

        DialogWindowReading reading = DialogWindowReader.Read(wrongLength, 8, 8, 10, 10, 10);

        Assert.Equal(DialogWindowState.Unreadable, reading.State);
        Assert.Equal("crop_truncated", reading.FailureReason);
    }

    [Fact]
    public void A_crop_matching_the_baseline_exactly_is_absent_with_zero_divergence()
    {
        byte[] crop = FlatColor(10, 10, 40, 38, 35);

        DialogWindowReading reading = DialogWindowReader.Read(crop, 10, 10, baselineMeanB: 40, baselineMeanG: 38, baselineMeanR: 35);

        Assert.Equal(DialogWindowState.Absent, reading.State);
        Assert.Equal(0d, reading.Divergence);
    }

    [Fact]
    public void A_crop_clearly_different_from_the_baseline_is_present()
    {
        // Divergence sqrt(3 * 200^2) ~= 346, well above DefaultPresentThreshold.
        byte[] crop = FlatColor(10, 10, 240, 240, 240);

        DialogWindowReading reading = DialogWindowReader.Read(crop, 10, 10, baselineMeanB: 40, baselineMeanG: 40, baselineMeanR: 40);

        Assert.Equal(DialogWindowState.Present, reading.State);
        Assert.True(reading.Divergence > DialogWindowReader.DefaultPresentThreshold);
    }

    [Fact]
    public void A_divergence_exactly_at_the_threshold_is_absent_not_present()
    {
        // meanB - baselineB = 24 exactly (G/R unchanged), matching
        // DefaultPresentThreshold -- the boundary belongs to "not yet different".
        byte[] crop = FlatColor(10, 10, (byte)(40 + DialogWindowReader.DefaultPresentThreshold), 40, 40);

        DialogWindowReading reading = DialogWindowReader.Read(crop, 10, 10, baselineMeanB: 40, baselineMeanG: 40, baselineMeanR: 40);

        Assert.Equal(DialogWindowState.Absent, reading.State);
    }

    [Fact]
    public void A_custom_threshold_overrides_the_default()
    {
        byte[] crop = FlatColor(10, 10, 50, 40, 40);

        // Divergence is 10 -- below the default threshold (Absent) but above a
        // caller-supplied, tighter one (Present).
        DialogWindowReading withDefault = DialogWindowReader.Read(crop, 10, 10, 40, 40, 40);
        DialogWindowReading withTighterThreshold = DialogWindowReader.Read(crop, 10, 10, 40, 40, 40, presentThreshold: 5);

        Assert.Equal(DialogWindowState.Absent, withDefault.State);
        Assert.Equal(DialogWindowState.Present, withTighterThreshold.State);
    }
}
