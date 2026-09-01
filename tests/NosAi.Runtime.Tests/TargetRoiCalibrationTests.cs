using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The file that decides whether the target reader is aimed at anything.
/// </summary>
/// <remarks>
/// ADR-0018: it is machine-specific and not committed, so a fresh clone reads
/// <c>target_roi_not_calibrated</c> — a different state from broken, reported as
/// one, exactly as ADR-0017 arranged for the glyph atlas.
/// </remarks>
public sealed class TargetRoiCalibrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "nosai-target-roi-" + Guid.NewGuid().ToString("N"));

    private string Path_(string name) => Path.Combine(_directory, name);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void A_missing_file_is_uncalibrated_and_is_not_broken()
    {
        TargetRoiCalibration loaded = TargetRoiCalibration.Load(Path_("absent"), out string? reason);

        Assert.False(loaded.IsCalibrated);
        Assert.Equal(TargetRoiCalibration.NotCalibratedReason, reason);
    }

    [Fact]
    public void A_confirmed_calibration_survives_a_round_trip()
    {
        var at = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        TargetRoiCalibration written = TargetRoiCalibration.Confirmed(
            0.401, 0.062, 0.198, 0.021, 1920, 1080, at);
        string path = Path_("target-roi.calibration");
        written.Save(path);

        TargetRoiCalibration loaded = TargetRoiCalibration.Load(path, out string? reason);

        Assert.Null(reason);
        Assert.True(loaded.IsCalibrated);
        Assert.Equal(0.401, loaded.X, 9);
        Assert.Equal(0.062, loaded.Y, 9);
        Assert.Equal(0.198, loaded.Width, 9);
        Assert.Equal(0.021, loaded.Height, 9);
        Assert.Equal(1920, loaded.ClientWidth);
        Assert.Equal(1080, loaded.ClientHeight);
        Assert.Equal(at, loaded.CalibratedAtUtc);
    }

    /// <summary>
    /// Not "fall back to the guess". Resolving to the uninspected
    /// <see cref="RoiSegmenter"/> fractions is precisely how an unaimed reader
    /// starts publishing a confident <i>no target</i>.
    /// </summary>
    [Fact]
    public void An_uncalibrated_calibration_resolves_to_no_region_at_all()
        => Assert.Null(TargetRoiCalibration.Uncalibrated.Resolve(new PixelRect(0, 0, 1920, 1080)));

    [Fact]
    public void A_confirmed_calibration_resolves_against_the_client_area()
    {
        TargetRoiCalibration calibration = TargetRoiCalibration.Confirmed(
            0.40, 0.06, 0.20, 0.02, 1920, 1080, DateTime.UtcNow);

        PixelRect roi = calibration.Resolve(new PixelRect(100, 50, 1000, 500))!.Value;

        Assert.Equal(100 + 400, roi.X);
        Assert.Equal(50 + 30, roi.Y);
        Assert.Equal(200, roi.Width);
        Assert.Equal(10, roi.Height);
    }

    /// <summary>
    /// A region running off the client area would be clamped at read time into a
    /// region the operator never looked at, which is the failure this type exists
    /// to prevent.
    /// </summary>
    [Theory]
    [InlineData(-0.1, 0.06, 0.20, 0.02)]
    [InlineData(0.90, 0.06, 0.20, 0.02)]
    [InlineData(0.40, 0.06, 0.0, 0.02)]
    [InlineData(0.40, 0.99, 0.20, 0.02)]
    public void A_region_outside_the_client_area_is_refused(double x, double y, double w, double h)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => TargetRoiCalibration.Confirmed(x, y, w, h, 1920, 1080, DateTime.UtcNow));

    /// <summary>
    /// Writing the uncalibrated state would make the next load report a
    /// calibration nobody confirmed.
    /// </summary>
    [Fact]
    public void The_uncalibrated_state_refuses_to_be_written()
        => Assert.Throws<InvalidOperationException>(
            () => TargetRoiCalibration.Uncalibrated.Save(Path_("never")));

    [Theory]
    [InlineData("garbage")]
    [InlineData("nosai-target-roi 1")]
    [InlineData("nosai-target-roi 1\n0.4 0.06 0.2")]
    [InlineData("nosai-target-roi 1\nx y 0.2 0.02 1920 1080 2026-09-01T12:00:00Z")]
    public void A_malformed_file_is_uncalibrated_with_a_reason(string contents)
    {
        string path = Path_("broken");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, contents);

        TargetRoiCalibration loaded = TargetRoiCalibration.Load(path, out string? reason);

        Assert.False(loaded.IsCalibrated);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    /// <summary>
    /// A file written under a later format is refused rather than half-read, for
    /// the reason the glyph atlas refuses an old normalisation.
    /// </summary>
    [Fact]
    public void A_future_version_is_refused_rather_than_guessed_at()
    {
        string path = Path_("future");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "nosai-target-roi 2\n0.4 0.06 0.2 0.02 1920 1080 2026-09-01T12:00:00Z\n");

        TargetRoiCalibration loaded = TargetRoiCalibration.Load(path, out string? reason);

        Assert.False(loaded.IsCalibrated);
        Assert.Equal("target_roi_version_unsupported:2", reason);
    }

    /// <summary>
    /// A file whose region falls off the client area is a file written by
    /// something other than a confirmed calibration, and it is not believed.
    /// </summary>
    [Fact]
    public void A_file_claiming_a_region_off_the_client_area_is_refused()
    {
        string path = Path_("offscreen");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "nosai-target-roi 1\n0.95 0.06 0.2 0.02 1920 1080 2026-09-01T12:00:00Z\n");

        TargetRoiCalibration loaded = TargetRoiCalibration.Load(path, out string? reason);

        Assert.False(loaded.IsCalibrated);
        Assert.Equal("target_roi_entry_malformed", reason);
    }
}
