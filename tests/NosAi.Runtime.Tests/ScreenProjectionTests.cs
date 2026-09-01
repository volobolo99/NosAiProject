using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
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
/// <b>The fit is on offsets from the character, not on map coordinates.</b> The
/// camera follows the character, so no transform from an absolute coordinate to a
/// pixel exists — the same square is drawn wherever the character is standing.
/// That is not a refinement: it is why the earlier version produced a calibration
/// with a residual of 0.00 that described nothing, and several of the tests below
/// exist only to state properties the absolute model could not express.
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

    /// <summary>Where the character stands in the projection tests.</summary>
    /// <remarks>
    /// Deliberately not the map origin. With the character at 0,0 an offset and an
    /// absolute coordinate are the same number, and every one of these tests would
    /// pass against the old model too.
    /// </remarks>
    private static readonly MapPoint Standing = new(100, 100);

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
        int deltaX, int deltaY, double a, double b, double c, double d, double e, double f)
        => new(
            new MapPoint(deltaX, deltaY),
            (int)Math.Round((a * deltaX) + (b * deltaY) + c),
            (int)Math.Round((d * deltaX) + (e * deltaY) + f));

    /// <summary>
    /// An isometric layout: the axes mix, which is exactly what a two-point
    /// axis-aligned calibration would get wrong. The translation is the pixel the
    /// character is drawn at, near the middle of the window.
    /// </summary>
    private static (double A, double B, double C, double D, double E, double F) Isometric
        => (16.0, -16.0, 512.0, 8.0, 8.0, 380.0);

    private static List<ScreenProjectionSample> IsometricSamples(params (int X, int Y)[] offsets)
    {
        (double a, double b, double c, double d, double e, double f) = Isometric;
        return offsets.Select(p => Sample(p.X, p.Y, a, b, c, d, e, f)).ToList();
    }

    /// <summary>Offsets small enough to stay on screen and spread enough to fit.</summary>
    private static List<ScreenProjectionSample> ThreeOffsets()
        => IsometricSamples((6, 2), (-4, 5), (1, -7));

    // -------------------------------------------------------------- the solve

    [Fact]
    public void Three_samples_recover_the_transform_that_produced_them()
    {
        List<ScreenProjectionSample> samples = ThreeOffsets();

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
    /// The zero offset is the character itself, so the translation is the pixel it
    /// is drawn at rather than an arbitrary constant.
    /// </summary>
    [Fact]
    public void The_translation_is_the_pixel_the_character_is_drawn_at()
    {
        Assert.True(ScreenProjectionCalibration.TrySolve(
            ThreeOffsets(), ClientWidth, ClientHeight, At,
            out ScreenProjectionCalibration calibration, out _));

        (double x, double y) = calibration.ProjectDelta(new MapPoint(0, 0))!.Value;

        Assert.Equal(calibration.Anchor.X, x, 6);
        Assert.Equal(calibration.Anchor.Y, y, 6);
        Assert.Equal(Isometric.C, x, 6);
        Assert.Equal(Isometric.F, y, 6);
    }

    /// <summary>
    /// The point of measuring rather than assuming: the recovered map predicts an
    /// offset that was never sampled.
    /// </summary>
    [Fact]
    public void The_solved_transform_predicts_an_offset_it_never_saw()
    {
        Assert.True(ScreenProjectionCalibration.TrySolve(
            ThreeOffsets(), ClientWidth, ClientHeight, At,
            out ScreenProjectionCalibration calibration, out _));

        (double x, double y) = calibration.ProjectDelta(new MapPoint(9, -3))!.Value;

        (double a, double b, double c, double d, double e, double f) = Isometric;
        Assert.Equal((a * 9) + (b * -3) + c, x, 6);
        Assert.Equal((d * 9) + (e * -3) + f, y, 6);
    }

    /// <summary>
    /// Samples beyond the third are not fitted; they check the answer, which is
    /// the only independent validity check this file can carry.
    /// </summary>
    [Fact]
    public void A_fourth_sample_is_held_back_as_a_check_and_agrees()
    {
        List<ScreenProjectionSample> samples = IsometricSamples((6, 2), (-4, 5), (1, -7), (8, 8));

        Assert.True(ScreenProjectionCalibration.TrySolve(
            samples, ClientWidth, ClientHeight, At, out ScreenProjectionCalibration calibration, out _));

        Assert.Equal(1, calibration.VerifiedAgainstSamples);
        Assert.True(calibration.WorstResidualPixels < 1.0);
    }

    /// <summary>
    /// And when it does not agree, nothing is written. A click that hit an
    /// obstacle walked somewhere other than where it was aimed, and that pair must
    /// not become a transform.
    /// </summary>
    [Fact]
    public void A_disagreeing_sample_refuses_the_whole_calibration()
    {
        List<ScreenProjectionSample> samples = ThreeOffsets();
        samples.Add(new ScreenProjectionSample(new MapPoint(8, 8), 5, 5));

        Assert.False(ScreenProjectionCalibration.TrySolve(
            samples, ClientWidth, ClientHeight, At, out ScreenProjectionCalibration calibration, out string? reason));

        Assert.False(calibration.IsCalibrated);
        Assert.StartsWith("samples_disagree", reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Three offsets on a line cannot fix a mapping of the plane: every sample
    /// walked along the same axis and one has to cross it.
    /// </summary>
    [Fact]
    public void Collinear_samples_are_refused_with_a_reason_that_says_what_to_do()
    {
        List<ScreenProjectionSample> samples = IsometricSamples((2, 2), (4, 4), (6, 6));

        Assert.False(ScreenProjectionCalibration.TrySolve(
            samples, ClientWidth, ClientHeight, At, out _, out string? reason));

        Assert.Equal("samples_are_collinear", reason);
    }

    [Fact]
    public void Fewer_than_three_samples_is_refused_and_says_how_many_are_missing()
    {
        List<ScreenProjectionSample> samples = IsometricSamples((6, 2), (-4, 5));

        Assert.False(ScreenProjectionCalibration.TrySolve(
            samples, ClientWidth, ClientHeight, At, out _, out string? reason));

        Assert.Equal("not_enough_samples:2_of_3", reason);
    }

    /// <summary>
    /// The check the absolute model had no way to state. A zero offset is the
    /// character, the character is visibly on screen, so a fit that draws it off
    /// the window is describing something other than this client.
    /// </summary>
    [Fact]
    public void A_fit_that_draws_the_character_off_the_window_is_refused()
    {
        List<ScreenProjectionSample> samples =
        [
            Sample(6, 2, 16, -16, 5000, 8, 8, 380),
            Sample(-4, 5, 16, -16, 5000, 8, 8, 380),
            Sample(1, -7, 16, -16, 5000, 8, 8, 380),
        ];

        Assert.False(ScreenProjectionCalibration.TrySolve(
            samples, ClientWidth, ClientHeight, At, out ScreenProjectionCalibration calibration, out string? reason));

        Assert.False(calibration.IsCalibrated);
        Assert.StartsWith("character_anchor_outside_client", reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// Samples that all land on the same pixel measure no scale. This is what the
    /// character's own position produced when it was sampled against its own
    /// pixel, and it is the failure that ended the absolute model.
    /// </summary>
    [Fact]
    public void Samples_whose_pixels_do_not_move_are_refused()
    {
        List<ScreenProjectionSample> samples =
        [
            new(new MapPoint(6, 2), 511, 373),
            new(new MapPoint(-4, 5), 512, 374),
            new(new MapPoint(1, -7), 513, 380),
        ];

        Assert.False(ScreenProjectionCalibration.TrySolve(
            samples, ClientWidth, ClientHeight, At, out _, out string? reason));

        Assert.StartsWith("screen_points_do_not_move", reason, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ persistence

    [Fact]
    public void A_calibration_survives_a_round_trip()
    {
        List<ScreenProjectionSample> samples = IsometricSamples((6, 2), (-4, 5), (1, -7), (8, 8));
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

    /// <summary>
    /// A file from the absolute model must not be reinterpreted as offsets. Its
    /// coefficients are wrong by the character's whole distance from the map
    /// origin, and the click would still land inside the window — so nothing
    /// downstream would notice, and only the version can catch it.
    /// </summary>
    [Fact]
    public void A_calibration_from_the_absolute_model_is_refused_by_version()
    {
        string path = PathFor("v1");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            path, "nosai-screen-projection 1\n16 -16 512 8 8 380 1024 768 0 1 2026-09-01T12:00:00Z\n");

        ScreenProjectionCalibration loaded = ScreenProjectionCalibration.Load(path, out string? reason);

        Assert.False(loaded.IsCalibrated);
        Assert.Equal("screen_projection_version_unsupported:1", reason);
    }

    /// <summary>A degenerate transform maps every offset to one pixel; it is not one.</summary>
    [Fact]
    public void A_file_whose_transform_collapses_the_plane_is_refused()
    {
        string path = PathFor("degenerate");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "nosai-screen-projection 2\n0 0 0 0 0 0 1024 768 0 0 2026-09-01T12:00:00Z\n");

        ScreenProjectionCalibration loaded = ScreenProjectionCalibration.Load(path, out string? reason);

        Assert.False(loaded.IsCalibrated);
        Assert.Equal("screen_projection_entry_malformed", reason);
    }

    /// <summary>A hand-edited file has had no solver check its anchor.</summary>
    [Fact]
    public void A_file_whose_anchor_is_off_the_window_is_refused()
    {
        string path = PathFor("anchor");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            path, "nosai-screen-projection 2\n16 -16 5000 8 8 380 1024 768 0 1 2026-09-01T12:00:00Z\n");

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
        => Assert.Null(ScreenProjectionCalibration.Uncalibrated.ProjectDelta(new MapPoint(10, 10)));

    // ------------------------------------------------------- the projection

    private static CalibratedScreenProjection Projection(
        PixelRect? clientArea,
        int clientWidth = ClientWidth,
        int clientHeight = ClientHeight,
        ClassifiedValue<MapPoint>? player = null)
    {
        Assert.True(ScreenProjectionCalibration.TrySolve(
            ThreeOffsets(), clientWidth, clientHeight, At,
            out ScreenProjectionCalibration calibration, out _));
        return new CalibratedScreenProjection(
            calibration,
            () => clientArea,
            () => player ?? ClassifiedValue<MapPoint>.Live(Standing, At));
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

        Assert.True(
            Projection(moved).TryProject(Standing.X + 6, Standing.Y + 2, out int x, out int y, out string? reason),
            reason);

        Assert.Equal(300 + (int)Math.Round((a * 6) + (b * 2) + c), x);
        Assert.Equal(200 + (int)Math.Round((d * 6) + (e * 2) + f), y);
    }

    /// <summary>
    /// The property the whole rewrite is for: the camera follows the character, so
    /// the same square is a different pixel once the character has moved. Under the
    /// absolute model this returned the same point both times, and a click aimed at
    /// a monster landed wherever the character used to be standing.
    /// </summary>
    [Fact]
    public void The_same_square_projects_elsewhere_once_the_character_has_moved()
    {
        var area = new PixelRect(0, 0, ClientWidth, ClientHeight);
        int targetX = Standing.X + 3, targetY = Standing.Y + 3;

        Assert.True(Projection(area).TryProject(targetX, targetY, out int before, out int beforeY, out _));

        CalibratedScreenProjection moved = Projection(
            area, player: ClassifiedValue<MapPoint>.Live(new MapPoint(Standing.X + 5, Standing.Y), At));
        Assert.True(moved.TryProject(targetX, targetY, out int after, out int afterY, out _));

        Assert.NotEqual(before, after);
        Assert.NotEqual(beforeY, afterY);
    }

    /// <summary>
    /// An unknown position is not the map origin. Treating it as one would aim
    /// every click at a real point on screen with nothing behind it — ADR-0014's
    /// rule at the exact place where breaking it becomes an action in the world.
    /// </summary>
    [Fact]
    public void An_unknown_character_position_is_refused_and_carries_why()
    {
        CalibratedScreenProjection projection = Projection(
            new PixelRect(0, 0, ClientWidth, ClientHeight),
            player: ClassifiedValue<MapPoint>.Unknown("player_manager_null"));

        Assert.False(projection.TryProject(Standing.X + 6, Standing.Y + 2, out int x, out int y, out string? reason));

        Assert.Equal(
            $"{CalibratedScreenProjection.PlayerPositionUnknownReason}:player_manager_null", reason);
        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    /// <summary>
    /// The domain check the card asks for. Clamping would turn "that square is not
    /// on screen" into a real click at a place nobody chose — and with a camera
    /// that follows the character, a target far away is simply not drawn, which is
    /// an ordinary event rather than an error.
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
    /// transform no longer describes what is on screen. This is what makes going
    /// full screen safe rather than silently wrong: it refuses, and the
    /// auto-calibration can then measure the new size.
    /// </summary>
    [Fact]
    public void A_client_resized_since_the_calibration_is_refused()
    {
        var resized = new PixelRect(0, 0, 1280, 1024);

        Assert.False(
            Projection(resized).TryProject(Standing.X + 6, Standing.Y + 2, out _, out _, out string? reason));

        Assert.Equal(CalibratedScreenProjection.ClientResizedReason, reason);
    }

    [Fact]
    public void Without_the_window_there_is_nothing_to_be_inside_of()
    {
        Assert.False(
            Projection(clientArea: null).TryProject(
                Standing.X + 6, Standing.Y + 2, out _, out _, out string? reason));

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
            () => new PixelRect(0, 0, ClientWidth, ClientHeight),
            () => ClassifiedValue<MapPoint>.Live(Standing, At));

        Assert.False(projection.TryProject(20, 12, out _, out _, out string? reason));

        Assert.Equal(ScreenProjectionCalibration.NotCalibratedReason, reason);
    }
}
