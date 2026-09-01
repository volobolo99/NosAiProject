using System.Linq;
using System.Globalization;
using System.Text;
using NosAi.Runtime.Autonomy;

namespace NosAi.Runtime.Perception;

/// <summary>
/// One pairing of a map <i>offset from the character</i> with the client pixel
/// that offset is drawn at.
/// </summary>
/// <param name="MapDelta">
/// Tiles away from the square the character is standing on, not an absolute map
/// coordinate. See <see cref="ScreenProjectionCalibration"/> for why the absolute
/// form cannot be measured at all.
/// </param>
/// <param name="ScreenX">
/// Relative to the client area's top-left corner, not the desktop. A window that
/// moves must not invalidate a calibration: the shape of the mapping belongs to
/// the client, its position on the desktop does not.
/// </param>
public readonly record struct ScreenProjectionSample(MapPoint MapDelta, int ScreenX, int ScreenY);

/// <summary>
/// The measured transform from a map offset to a pixel of the client area.
/// </summary>
/// <remarks>
/// <para>
/// <b>Measured, not derived.</b> The mapping depends on the resolution, the zoom
/// and the client's own projection. Anything assumed about its shape is a guess
/// that produces a click somewhere in the window, which the cycle would discover
/// only at verification — after having acted.
/// </para>
/// <para>
/// <b>Why an offset and not a map coordinate.</b> The first version of this fitted
/// <c>screen = A·mapCoordinate + C</c>, and no such transform exists: the camera
/// follows the character, so the same square is drawn at a different pixel every
/// time the character moves. Measured on the real client, walking twelve tiles
/// moved the character's own pixel by seven — it stays at the anchor and the map
/// scrolls underneath. Fitting the absolute form to samples that all land in the
/// same place gave a residual of 0.00 on a transform that described nothing,
/// which is the worst possible outcome: a confident answer with no content.
/// </para>
/// <para>
/// What does hold still is the relation between an <i>offset</i> from the
/// character and the pixel that offset appears at, so this fits
/// <c>screen = A·Δmap + anchor</c>. The consequence worth stating: <c>C</c> and
/// <c>F</c> are no longer an arbitrary translation, they are the pixel the
/// character itself is drawn at, and the fit is rejected when they land outside
/// the window — a check the absolute form could not express.
/// </para>
/// <para>
/// <b>Why three samples and not two.</b> F2-3 specifies a two-point calibration.
/// Two pairs give four equations and a general affine map has six unknowns
/// (<c>sx = a·Δx + b·Δy + c</c>, <c>sy = d·Δx + e·Δy + f</c>), so two points fix
/// it only once something is assumed about its structure — that the axes do not
/// mix, say, which is exactly false for an isometric projection, the one the card
/// names. Three non-collinear pairs determine all six and assume nothing. Samples
/// beyond the third are not fitted: they are held back and used to check the
/// solution, which is the only way this file can carry its own validity check.
/// </para>
/// <para>
/// <b>Machine-specific, and therefore not committed.</b> It belongs in gitignored
/// <c>data/perception/</c> beside the glyph atlas and the target-frame
/// calibration, for the reason ADR-0017 gives for the atlas: it describes one
/// client at one resolution on one display.
/// </para>
/// </remarks>
public sealed record ScreenProjectionCalibration
{
    /// <summary>Where the calibration lives, relative to the repository root.</summary>
    public const string RelativePath = "data/perception/screen-projection.calibration";

    /// <summary>Reported by every consumer while no calibration exists.</summary>
    public const string NotCalibratedReason = "screen_projection_not_calibrated";

    /// <summary>The fewest pairs that determine a general affine map.</summary>
    public const int MinimumSamples = 3;

    /// <summary>
    /// How far a held-back sample may land from where the solved transform puts
    /// it, in pixels.
    /// </summary>
    /// <remarks>
    /// The operator places a cursor by hand, so a pixel or two is the procedure,
    /// not an error. Tens of pixels is a mistyped coordinate or a sample taken
    /// after the view scrolled, and that is the calibration this refuses to write.
    /// </remarks>
    public const double MaxVerificationResidualPixels = 6.0;

    /// <summary>
    /// How far apart the sampled pixels must lie before a transform can be fitted.
    /// </summary>
    /// <remarks>
    /// Generous, because it is not measuring precision: it separates "the screen
    /// points moved" from "they did not". A camera that follows the character
    /// keeps every sample of the character within a few pixels of the same spot,
    /// and no amount of map movement makes that measurable.
    /// </remarks>
    public const double MinScreenSpanPixels = 40.0;

