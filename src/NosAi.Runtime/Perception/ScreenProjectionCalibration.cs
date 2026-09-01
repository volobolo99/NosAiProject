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
/// names. Three non-collinear pairs determine all six and assume nothing.
/// </para>
/// <para>
/// <b>And why every sample is fitted.</b> Samples beyond the third used to be held
/// back rather than fitted, so that the residual measured something the solve had
/// not already been given. The reasoning was right and the arrangement was the
/// wrong way round: three pairs determine six unknowns <i>exactly</i>, so whatever
/// error those three carried silently became the definition of the transform, and
/// the held-back samples were then judged against it. On the real client that
/// turned six readings agreeing to within one tile into a reported disagreement of
/// 218 px. The fit is now least squares over every sample, which keeps the
/// residual falsifiable for the reason that actually makes it so: there are more
/// samples than unknowns, so the solve cannot reproduce all of them and a wrong
/// model has nowhere to hide.
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
    /// How far a sample may land from where the fitted transform puts it, measured
    /// in map tiles rather than in pixels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why tiles.</b> This was six pixels, which was the right unit only while
    /// an operator placed the cursor by hand: the pair was a pixel a person aimed
    /// at and a coordinate they read, so a pixel or two was the procedure rather
    /// than an error. The auto-calibrator does not measure in pixels at all — it
    /// reads a tile index back from the client, so a tile is what the error is
    /// made of. On the real client one tile is about 32x15 px, so six pixels asked
    /// for a fifth of a tile: a tolerance finer than the resolution of its own
    /// evidence, which refuses correct fits and cannot be met by any amount of
    /// care.
    /// </para>
    /// <para>
    /// <b>Where the number comes from.</b> Two half-tiles, both inherent to the
    /// method: the clicked pixel may lie anywhere inside the tile the client
    /// resolves it to, and the character is drawn at a smooth position inside the
    /// tile whose integer index is all that memory holds. That is a whole tile per
    /// axis before anything has gone wrong. The margin over it is deliberately
    /// small, and it still catches what this check exists for: the absolute model,
    /// and a client resolving clicks to a waypoint rather than to a destination,
    /// miss by tens of tiles and not by one.
    /// </para>
    /// </remarks>
    public const double MaxVerificationResidualTiles = 1.5;

    /// <summary>
    /// How uncertain the measured size of a tile may be before the samples are
    /// declared not to determine the transform, as a fraction of that size.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A small residual is not the same as a determined answer.</b> The residual
    /// says the fitted transform reproduces the pairs it was given. It says nothing
    /// about how much the answer would move if the pairs moved by the noise they
    /// are known to carry, and when the offsets are small that movement is
    /// enormous: an error of one tile on an offset of four is a quarter of the
    /// signal, and the scale that comes out is worth about as much.
    /// </para>
    /// <para>
    /// <b>Measured on the live client.</b> Two runs of the auto-calibrator minutes
    /// apart, at the same window size, agreed on five of eight readings and still
    /// produced tile sizes of 37 px and 56 px — half as big again. Both passed the
    /// residual check. What separates them from a usable calibration is not the
    /// residual but the standard error of the fit, which was 25% and 19% of the
    /// tile size: the samples simply did not contain the answer. A click computed
    /// from either would land tiles away from where it was aimed, and the cycle
    /// would learn that only after acting.
    /// </para>
    /// <para>
    /// <b>Five per cent, and it comes from what the click has to do.</b> These
    /// clicks are aimed up to about ten tiles from the character, and a click has
    /// to land on the tile it was aimed at, so the scale may be wrong by at most
    /// half a tile over that distance: half of ten is a twentieth. The bar is not
    /// set where the measurements happen to fall. It was ten per cent first, and
    /// the run that squeezed under it at 5.8% would still have put a ten-tile click
    /// almost a whole tile wide - accepted, and useless for the one thing it is
    /// for.
    /// </para>
    /// <para>
    /// It is also attainable, which is the other half of choosing a threshold:
    /// simulated over the probe ring this command uses, with a whole tile of slip
    /// on every pair, the fit comes out near three per cent. So what this refuses
    /// is sampling that failed, not sampling that has noise in it.
    /// </para>
    /// </remarks>
    public const double MaxScaleUncertainty = 0.05;

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

    /// <summary>Reported when a file from an earlier model is found.</summary>
    /// <remarks>
    /// <para>
    /// The bump from 1 to 2 is not cosmetic. A v1 file holds coefficients fitted
    /// to absolute map coordinates; read as offsets they project to a pixel that
    /// is wrong by the character's whole distance from the map origin, and the
    /// click still lands inside the window, so nothing downstream would notice.
    /// Refusing by version is what stops a stale file from being silently
    /// reinterpreted into a real click somewhere nobody chose.
    /// </para>
    /// <para>
    /// 2 to 3 adds the DPI awareness regime the fit was estimated under. A v2 file
    /// does not merely lack the field: it was written by a build that could not
    /// have checked it, so its numbers are in an unrecorded unit. Defaulting the
    /// missing field to whatever the reader is running under would assert exactly
    /// the thing the field exists to establish.
    /// </para>
    /// </remarks>
    private const string Magic = "nosai-screen-projection";
    private const int Version = 3;

    private ScreenProjectionCalibration(
        bool isCalibrated,
        double a, double b, double c,
        double d, double e, double f,
        int clientWidth, int clientHeight,
        double worstResidual,
        int verifiedAgainst,
        DpiAwarenessRegime regime,
        DateTime? calibratedAtUtc)
    {
        IsCalibrated = isCalibrated;
        A = a; B = b; C = c;
        D = d; E = e; F = f;
        ClientWidth = clientWidth;
        ClientHeight = clientHeight;
        WorstResidualPixels = worstResidual;
        VerifiedAgainstSamples = verifiedAgainst;
        Regime = regime;
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

    /// <summary>
    /// How far the worst sample landed from where the fit puts it, in pixels.
    /// </summary>
    /// <remarks>
    /// Recorded in pixels because that is what it is; it is <i>judged</i> in tiles,
    /// against <see cref="MaxVerificationResidualTiles"/>, because that is the unit
    /// the samples were measured in.
    /// </remarks>
    public double WorstResidualPixels { get; }

    /// <summary>
    /// The DPI awareness regime the process was running under when this was fitted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every number in this file is a pixel coordinate, and which pixels those are
    /// is decided by the regime: an unaware process reads a window as 1536x912 where
    /// an aware one reads 1920x1140, on a display at 125%. The transform is
    /// meaningless outside the unit it was measured in, and nothing else in the file
    /// records that unit.
    /// </para>
    /// <para>
    /// It is not derivable from the other fields, which is why it has to be stored.
    /// The client width and height do change with the regime on a scaled display, so
    /// the size comparison catches the change there by accident — but at 100% they
    /// are identical in both regimes and it catches nothing, and between the two
    /// aware regimes they are identical at every scale.
    /// </para>
    /// </remarks>
    public DpiAwarenessRegime Regime { get; }

    /// <summary>How much independent checking the residual represents.</summary>
    /// <remarks>
    /// Samples beyond the three a general affine map needs, so: the degrees of
    /// freedom left over once the fit has taken what it needs. Zero means the
    /// calibration was solved from exactly three pairs, which three pairs always
    /// reproduce exactly, so its residual of zero confirms nothing. Usable, and
    /// worth knowing.
    /// </remarks>
    public int VerifiedAgainstSamples { get; }

    /// <summary>When the operator produced it, or null when uncalibrated.</summary>
    public DateTime? CalibratedAtUtc { get; }

    /// <summary>The state before the operator has calibrated anything.</summary>
    public static ScreenProjectionCalibration Uncalibrated { get; } =
        new(false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, DpiAwarenessRegime.Unknown, null);

    /// <summary>
    /// Solves the transform from the operator's samples, or says why it cannot.
    /// </summary>
    /// <remarks>
    /// The first three samples determine the map; every sample after them is
    /// projected through it and checked. A calibration that cannot predict a pair
    /// the operator actually recorded is not written.
    /// </remarks>
    /// <param name="regime">
    /// The DPI awareness regime the samples were measured under. Defaulted to the
    /// calling process's own regime, because that is what it is in every real call;
    /// it is a parameter so a test can state a regime instead of inheriting whatever
    /// the test host happens to run under.
    /// </param>
    public static bool TrySolve(
        IReadOnlyList<ScreenProjectionSample> samples,
        int clientWidth,
        int clientHeight,
        DateTime calibratedAtUtc,
        out ScreenProjectionCalibration calibration,
        out string? failureReason,
        DpiAwarenessRegime? regime = null)
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

        // Least squares over every sample, on offsets centred about their own
        // mean: centring keeps the scale part of the solve independent of where
        // the anchor lands.
        double meanX = samples.Average(s => (double)s.MapDelta.X);
        double meanY = samples.Average(s => (double)s.MapDelta.Y);

        double sxx = 0, sxy = 0, syy = 0;
        foreach (ScreenProjectionSample s in samples)
        {
            double dx = s.MapDelta.X - meanX;
            double dy = s.MapDelta.Y - meanY;
            sxx += dx * dx;
            sxy += dx * dy;
            syy += dy * dy;
        }

        // Zero area means the offsets lie on a line, and a line cannot fix a
        // mapping of the plane: every sample walked along the same axis and one
        // has to cross it. Judged against the spread rather than against an
        // absolute number, so it does not depend on how far the samples walked.
        double determinant = (sxx * syy) - (sxy * sxy);
        double spread = sxx + syy;

        if (spread <= 0 || determinant <= 1e-9 * spread * spread)
        {
            failureReason = "samples_are_collinear";
            return false;
        }

        // The screen points have to move too, or there is nothing to measure a
        // scale against. This survives from the absolute model, where it was the
        // check that finally caught it: the samples were the character’s own
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
            samples, meanX, meanY, sxx, sxy, syy, determinant, static s => s.ScreenX);
        (double d, double e, double f) = SolveComponent(
            samples, meanX, meanY, sxx, sxy, syy, determinant, static s => s.ScreenY);

        // A residual is a pixel distance, but the measurement error is not: what
        // was read back is a tile index, so the disagreement is carried back
        // through the fitted transform and judged in the unit it was made in.
        // Inverting it requires it to be a transform at all.
        double transformDeterminant = (a * e) - (b * d);
        if (Math.Abs(transformDeterminant) < 1e-9)
        {
            failureReason = "fitted_transform_collapses_the_plane";
            return false;
        }

        double worst = 0;
        double worstTiles = 0;
        foreach (ScreenProjectionSample sample in samples)
        {
            double px = (a * sample.MapDelta.X) + (b * sample.MapDelta.Y) + c;
            double py = (d * sample.MapDelta.X) + (e * sample.MapDelta.Y) + f;
            double errorX = px - sample.ScreenX;
            double errorY = py - sample.ScreenY;

            worst = Math.Max(worst, Math.Sqrt((errorX * errorX) + (errorY * errorY)));

            double tileX = ((e * errorX) - (b * errorY)) / transformDeterminant;
            double tileY = ((a * errorY) - (d * errorX)) / transformDeterminant;
            worstTiles = Math.Max(worstTiles, Math.Sqrt((tileX * tileX) + (tileY * tileY)));
        }

        if (worstTiles > MaxVerificationResidualTiles)
        {
            failureReason = string.Create(CultureInfo.InvariantCulture,
                $"samples_disagree:{worstTiles:F2}tiles_{worst:F0}px");
            return false;
        }

        // How much the answer would move if the samples moved by the noise they
        // carry. Six unknowns from 2n equations leaves 2n-6 degrees of freedom to
        // estimate that noise from; at exactly three pairs there are none, the fit
        // reproduces its input, and there is nothing to say - which is what
        // VerifiedAgainstSamples reports as zero.
        int degreesOfFreedom = (2 * samples.Count) - 6;
        if (degreesOfFreedom > 0)
        {
            double sumOfSquares = 0;
            foreach (ScreenProjectionSample sample in samples)
            {
                double px = (a * sample.MapDelta.X) + (b * sample.MapDelta.Y) + c;
                double py = (d * sample.MapDelta.X) + (e * sample.MapDelta.Y) + f;
                sumOfSquares += ((px - sample.ScreenX) * (px - sample.ScreenX))
                                + ((py - sample.ScreenY) * (py - sample.ScreenY));
            }

            // Standard error of the two scale coefficients, from the same centred
            // normal matrix the fit came out of.
            double variance = sumOfSquares / degreesOfFreedom;
            double standardErrorX = Math.Sqrt(variance * syy / determinant);
            double standardErrorY = Math.Sqrt(variance * sxx / determinant);

            double pitchX = Math.Sqrt((a * a) + (d * d));
            double pitchY = Math.Sqrt((b * b) + (e * e));
            if (pitchX <= 0 || pitchY <= 0)
            {
                failureReason = "fitted_transform_collapses_the_plane";
                return false;
            }

            double uncertaintyX = standardErrorX / pitchX;
            double uncertaintyY = standardErrorY / pitchY;

            if (uncertaintyX > MaxScaleUncertainty || uncertaintyY > MaxScaleUncertainty)
            {
                failureReason = string.Create(CultureInfo.InvariantCulture,
                    $"scale_not_determined:{uncertaintyX * 100:F0}x{uncertaintyY * 100:F0}pct");
                return false;
            }
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
            regime ?? DpiAwareness.Current(),
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
        if (fields.Length != 12
            || !TryNumber(fields[0], out double a) || !TryNumber(fields[1], out double b)
            || !TryNumber(fields[2], out double c) || !TryNumber(fields[3], out double d)
            || !TryNumber(fields[4], out double e) || !TryNumber(fields[5], out double f)
            || !int.TryParse(fields[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int clientWidth)
            || !int.TryParse(fields[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out int clientHeight)
            || !TryNumber(fields[8], out double residual)
            || !int.TryParse(fields[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out int verified)
            || !DateTime.TryParse(fields[11], CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime at))
        {
            failureReason = "screen_projection_entry_malformed";
            return Uncalibrated;
        }

        // A regime token this build does not recognise reads as Unknown rather than
        // as anything in particular, and Unknown never matches a live regime, so the
        // calibration is refused at use instead of being read in the wrong unit.
        DpiAwarenessRegime regime = DpiAwareness.FromWire(fields[10]);

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
            true, a, b, c, d, e, f, clientWidth, clientHeight, residual, verified, regime, at);
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
            .Append(Regime.ToWire()).Append(' ')
            .Append(CalibratedAtUtc!.Value.ToString("O", CultureInfo.InvariantCulture)).Append('\n');

        File.WriteAllText(path, text.ToString());
    }

    /// <summary>
    /// Solves one row of the affine map by Cramer's rule over the three sampled
    /// offsets.
    /// </summary>
    /// <summary>
    /// Least-squares fit of one screen component against the offsets.
    /// </summary>
    /// <remarks>
    /// With exactly three non-collinear samples this reproduces the exact solve it
    /// replaces, so the minimum case is unchanged; beyond three it averages rather
    /// than interpolates.
    /// </remarks>
    private static (double A, double B, double C) SolveComponent(
        IReadOnlyList<ScreenProjectionSample> samples,
        double meanX,
        double meanY,
        double sxx,
        double sxy,
        double syy,
        double determinant,
        Func<ScreenProjectionSample, int> screen)
    {
        double meanV = samples.Average(s => (double)screen(s));

        double tx = 0, ty = 0;
        foreach (ScreenProjectionSample s in samples)
        {
            double dv = screen(s) - meanV;
            tx += (s.MapDelta.X - meanX) * dv;
            ty += (s.MapDelta.Y - meanY) * dv;
        }

        double a = ((syy * tx) - (sxy * ty)) / determinant;
        double b = ((sxx * ty) - (sxy * tx)) / determinant;
        double c = meanV - (a * meanX) - (b * meanY);
        return (a, b, c);
    }

    /// <summary>Whether the character's own pixel falls inside the window.</summary>
    private static bool AnchorIsInside(double x, double y, int clientWidth, int clientHeight)
        => x >= 0 && x < clientWidth && y >= 0 && y < clientHeight;

    private static bool TryNumber(string field, out double value)
        => double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
