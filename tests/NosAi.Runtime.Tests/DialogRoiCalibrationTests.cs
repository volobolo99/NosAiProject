using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The file that decides whether the dialog-window reader is aimed at
/// anything, and what its confirmed-empty baseline looks like.
/// </summary>
public sealed class DialogRoiCalibrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "nosai-dialog-roi-" + Guid.NewGuid().ToString("N"));

    private string Path_(string name) => Path.Combine(_directory, name);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void A_missing_file_is_uncalibrated_and_is_not_broken()
    {
        DialogRoiCalibration loaded = DialogRoiCalibration.Load(Path_("absent"), out string? reason);

        Assert.False(loaded.IsCalibrated);
        Assert.Equal(DialogRoiCalibration.NotCalibratedReason, reason);
    }

    [Fact]
    public void A_confirmed_calibration_survives_a_round_trip()
    {
        var at = new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);
        DialogRoiCalibration written = DialogRoiCalibration.Confirmed(
            0.30, 0.40, 0.40, 0.30, 1920, 1080, at, baselineMeanB: 40.5, baselineMeanG: 38.2, baselineMeanR: 35.1);
        string path = Path_("dialog-roi.calibration");
        written.Save(path);

        DialogRoiCalibration loaded = DialogRoiCalibration.Load(path, out string? reason);

        Assert.Null(reason);
        Assert.True(loaded.IsCalibrated);
        Assert.Equal(0.30, loaded.X, 9);
        Assert.Equal(0.40, loaded.Y, 9);
        Assert.Equal(0.40, loaded.Width, 9);
        Assert.Equal(0.30, loaded.Height, 9);
        Assert.Equal(1920, loaded.ClientWidth);
        Assert.Equal(1080, loaded.ClientHeight);
        Assert.Equal(at, loaded.CalibratedAtUtc);
        Assert.Equal(40.5, loaded.BaselineMeanB, 9);
        Assert.Equal(38.2, loaded.BaselineMeanG, 9);
        Assert.Equal(35.1, loaded.BaselineMeanR, 9);
    }

    [Fact]
    public void An_uncalibrated_calibration_resolves_to_no_region_at_all()
        => Assert.Null(DialogRoiCalibration.Uncalibrated.Resolve(new PixelRect(0, 0, 1920, 1080)));

    [Fact]
    public void A_confirmed_calibration_resolves_against_the_client_area()
    {
        DialogRoiCalibration calibration = DialogRoiCalibration.Confirmed(
            0.30, 0.40, 0.40, 0.10, 1920, 1080, DateTime.UtcNow, 40, 40, 40);

        PixelRect roi = calibration.Resolve(new PixelRect(100, 50, 1000, 500))!.Value;

        Assert.Equal(100 + 300, roi.X);
        Assert.Equal(50 + 200, roi.Y);
        Assert.Equal(400, roi.Width);
        Assert.Equal(50, roi.Height);
    }

    [Theory]
    [InlineData(-0.1, 0.4, 0.4, 0.3)]
    [InlineData(0.90, 0.4, 0.4, 0.3)]
    [InlineData(0.30, 0.4, 0.0, 0.3)]
    [InlineData(0.30, 0.99, 0.4, 0.3)]
    public void A_region_outside_the_client_area_is_refused(double x, double y, double w, double h)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => DialogRoiCalibration.Confirmed(x, y, w, h, 1920, 1080, DateTime.UtcNow, 40, 40, 40));

    [Theory]
    [InlineData(-1, 40, 40)]
    [InlineData(256, 40, 40)]
    [InlineData(40, -1, 40)]
    [InlineData(40, 256, 40)]
    [InlineData(40, 40, -1)]
    [InlineData(40, 40, 256)]
    public void A_baseline_channel_mean_outside_a_valid_pixel_range_is_refused(double b, double g, double r)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => DialogRoiCalibration.Confirmed(0.3, 0.4, 0.4, 0.3, 1920, 1080, DateTime.UtcNow, b, g, r));

    [Fact]
    public void The_uncalibrated_state_refuses_to_be_written()
        => Assert.Throws<InvalidOperationException>(
            () => DialogRoiCalibration.Uncalibrated.Save(Path_("never")));

    [Theory]
    [InlineData("garbage")]
    [InlineData("nosai-dialog-roi 1")]
    [InlineData("nosai-dialog-roi 1\n0.3 0.4 0.4 0.3")]
    [InlineData("nosai-dialog-roi 1\nx y 0.4 0.3 1920 1080 2026-09-06T12:00:00Z 40 40 40")]
    public void A_malformed_file_is_uncalibrated_with_a_reason(string contents)
    {
        string path = Path_("broken");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, contents);

        DialogRoiCalibration loaded = DialogRoiCalibration.Load(path, out string? reason);

        Assert.False(loaded.IsCalibrated);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Fact]
    public void A_future_version_is_refused_rather_than_guessed_at()
    {
        string path = Path_("future");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "nosai-dialog-roi 2\n0.3 0.4 0.4 0.3 1920 1080 2026-09-06T12:00:00Z 40 40 40\n");

        DialogRoiCalibration loaded = DialogRoiCalibration.Load(path, out string? reason);

        Assert.False(loaded.IsCalibrated);
        Assert.Equal("dialog_roi_version_unsupported:2", reason);
    }

    [Fact]
    public void A_file_claiming_a_region_off_the_client_area_is_refused()
    {
        string path = Path_("offscreen");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "nosai-dialog-roi 1\n0.95 0.4 0.4 0.3 1920 1080 2026-09-06T12:00:00Z 40 40 40\n");

        DialogRoiCalibration loaded = DialogRoiCalibration.Load(path, out string? reason);

        Assert.False(loaded.IsCalibrated);
        Assert.Equal("dialog_roi_entry_malformed", reason);
    }
}