    /// <summary>Reported when a file from before the offset model is found.</summary>
    /// <remarks>
    /// The bump from 1 to 2 is not cosmetic. A v1 file holds coefficients fitted
    /// to absolute map coordinates; read as offsets they project to a pixel that
    /// is wrong by the character's whole distance from the map origin, and the
    /// click still lands inside the window, so nothing downstream would notice.
    /// Refusing by version is what stops a stale file from being silently
    /// reinterpreted into a real click somewhere nobody chose.
    /// </remarks>
    private const string Magic = "nosai-screen-projection";
    private const int Version = 2;

    private ScreenProjectionCalibration(
        bool isCalibrated,
        double a, double b, double c,
        double d, double e, double f,
        int clientWidth, int clientHeight,
        double worstResidual,
        int verifiedAgainst,
        DateTime? calibratedAtUtc)
    {
        IsCalibrated = isCalibrated;
        A = a; B = b; C = c;
        D = d; E = e; F = f;
        ClientWidth = clientWidth;
        ClientHeight = clientHeight;
        WorstResidualPixels = worstResidual;
        VerifiedAgainstSamples = verifiedAgainst;
        CalibratedAtUtc = calibratedAtUtc;
    }

    /// <summary>Whether a real calibration was loaded. False is not "guess one".</summary>
    public bool IsCalibrated { get; }

    /// <summary>Coefficients of <c>screenX = A·Δx + B·Δy + C</c>.</summary>
    public double A { get; }
    public double B { get; }
    public double C { get; }

    /// <summary>Coefficients of <c>screenY = D·Δx + E·Δy + F</c>.</summary>
    public double D { get; }
    public double E { get; }
    public double F { get; }

    /// <summary>
    /// The pixel the character itself is drawn at, which is where a zero offset
    /// projects.
    /// </summary>
    /// <remarks>
    /// Not a spare name for <see cref="C"/> and <see cref="F"/>: it is the one
    /// coefficient of the fit that can be checked against something outside the
    /// arithmetic, because the character is visibly on screen and so the anchor
    /// has to be inside the client area.
    /// </remarks>
    public (double X, double Y) Anchor => (C, F);

    /// <summary>The client area the samples were taken against, in pixels.</summary>
    /// <remarks>
    /// A different size means a different zoom or layout, so the transform no
    /// longer describes what is on screen. Consumers refuse rather than scale it,
    /// because scaling assumes the very structure this type refuses to assume.
    /// </remarks>
    public int ClientWidth { get; }
    public int ClientHeight { get; }

    /// <summary>How far the worst held-back sample landed from its prediction.</summary>
    public double WorstResidualPixels { get; }

    /// <summary>How many samples were held back to check the solution.</summary>
    /// <remarks>
    /// Zero means the calibration was solved from exactly three pairs and nothing
    /// independent confirmed it. Usable, and worth knowing.
    /// </remarks>
    public int VerifiedAgainstSamples { get; }

    /// <summary>When the operator produced it, or null when uncalibrated.</summary>
    public DateTime? CalibratedAtUtc { get; }

    /// <summary>The state before the operator has calibrated anything.</summary>
    public static ScreenProjectionCalibration Uncalibrated { get; } =
        new(false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null);

