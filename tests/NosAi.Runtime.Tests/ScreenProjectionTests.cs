using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The transform from a map coordinate to a window pixel, and every case where it
/// refuses to produce one.
/// </summary>
/// <remarks>
/// <para>
/// F2-3, the card where a mistake makes the runtime click somewhere it was not
/// asked to. The arithmetic is checked against transforms built by hand, so a
/// wrong solve is caught here rather than by a click on a real client.
/// </para>
/// <para>
/// Three samples, not two: two pairs give four equations and a general affine map
/// has six unknowns, so two fix it only once something is assumed about its
/// shape. The projection is isometric, which is precisely the assumption that
/// would be wrong.
/// </para>
/// </remarks>
public sealed class ScreenProjectionTests : IDisposable
{
    private static readonly DateTime At = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
    private const int ClientWidth = 1024;
    private const int ClientHeight = 768;

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "nosai-screen-projection-" + Guid.NewGuid().ToString("N"));

    private string PathFor(string name) => Path.Combine(_directory, name);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    /// <summary>Samples generated from a known transform, so the solve has a right answer.</summary>
    private static ScreenProjectionSample Sample(
        int mapX, int mapY, double a, double b, double c, double d, double e, double f)
        => new(
            new MapPoint(mapX, mapY),
            (int)Math.Round((a * mapX) + (b * mapY) + c),
            (int)Math.Round((d * mapX) + (e * mapY) + f));

    /// <summary>
    /// An isometric layout: the axes mix, which is exactly what a two-point
    /// axis-aligned calibration would get wrong.
    /// </summary>
    private static (double A, double B, double C, double D, double E, double F) Isometric
        => (16.0, -16.0, 512.0, 8.0, 8.0, 100.0);

    private static List<ScreenProjectionSample> IsometricSamples(params (int X, int Y)[] points)
    {
        (double a, double b, double c, double d, double e, double f) = Isometric;
        return points.Select(p => Sample(p.X, p.Y, a, b, c, d, e, f)).ToList();
    }

    // -------------------------------------------------------------- the solve

    [Fact]
    public void Three_samples_recover_the_transform_that_produced_them()
    {
        List<ScreenProjectionSample> samples = IsometricSamples((10, 10), (20, 12), (14, 25));

        Assert.True(ScreenProjectionCalibration.TrySolve(
            samples, ClientWidth, ClientHeight, At,
            out ScreenProjectionCalibration calibration, out string? reason), reason);

        (double a, double b, double c, double d, double e, double f) = Isometric;
        Assert.Equal(a, calibration.A, 6);
        Assert.Equal(b, calibration.B, 6);
        Assert.Equal(c, calibration.C, 6);
        Assert.Equal(d, calibration.D, 6);
        Assert.Equal(e, calibration.E, 6);
        Assert.Equal(f, calibration.F, 6);
    }

    /// <summary>
    /// The point of measuring rather than assuming: the recovered map predicts a
    /// coordinate that was never sampled.
    /// </summary>
    [Fact]
    public void The_solved_transform_predicts_a_point_it_never_saw()
    {
        List<ScreenProjectionSample> samples = IsometricSamples((10, 10), (20, 12), (14, 25));
        Assert.True(ScreenProjectionCalibration.TrySolve(
            samples, ClientWidth, ClientHeight, At, out ScreenProjectionCalibration calibration, out _));

        (double x, double y) = calibration.Project(new MapPoint(33, 7))!.Value;

        (double a, double b, double c, double d, double e, double f) = Isometric;
        Assert.Equal((a * 33) + (b * 7) + c, x, 6);
        Assert.Equal((d * 33) + (e * 7) + f, y, 6);
    }

    /// <summary>
    /// Samples beyond the third are not fitted; they check the answer, which is
    /// the only independent validity check this file can carry.
    /// </summary>
    [Fact]
    public void A_fourth_sample_is_held_back_as_a_check_and_agrees()
    {
        List<ScreenProjectionSample> samples = IsometricSamples((10, 10), (20, 12), (14, 25), (30, 30));

        Assert.True(ScreenProjectionCalibration.TrySolve(
            samples, ClientWidth, ClientHeight, At, out ScreenProjectionCalibration calibration, out _));

        Assert.Equal(1, calibration.VerifiedAgainstSamples);
        Assert.True(calibration.WorstResidualPixels < 1.0);
    }

    /// <summary>
    /// And when it does not agree, nothing is written. A mistyped coordinate or a
    /// sample taken after the view scrolled must not become a transform.
    /// </summary>
    [Fact]
    public void A_disagreeing_sample_refuses_the_whole_calibration()
    {
        List<ScreenProjectionSample> samples = IsometricSamples((10, 10), (20, 12), (14, 25));
        samples.Add(new ScreenProjectionSample(new MapPoint(30, 30), 5, 5));

        Assert.False(ScreenProjectionCalibration.TrySolve(
            samples, ClientWidth, ClientHeight, At, out ScreenProjectionCalibration calibration, out string? reason));

        Assert.False(calibration.IsCalibrated);
        Assert.StartsWith("samples_disagree", reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Three points on a line cannot fix a mapping of the plane: the operator
    /// walked in one direction and has to walk in another.
    /// </summary>
    [Fact]
    public void Collinear_samples_are_refused_with_a_reason_that_says_what_to_do()
    {
        List<ScreenProjectionSample> samples = IsometricSamples((10, 10), (20, 20), (30, 30));

        Assert.False(ScreenProjectionCalibration.TrySolve(
            samples, ClientWidth, ClientHeight, At, out _, out string? reason));

        Assert.Equal("samples_are_collinear", reason);
    }

    [Fact]
    public void Fewer_than_three_samples_is_refused_and_says_how_many_are_missing()
    {
        List<ScreenProjectionSample> samples = IsometricSamples((10, 10), (20, 12));

        Assert.False(ScreenProjectionCalibration.TrySolve(
            samples, ClientWidth, ClientHeight, At, out _, out string? reason));

        Assert.Equal("not_enough_samples:2_of_3", reason);
    }

    // ------------------------------------------------------------ persistence

    [Fact]
    public void A_calibration_survives_a_round_trip()
    {
        List<ScreenProjectionSample> samples = IsometricSamples((10, 10), (20, 12), (14, 25), (30, 30));
        Assert.True(ScreenProjectionCalibration.TrySolve(
            samples, ClientWidth, ClientHeight, At, out ScreenProjectionCalibration written, out _));
        string path = PathFor("screen-projection.calibration");
        written.Save(path);

        ScreenProjectionCalibration loaded = ScreenProjectionCalibration.Load(path, out string? reason);

        Assert.Null(reason);
        Assert.True(loaded.IsCalibrated);
        Assert.Equal(written.A, loaded.A, 9);
        Assert.Equal(written.E, loaded.E, 9);
        Assert.Equal(ClientWidth, loaded.ClientWidth);
        Assert.Equal(At, loaded.CalibratedAtUtc);
        Assert.Equal(1, loaded.VerifiedAgainstSamples);
    }

    [Fact]
    public void A_missing_file_is_uncalibrated_and_is_not_broken()
    {
        ScreenProjectionCalibration loaded =
            ScreenProjectionCalibration.Load(PathFor("absent"), out string? reason);

        Assert.False(loaded.IsCalibrated);
        Assert.Equal(ScreenProjectionCalibration.NotCalibratedReason, reason);
    }

    /// <summary>A degenerate transform maps the whole map to one pixel; it is not one.</summary>
    [Fact]
    public void A_file_whose_transform_collapses_the_plane_is_refused()
    {
        string path = PathFor("degenerate");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "nosai-screen-projection 1\n0 0 0 0 0 0 1024 768 0 0 2026-09-01T12:00:00Z\n");

        ScreenProjectionCalibration loaded = ScreenProjectionCalibration.Load(path, out string? reason);

        Assert.False(loaded.IsCalibrated);
        Assert.Equal("screen_projection_entry_malformed", reason);
    }

    [Fact]
    public void The_uncalibrated_state_refuses_to_be_written()
        => Assert.Throws<InvalidOperationException>(
            () => ScreenProjectionCalibration.Uncalibrated.Save(PathFor("never")));

    [Fact]
    public void An_uncalibrated_transform_projects_nothing_rather_than_guessing()
        => Assert.Null(ScreenProjectionCalibration.Uncalibrated.Project(new MapPoint(10, 10)));

    // ------------------------------------------------------- the projection

    private static CalibratedScreenProjection Projection(
        PixelRect? clientArea, int clientWidth = ClientWidth, int clientHeight = ClientHeight)
    {
        List<ScreenProjectionSample> samples = IsometricSamples((10, 10), (20, 12), (14, 25));
        Assert.True(ScreenProjectionCalibration.TrySolve(
            samples, clientWidth, clientHeight, At, out ScreenProjectionCalibration calibration, out _));
        return new CalibratedScreenProjection(calibration, () => clientArea);
    }

    /// <summary>
    /// The calibration is client-relative, so the window's position on the desktop
    /// is added here. A window that has been dragged has not invalidated the
    /// mapping, only moved it.
    /// </summary>
    [Fact]
    public void A_projected_point_is_offset_by_where_the_window_currently_is()
    {
        (double a, double b, double c, double d, double e, double f) = Isometric;
        var moved = new PixelRect(300, 200, ClientWidth, ClientHeight);

        Assert.True(Projection(moved).TryProject(20, 12, out int x, out int y, out string? reason), reason);

        Assert.Equal(300 + (int)Math.Round((a * 20) + (b * 12) + c), x);
        Assert.Equal(200 + (int)Math.Round((d * 20) + (e * 12) + f), y);
    }

    /// <summary>
    /// The domain check the card asks for. Clamping would turn "that coordinate is
    /// not on screen" into a real click at a place nobody chose.
    /// </summary>
    [Fact]
    public void A_point_outside_the_client_area_is_refused_not_clamped()
    {
        var area = new PixelRect(0, 0, ClientWidth, ClientHeight);

        Assert.False(Projection(area).TryProject(9000, -9000, out int x, out int y, out string? reason));

        Assert.Equal(CalibratedScreenProjection.OutsideClientAreaReason, reason);
        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    /// <summary>
    /// A resized client is a different zoom and a different layout, so the measured
    /// transform no longer describes what is on screen.
    /// </summary>
    [Fact]
    public void A_client_resized_since_the_calibration_is_refused()
    {
        var resized = new PixelRect(0, 0, 1280, 1024);

        Assert.False(Projection(resized).TryProject(20, 12, out _, out _, out string? reason));

        Assert.Equal(CalibratedScreenProjection.ClientResizedReason, reason);
    }

    [Fact]
    public void Without_the_window_there_is_nothing_to_be_inside_of()
    {
        Assert.False(Projection(clientArea: null).TryProject(20, 12, out _, out _, out string? reason));

        Assert.Equal(CalibratedScreenProjection.WindowNotLocatedReason, reason);
    }

    /// <summary>
    /// The refusal the effector already acts on: without a calibration there is no
    /// point, and a fallback transform would click somewhere in the window.
    /// </summary>
    [Fact]
    public void Without_a_calibration_the_projection_refuses_by_name()
    {
        var projection = new CalibratedScreenProjection(
            ScreenProjectionCalibration.Uncalibrated,
            () => new PixelRect(0, 0, ClientWidth, ClientHeight));

        Assert.False(projection.TryProject(20, 12, out _, out _, out string? reason));

        Assert.Equal(ScreenProjectionCalibration.NotCalibratedReason, reason);
    }
}
