using System;
using Xunit;

namespace NosAi.ControlPanel.Tests;

public class TargetRoiCardTests
{
    [Fact]
    public void Four_valid_fractions_pass()
    {
        bool accepted = TargetRoiCalibrationCard.TryValidateFractions(
            "0.40", "0.06", "0.20", "0.02",
            out var x, out var y, out var w, out var h, out var refusal);

        Assert.True(accepted);
        Assert.Null(refusal);
        Assert.Equal(0.40, x, 10);
        Assert.Equal(0.06, y, 10);
        Assert.Equal(0.20, w, 10);
        Assert.Equal(0.02, h, 10);
    }

    [Theory]
    [InlineData("-0.10", "0.06", "0.20", "0.02", "x")]  // x below 0
    [InlineData("0.40", "1.00", "0.20", "0.02", "y")]   // y at 1, outside [0, 1)
    [InlineData("0.40", "0.06", "0.00", "0.02", "w")]   // w at 0, outside (0, 1]
    [InlineData("0.40", "0.06", "0.20", "1.50", "h")]   // h above 1
    [InlineData("0.90", "0.06", "0.20", "0.02", "x")]   // x + w > 1
    [InlineData("0.40", "0.90", "0.20", "0.20", "y")]   // y + h > 1
    [InlineData("abc", "0.06", "0.20", "0.02", "x")]    // not a number
    public void Each_range_rule_names_the_guilty_fraction(
        string xText, string yText, string wText, string hText, string guiltyFraction)
    {
        bool accepted = TargetRoiCalibrationCard.TryValidateFractions(
            xText, yText, wText, hText,
            out _, out _, out _, out _, out var refusal);

        Assert.False(accepted);
        Assert.NotNull(refusal);
        Assert.Contains(guiltyFraction, refusal!);
    }

    [Fact]
    public void A_crop_older_than_the_run_is_not_presented_as_new()
    {
        var runStarted = new DateTime(2024, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        var beforeRun = new DateTime(2023, 12, 1, 8, 0, 0, DateTimeKind.Utc);
        var afterRun = new DateTime(2024, 6, 1, 8, 0, 0, DateTimeKind.Utc);

        string missing = TargetRoiCalibrationCard.DescribeCrop(false, afterRun, runStarted);
        Assert.Contains("Nessun ritaglio", missing);

        string stale = TargetRoiCalibrationCard.DescribeCrop(true, beforeRun, runStarted);
        Assert.Contains("non è di questa corsa", stale);
        Assert.Contains("2023-12-01 08:00:00 UTC", stale);

        string fresh = TargetRoiCalibrationCard.DescribeCrop(true, afterRun, runStarted);
        Assert.Contains("Ritaglio nuovo", fresh);
        Assert.Contains("2024-06-01 08:00:00 UTC", fresh);
    }
}