    /// <summary>
    /// Solves the transform from the operator's samples, or says why it cannot.
    /// </summary>
    /// <remarks>
    /// The first three samples determine the map; every sample after them is
    /// projected through it and checked. A calibration that cannot predict a pair
    /// the operator actually recorded is not written.
    /// </remarks>
    public static bool TrySolve(
        IReadOnlyList<ScreenProjectionSample> samples,
        int clientWidth,
        int clientHeight,
        DateTime calibratedAtUtc,
        out ScreenProjectionCalibration calibration,
        out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(samples);
        calibration = Uncalibrated;

        if (clientWidth <= 0 || clientHeight <= 0)
        {
            failureReason = "client_area_has_no_extent";
            return false;
        }

        if (samples.Count < MinimumSamples)
        {
            failureReason = $"not_enough_samples:{samples.Count}_of_{MinimumSamples}";
            return false;
        }

        ScreenProjectionSample s0 = samples[0], s1 = samples[1], s2 = samples[2];

        // Twice the signed area of the triangle the three offsets make. Zero
        // means they lie on a line, and a line cannot fix a mapping of the plane:
        // every sample walked along the same axis and one has to cross it.
        double determinant =
            (s1.MapDelta.X - s0.MapDelta.X) * (double)(s2.MapDelta.Y - s0.MapDelta.Y) -
            (s2.MapDelta.X - s0.MapDelta.X) * (double)(s1.MapDelta.Y - s0.MapDelta.Y);

        if (Math.Abs(determinant) < 1e-9)
        {
            failureReason = "samples_are_collinear";
            return false;
        }

        // The screen points have to move too, or there is nothing to measure a
        // scale against. This survives from the absolute model, where it was the
        // check that finally caught it: the samples were the character's own
        // pixel, which the camera holds still, and three of them fit six unknowns
        // exactly so the residual saw nothing wrong.
        double screenSpanX = samples.Max(s => (double)s.ScreenX) - samples.Min(s => (double)s.ScreenX);
        double screenSpanY = samples.Max(s => (double)s.ScreenY) - samples.Min(s => (double)s.ScreenY);
        if (screenSpanX < MinScreenSpanPixels && screenSpanY < MinScreenSpanPixels)
        {
            failureReason =
                $"screen_points_do_not_move:{screenSpanX:F0}x{screenSpanY:F0}px";
            return false;
        }

        (double a, double b, double c) = SolveComponent(
            s0, s1, s2, determinant, static s => s.ScreenX);
        (double d, double e, double f) = SolveComponent(
            s0, s1, s2, determinant, static s => s.ScreenY);

        // Every pair recorded, including the three that were fitted: a residual on
        // those means the arithmetic went wrong rather than that the samples
        // disagree, and either way the transform must not be written.
        double worst = 0;
        foreach (ScreenProjectionSample sample in samples)
        {
            double px = (a * sample.MapDelta.X) + (b * sample.MapDelta.Y) + c;
            double py = (d * sample.MapDelta.X) + (e * sample.MapDelta.Y) + f;
            double residual = Math.Sqrt(
                ((px - sample.ScreenX) * (px - sample.ScreenX)) +
                ((py - sample.ScreenY) * (py - sample.ScreenY)));
            worst = Math.Max(worst, residual);
        }

        if (worst > MaxVerificationResidualPixels)
        {
            failureReason = $"samples_disagree:{worst:F1}px";
            return false;
        }

        // The one coefficient with a meaning outside the fit: a zero offset is the
        // character, and the character is on screen. An anchor off the window is a
        // fit that happens to reproduce its own samples and nothing else — the
        // failure the absolute model had no way to express, so it is expressed
        // here.
        if (!AnchorIsInside(c, f, clientWidth, clientHeight))
        {
            failureReason = $"character_anchor_outside_client:{c:F0},{f:F0}";
            return false;
        }

        calibration = new ScreenProjectionCalibration(
            true, a, b, c, d, e, f,
            clientWidth, clientHeight,
            worst,
            samples.Count - MinimumSamples,
            calibratedAtUtc);
        failureReason = null;
        return true;
    }

    /// <summary>
    /// Projects an offset from the character into a pixel of the client area, in
    /// client-relative coordinates.
    /// </summary>
    /// <param name="mapDelta">
    /// Tiles from the character's square to the target's, target minus character.
    /// An absolute map coordinate passed here projects to a point that is wrong
    /// by the character's own distance from the origin, which is why the parameter
    /// is named for what it is.
    /// </param>
    /// <remarks>
    /// Null when there is no calibration. Not a fallback: falling back is how a
    /// click lands in an arbitrary part of the window.
    /// </remarks>
    public (double X, double Y)? ProjectDelta(MapPoint mapDelta)
        => IsCalibrated
            ? ((A * mapDelta.X) + (B * mapDelta.Y) + C, (D * mapDelta.X) + (E * mapDelta.Y) + F)
            : null;

    /// <summary>Loads the calibration, or returns <see cref="Uncalibrated"/> with a reason.</summary>
    /// <remarks>
    /// A missing file is the state before the operator has calibrated anything,
    /// and it reports as that rather than as a fault.
    /// </remarks>
    public static ScreenProjectionCalibration Load(string path, out string? failureReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        failureReason = null;

        if (!File.Exists(path))
        {
            failureReason = NotCalibratedReason;
            return Uncalibrated;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (IOException ex)
        {
            failureReason = $"screen_projection_unreadable:{ex.GetType().Name}";
            return Uncalibrated;
        }

        if (lines.Length < 2 || !lines[0].StartsWith(Magic, StringComparison.Ordinal))
        {
            failureReason = "screen_projection_header_unrecognised";
            return Uncalibrated;
        }

        string[] header = lines[0].Split(' ');
        if (header.Length != 2
            || !int.TryParse(header[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int version))
        {
            failureReason = "screen_projection_header_unrecognised";
            return Uncalibrated;
        }

        if (version != Version)
        {
            failureReason = $"screen_projection_version_unsupported:{version}";
            return Uncalibrated;
        }

        string[] fields = lines[1].Split(' ');
        if (fields.Length != 11
            || !TryNumber(fields[0], out double a) || !TryNumber(fields[1], out double b)
            || !TryNumber(fields[2], out double c) || !TryNumber(fields[3], out double d)
            || !TryNumber(fields[4], out double e) || !TryNumber(fields[5], out double f)
            || !int.TryParse(fields[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int clientWidth)
            || !int.TryParse(fields[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out int clientHeight)
            || !TryNumber(fields[8], out double residual)
            || !int.TryParse(fields[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out int verified)
            || !DateTime.TryParse(fields[10], CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime at))
        {
            failureReason = "screen_projection_entry_malformed";
            return Uncalibrated;
        }

        // A transform that maps every offset to the same pixel is not a transform,
        // and it is what an all-zero or corrupted file decodes to. The anchor is
        // checked on the way in as well as on the way out: a file edited by hand
        // has had no solver look at it.
        if (clientWidth <= 0 || clientHeight <= 0
            || Math.Abs((a * e) - (b * d)) < 1e-9
            || !AnchorIsInside(c, f, clientWidth, clientHeight))
        {
            failureReason = "screen_projection_entry_malformed";
            return Uncalibrated;
        }

        return new ScreenProjectionCalibration(
            true, a, b, c, d, e, f, clientWidth, clientHeight, residual, verified, at);
    }

    /// <summary>Writes the calibration, creating the directory if needed.</summary>
    /// <exception cref="InvalidOperationException">
    /// When there is nothing to write. Persisting the uncalibrated state would
    /// make the next load report a calibration nobody produced.
    /// </exception>
    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!IsCalibrated)
            throw new InvalidOperationException("There is no calibration to write.");

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var text = new StringBuilder();
        text.Append(Magic).Append(' ').Append(Version).Append('\n');
        foreach (double value in new[] { A, B, C, D, E, F })
            text.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append(' ');
        text
            .Append(ClientWidth.ToString(CultureInfo.InvariantCulture)).Append(' ')
            .Append(ClientHeight.ToString(CultureInfo.InvariantCulture)).Append(' ')
            .Append(WorstResidualPixels.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
            .Append(VerifiedAgainstSamples.ToString(CultureInfo.InvariantCulture)).Append(' ')
            .Append(CalibratedAtUtc!.Value.ToString("O", CultureInfo.InvariantCulture)).Append('\n');

        File.WriteAllText(path, text.ToString());
    }

    /// <summary>
    /// Solves one row of the affine map by Cramer's rule over the three sampled
    /// offsets.
    /// </summary>
    private static (double A, double B, double C) SolveComponent(
        ScreenProjectionSample s0,
        ScreenProjectionSample s1,
        ScreenProjectionSample s2,
        double determinant,
        Func<ScreenProjectionSample, int> screen)
    {
        double v0 = screen(s0), v1 = screen(s1), v2 = screen(s2);

        double a = (((v1 - v0) * (s2.MapDelta.Y - s0.MapDelta.Y)) - ((v2 - v0) * (s1.MapDelta.Y - s0.MapDelta.Y))) / determinant;
        double b = (((v2 - v0) * (s1.MapDelta.X - s0.MapDelta.X)) - ((v1 - v0) * (s2.MapDelta.X - s0.MapDelta.X))) / determinant;
        double c = v0 - (a * s0.MapDelta.X) - (b * s0.MapDelta.Y);
        return (a, b, c);
    }

    /// <summary>Whether the character's own pixel falls inside the window.</summary>
    private static bool AnchorIsInside(double x, double y, int clientWidth, int clientHeight)
        => x >= 0 && x < clientWidth && y >= 0 && y < clientHeight;

    private static bool TryNumber(string field, out double value)
        => double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
