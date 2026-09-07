using System.Linq;
using System.Globalization;
using System.Text;
using NosAi.Runtime.Contracts;

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
/// <b>Specifica di una macchina, e versionata lo stesso dal 2026-09-07</b> — la
/// riga di <c>.gitignore</c> che escludeva <c>data/</c> per intero e' stata
/// stretta apposta: senza questi sei numeri nessuna affermazione del repository
/// sulla proiezione si riproduce, e il giorno in cui sono quasi andati persi lo
/// ha reso evidente. Vale per una macchina sola, e va rifatta altrove. Sta in
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
    /// The fewest pairs before the two perspective terms are fitted at all.
    /// </summary>
    /// <remarks>
    /// Eight unknowns need four pairs to be determined and a fifth before the fit
    /// can be checked against anything it did not already reproduce. Under five,
    /// the map stays affine — which is not a compromise but the honest answer:
    /// with four pairs a perspective term is whatever the noise asks for.
    /// </remarks>
    public const int PerspectiveMinimumSamples = 5;

    /// <summary>
    /// Le coppie che una mappa prospettica consuma: otto incognite, due equazioni
    /// per coppia.
    /// </summary>
    /// <remarks>
    /// Distinto da <see cref="PerspectiveMinimumSamples"/>, che è quante ne
    /// servono per <i>controllarla</i>: quattro la determinano e non la
    /// verificano, perché quattro coppie la riproducono sempre in modo esatto.
    /// </remarks>
    public const int PerspectiveMinimumPairs = 4;

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
    /// <para>
    /// 3 to 4 adds the window DPI, which is the storable half of a
    /// <see cref="GeometryEpoch"/> that the client size cannot express: a scale
    /// changed while the rectangle stays the same is the gap
    /// <c>docs/CONTROLLO_PERSONAGGIO_ATTUAZIONE.md</c> § 6.3 named, and it is
    /// invisible to a comparison of width and height. Same reasoning as the bump
    /// before it: a v3 file was written where nothing read the DPI, so it has no
    /// DPI to be compared against, and inventing one from the reader would be the
    /// assertion the field exists to avoid.
    /// </para>
    /// </remarks>
    private const string Magic = "nosai-screen-projection";
    private const int Version = 5;

    /// <summary>
    /// <b>Dalla 4 alla 5, il 2026-09-08</b>: due termini in coda ai sei
    /// coefficienti, <c>G</c> e <c>H</c>, e un conteggio dei campioni scartati in
    /// fondo alla riga. La mappa è passata da affine a prospettica perché la
    /// casella vale più pixel in basso che in alto — misurato su due sessioni
    /// indipendenti, che concordano sul termine verticale. Una v4 resta leggibile:
    /// è la stessa mappa con la prospettiva a zero, ed è esattamente ciò che
    /// quella versione affermava.
    /// </summary>

    /// <summary>
    /// La versione 4 e' la stessa mappa senza i due termini prospettici, cioe'
    /// con entrambi a zero. Si legge ancora: una calibrazione affine e' una
    /// prospettica con la prospettiva a zero, e rifiutarla costringerebbe a
    /// ricalibrare per una differenza che il file sa esprimere.
    /// </summary>
    private const int AffineOnlyVersion = 4;

    private ScreenProjectionCalibration(
        bool isCalibrated,
        double a, double b, double c,
        double d, double e, double f,
        int clientWidth, int clientHeight,
        double worstResidual,
        int verifiedAgainst,
        int discardedSamples,
        DpiAwarenessRegime regime,
        uint clientDpi,
        DateTime? calibratedAtUtc,
        double g = 0,
        double h = 0)
    {
        IsCalibrated = isCalibrated;
        A = a; B = b; C = c;
        D = d; E = e; F = f;
        G = g; H = h;
        ClientWidth = clientWidth;
        ClientHeight = clientHeight;
        WorstResidualPixels = worstResidual;
        VerifiedAgainstSamples = verifiedAgainst;
        DiscardedSamples = discardedSamples;
        Regime = regime;
        ClientDpi = clientDpi;
        CalibratedAtUtc = calibratedAtUtc;
    }

    /// <summary>Whether a real calibration was loaded. False is not "guess one".</summary>
    public bool IsCalibrated { get; }

    /// <summary>Coefficients of <c>screenX = A·Δx + B·Δy + C</c>.</summary>
    public double A { get; }
    public double B { get; }
    public double C { get; }

    /// <summary>
    /// I due termini prospettici del denominatore <c>G·Δx + H·Δy + 1</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Perche' esistono, misurato.</b> Fino al 2026-09-07 questa mappa era
    /// affine, e non tornava: i dodici campioni del 3 settembre lasciavano 2,43
    /// caselle di residuo contro una soglia di 1,5, e undici campioni raccolti il
    /// 7 ne lasciavano 3,77. Dividendo ciascuna delle due sessioni fra i clic
    /// nella meta' alta e quelli nella meta' bassa della finestra, il passo della
    /// casella cambia nella stessa direzione in tutte e due: 30,7 -> 36,6 px in
    /// orizzontale il 3 settembre, 36,9 -> 42,9 il 7. La casella e' piu' grande in
    /// basso e piu' piccola in alto, che e' quello che fa una telecamera inclinata
    /// e che nessuna mappa affine puo' rappresentare.
    /// </para>
    /// <para>
    /// Aggiungendo i due termini il residuo del 3 settembre scende da 2,43 a 1,23
    /// caselle — sotto soglia, con gli stessi campioni che prima venivano
    /// rifiutati — e i due termini che le due sessioni misurano indipendentemente
    /// concordano: <c>H</c> vale -0,0181 e -0,0153, <c>G</c> e' circa zero in
    /// entrambe, come dev'essere per una telecamera inclinata attorno al solo asse
    /// orizzontale.
    /// </para>
    /// </remarks>
    public double G { get; }
    public double H { get; }

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

    /// <summary>The window's DPI when the fit was made, or zero when it was not read.</summary>
    /// <remarks>
    /// <para>
    /// Together with <see cref="ClientWidth"/> and <see cref="ClientHeight"/> this is
    /// the <see cref="GeometryShape"/> the fit belongs to: the part of a geometry epoch
    /// that means anything in a file. The rest of an epoch — the window handle, the
    /// monitor, the position — is session-scoped, and a calibration that stored it
    /// would be refused on every restart.
    /// </para>
    /// <para>
    /// The position is left out on purpose rather than forgotten. A window that moves
    /// keeps its transform, because <see cref="CalibratedScreenProjection"/> adds the
    /// client origin at the moment of use; making a move invalidate a calibration would
    /// throw away a good measurement every time the operator dragged the window.
    /// Movement belongs to the epoch and to the commit point, not here.
    /// </para>
    /// </remarks>
    public uint ClientDpi { get; }

    /// <summary>The storable part of the geometry this fit belongs to.</summary>
    public GeometryShape Shape => new(ClientWidth, ClientHeight, ClientDpi);

    /// <summary>How much independent checking the residual represents.</summary>
    /// <remarks>
    /// <para>
    /// Le coppie oltre quelle che il fit consuma, cioè i gradi di libertà che
    /// restano una volta che ha preso il suo. Zero significa che la calibrazione
    /// è stata risolta da esattamente il minimo, che il minimo riproduce sempre
    /// in modo esatto: il suo residuo di zero non conferma nulla. Utilizzabile, e
    /// vale la pena saperlo.
    /// </para>
    /// <para>
    /// <b>Corretto il 2026-09-08.</b> Questo numero sottraeva sempre tre — le
    /// coppie che serve a una mappa affine — anche quando il fit aveva adottato
    /// gli otto parametri prospettici, che di coppie ne consumano quattro. Il
    /// residuo risultava così verificato contro una coppia in più di quante
    /// l'avessero davvero verificato. Ora la sottrazione segue il modello scelto.
    /// </para>
    public int VerifiedAgainstSamples { get; }

    /// <summary>
    /// Quanti campioni il fit ha lasciato fuori come fuori bersaglio.
    /// </summary>
    /// <remarks>
    /// Zero e' il caso normale. Un numero diverso da zero non invalida la
    /// calibrazione -- il tetto di <see cref="MaxDiscardedFraction"/> l'ha gia'
    /// giudicata -- ma dice a chi la legge che una parte dei campioni non
    /// concordava, il che e' un'informazione sulla sessione e non un dettaglio
    /// interno del solutore.
    /// </remarks>
    public int DiscardedSamples { get; }

    /// <summary>When the operator produced it, or null when uncalibrated.</summary>
    public DateTime? CalibratedAtUtc { get; }

    /// <summary>The state before the operator has calibrated anything.</summary>
    public static ScreenProjectionCalibration Uncalibrated { get; } =
        new(false, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, DpiAwarenessRegime.Unknown, 0, null);

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
        DpiAwarenessRegime? regime = null,
        uint clientDpi = 0,
        int discardedSamples = 0)
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

        // La prospettiva si aggiunge solo se paga. L'affine resta la risposta di
        // partenza, la prospettica viene misurata sugli stessi campioni, e vince
        // solo se il residuo peggiore -- l'unica cosa che decide se la
        // calibrazione viene scritta -- e' davvero piu' piccolo. Un modello con
        // due parametri in piu' che non riduce l'errore sta adattando il rumore.
        double g = 0, h = 0;
        double perspectiveVarianceX = 0, perspectiveVarianceY = 0;
        if (samples.Count >= PerspectiveMinimumSamples
            && TrySolvePerspective(samples, out double[]? projective,
                   out double factorX, out double factorY)
            && WorstTiles(samples, projective![0], projective[1], projective[2],
                   projective[3], projective[4], projective[5], projective[6], projective[7],
                   out double projectiveTiles, out _, out _)
            && WorstTiles(samples, a, b, c, d, e, f, 0, 0, out double affineTiles, out _, out _)
            && projectiveTiles < affineTiles)
        {
            a = projective[0]; b = projective[1]; c = projective[2];
            d = projective[3]; e = projective[4]; f = projective[5];
            g = projective[6]; h = projective[7];
            perspectiveVarianceX = factorX;
            perspectiveVarianceY = factorY;
        }

        // A residual is a pixel distance, but the measurement error is not: what
        // was read back is a tile index, so the disagreement is carried back
        // through the fitted transform and judged in the unit it was made in.
        // Inverting it requires it to be a transform at all -- e con la
        // prospettiva l'inversa cambia da punto a punto, quindi si usa la
        // derivata locale invece della matrice costante.
        if (!WorstTiles(samples, a, b, c, d, e, f, g, h, out double worstTiles, out double worst, out _))
        {
            failureReason = "fitted_transform_collapses_the_plane";
            return false;
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
        // Otto parametri quando la prospettiva e' stata adottata, sei quando no:
        // dividere la somma dei quadrati per i gradi di liberta' sbagliati non e'
        // una approssimazione, e' un'altra grandezza.
        bool perspective = g != 0 || h != 0;
        int parameters = perspective ? 8 : 6;
        int degreesOfFreedom = (2 * samples.Count) - parameters;
        if (degreesOfFreedom > 0)
        {
            double sumOfSquares = 0;
            foreach (ScreenProjectionSample sample in samples)
            {
                double denominator = (g * sample.MapDelta.X) + (h * sample.MapDelta.Y) + 1;
                double px = ((a * sample.MapDelta.X) + (b * sample.MapDelta.Y) + c) / denominator;
                double py = ((d * sample.MapDelta.X) + (e * sample.MapDelta.Y) + f) / denominator;
                sumOfSquares += ((px - sample.ScreenX) * (px - sample.ScreenX))
                                + ((py - sample.ScreenY) * (py - sample.ScreenY));
            }

            // L'errore standard dei due coefficienti di scala, dalla matrice
            // normale da cui il fit e' uscito davvero. Riusare quella affine per
            // un fit a otto parametri darebbe un numero che non descrive nulla:
            // e' la matrice di un altro problema.
            double variance = sumOfSquares / degreesOfFreedom;
            double standardErrorX = perspective
                ? Math.Sqrt(variance * perspectiveVarianceX)
                : Math.Sqrt(variance * syy / determinant);
            double standardErrorY = perspective
                ? Math.Sqrt(variance * perspectiveVarianceY)
                : Math.Sqrt(variance * sxx / determinant);

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
            // Le coppie che il modello adottato consuma: tre per l'affine, quattro
            // per la prospettica. Sottrarne sempre tre gonfierebbe la verifica.
            samples.Count - (g != 0 || h != 0 ? PerspectiveMinimumPairs : MinimumSamples),
            discardedSamples,
            regime ?? DpiAwareness.Current(),
            clientDpi,
            calibratedAtUtc,
            g,
            h);
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
    {
        if (!IsCalibrated)
            return null;

        double denominator = (G * mapDelta.X) + (H * mapDelta.Y) + 1;

        // Il denominatore si annulla sull'orizzonte: la' il piano della mappa e'
        // parallelo alla vista e una casella occupa zero pixel. Nessun pixel e'
        // la risposta giusta, e sceglierne uno qualsiasi manderebbe un clic in un
        // punto arbitrario della finestra.
        if (Math.Abs(denominator) < 1e-6)
            return null;

        return (((A * mapDelta.X) + (B * mapDelta.Y) + C) / denominator,
                ((D * mapDelta.X) + (E * mapDelta.Y) + F) / denominator);
    }

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

        if (version != Version && version != AffineOnlyVersion)
        {
            failureReason = $"screen_projection_version_unsupported:{version}";
            return Uncalibrated;
        }

        // La v4 non porta i due termini prospettici, e quello che manca vale zero:
        // e' esattamente cio' che quella versione affermava.
        int perspectiveFields = version == Version ? 2 : 0;
        double g = 0, h = 0;
        int discardedRead = 0;
        string[] fields = lines[1].Split(' ');
        if (perspectiveFields == 2
            && (fields.Length != 16
                || !TryNumber(fields[6], out g) || !TryNumber(fields[7], out h)
                || !int.TryParse(fields[15], NumberStyles.Integer, CultureInfo.InvariantCulture,
                       out discardedRead)))
        {
            failureReason = "screen_projection_entry_malformed";
            return Uncalibrated;
        }

        if (perspectiveFields == 2)
            fields = fields.Take(6).Concat(fields.Skip(8).Take(7)).ToArray();

        if (fields.Length != 13
            || !TryNumber(fields[0], out double a) || !TryNumber(fields[1], out double b)
            || !TryNumber(fields[2], out double c) || !TryNumber(fields[3], out double d)
            || !TryNumber(fields[4], out double e) || !TryNumber(fields[5], out double f)
            || !int.TryParse(fields[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int clientWidth)
            || !int.TryParse(fields[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out int clientHeight)
            || !TryNumber(fields[8], out double residual)
            || !int.TryParse(fields[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out int verified)
            || !uint.TryParse(fields[11], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint clientDpi)
            || !DateTime.TryParse(fields[12], CultureInfo.InvariantCulture,
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
            true, a, b, c, d, e, f, clientWidth, clientHeight, residual, verified, discardedRead,
            regime, clientDpi, at, g, h);
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
        foreach (double value in new[] { A, B, C, D, E, F, G, H })
            text.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append(' ');
        text
            .Append(ClientWidth.ToString(CultureInfo.InvariantCulture)).Append(' ')
            .Append(ClientHeight.ToString(CultureInfo.InvariantCulture)).Append(' ')
            .Append(WorstResidualPixels.ToString("R", CultureInfo.InvariantCulture)).Append(' ')
            .Append(VerifiedAgainstSamples.ToString(CultureInfo.InvariantCulture)).Append(' ')
            .Append(Regime.ToWire()).Append(' ')
            .Append(ClientDpi.ToString(CultureInfo.InvariantCulture)).Append(' ')
            .Append(CalibratedAtUtc!.Value.ToString("O", CultureInfo.InvariantCulture)).Append(' ')
            .Append(DiscardedSamples.ToString(CultureInfo.InvariantCulture)).Append('\n');

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

    /// <summary>
    /// Fits the eight coefficients of the projective map by least squares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ogni coppia da' due equazioni lineari nelle otto incognite, ottenute
    /// moltiplicando per il denominatore: <c>u·(G·Δx + H·Δy + 1) = A·Δx + B·Δy + C</c>
    /// e altrettanto per <c>v</c>. E' la forma diretta, quella che minimizza
    /// l'errore algebrico e non quello geometrico -- differenza che conta quando i
    /// punti sono quasi sull'orizzonte, e che qui non conta: i clic stanno dentro
    /// una finestra di 1024x768 e il denominatore misurato sulle sessioni reali
    /// resta fra 0,7 e 1,3.
    /// </para>
    /// <para>
    /// Il risultato viene comunque giudicato in caselle da <see cref="WorstTiles"/>,
    /// cioe' sull'errore che l'operatore vedrebbe, e adottato solo se batte
    /// l'affine su quella misura. Un fit che minimizza una cosa e viene accettato
    /// su un'altra e' onesto solo se e' la seconda a decidere.
    /// </para>
    /// </remarks>
    private static bool TrySolvePerspective(
        IReadOnlyList<ScreenProjectionSample> samples,
        out double[]? coefficients,
        out double varianceFactorX,
        out double varianceFactorY)
    {
        coefficients = null;
        varianceFactorX = 0;
        varianceFactorY = 0;

        var normal = new double[8, 9];
        Span<double> row = stackalloc double[8];

        foreach (ScreenProjectionSample sample in samples)
        {
            double x = sample.MapDelta.X;
            double y = sample.MapDelta.Y;

            foreach (bool horizontal in new[] { true, false })
            {
                double target = horizontal ? sample.ScreenX : sample.ScreenY;
                row.Clear();
                row[horizontal ? 0 : 3] = x;
                row[horizontal ? 1 : 4] = y;
                row[horizontal ? 2 : 5] = 1;
                row[6] = -target * x;
                row[7] = -target * y;

                for (int i = 0; i < 8; i++)
                {
                    for (int j = 0; j < 8; j++)
                        normal[i, j] += row[i] * row[j];
                    normal[i, 8] += row[i] * target;
                }
            }
        }

        // La copia serve perche' l'eliminazione consuma la matrice, e la stessa
        // matrice serve dopo: la diagonale della sua inversa dice quanto sono
        // determinati i coefficienti, che e' una domanda diversa da quanto bene
        // riproducono i campioni.
        var forInverse = (double[,])normal.Clone();
        if (!TrySolveInPlace(normal, out double[] solved))
            return false;
        if (!TryInvertDiagonal(forInverse, out varianceFactorX, out varianceFactorY))
            return false;

        // Un denominatore che cambia segno fra un campione e l'altro vuol dire che
        // il piano passa dietro la telecamera: aritmeticamente una soluzione,
        // geometricamente niente.
        foreach (ScreenProjectionSample sample in samples)
        {
            double denominator = (solved[6] * sample.MapDelta.X) + (solved[7] * sample.MapDelta.Y) + 1;
            if (denominator <= 0.1)
                return false;
        }

        coefficients = solved;
        return true;
    }

    /// <summary>
    /// The two diagonal entries of the inverse normal matrix that belong to the
    /// horizontal and vertical scale coefficients.
    /// </summary>
    /// <remarks>
    /// Sono i due moltiplicatori che, per la varianza dei residui, danno la
    /// varianza di quei coefficienti. Si ottengono risolvendo la normale contro le
    /// colonne della matrice identita' corrispondenti -- <c>A</c> e' l'incognita 0,
    /// <c>E</c> la 4 -- senza mai formare l'inversa intera, che non serve.
    /// </remarks>
    private static bool TryInvertDiagonal(double[,] normal, out double forA, out double forE)
    {
        forA = 0;
        forE = 0;

        foreach (int index in new[] { 0, 4 })
        {
            var augmented = new double[8, 9];
            for (int i = 0; i < 8; i++)
            {
                for (int j = 0; j < 8; j++)
                    augmented[i, j] = normal[i, j];
                augmented[i, 8] = i == index ? 1 : 0;
            }

            if (!TrySolveInPlace(augmented, out double[] column))
                return false;
            if (column[index] <= 0)
                return false;

            if (index == 0) forA = column[0];
            else forE = column[4];
        }

        return true;
    }

    /// <summary>Gaussian elimination with partial pivoting on an 8x9 augmented matrix.</summary>
    private static bool TrySolveInPlace(double[,] matrix, out double[] solution)
    {
        const int n = 8;
        solution = new double[n];

        for (int column = 0; column < n; column++)
        {
            int pivot = column;
            for (int r = column + 1; r < n; r++)
                if (Math.Abs(matrix[r, column]) > Math.Abs(matrix[pivot, column]))
                    pivot = r;

            if (Math.Abs(matrix[pivot, column]) < 1e-12)
                return false;

            if (pivot != column)
                for (int j = column; j <= n; j++)
                    (matrix[column, j], matrix[pivot, j]) = (matrix[pivot, j], matrix[column, j]);

            double divisor = matrix[column, column];
            for (int j = column; j <= n; j++)
                matrix[column, j] /= divisor;

            for (int r = 0; r < n; r++)
            {
                if (r == column) continue;
                double factor = matrix[r, column];
                if (factor == 0) continue;
                for (int j = column; j <= n; j++)
                    matrix[r, j] -= factor * matrix[column, j];
            }
        }

        for (int i = 0; i < n; i++)
        {
            if (double.IsNaN(matrix[i, n]) || double.IsInfinity(matrix[i, n]))
                return false;
            solution[i] = matrix[i, n];
        }

        return true;
    }

    /// <summary>
    /// The worst disagreement between the map and its samples, in tiles and in
    /// pixels, or false when the map cannot be inverted where a sample sits.
    /// </summary>
    /// <remarks>
    /// Con la prospettiva l'inversa non e' una matrice sola: quanti pixel vale una
    /// casella dipende da dove si guarda. L'errore in pixel viene quindi riportato
    /// in caselle attraverso la derivata della mappa <i>in quel campione</i>, che
    /// per un campione affine si riduce esattamente alla matrice costante di
    /// prima.
    /// </remarks>
    private static bool WorstTiles(
        IReadOnlyList<ScreenProjectionSample> samples,
        double a, double b, double c, double d, double e, double f, double g, double h,
        out double worstTiles, out double worstPixels, out int worstIndex)
    {
        worstTiles = 0;
        worstPixels = 0;
        worstIndex = -1;

        for (int index = 0; index < samples.Count; index++)
        {
            ScreenProjectionSample sample = samples[index];
            double x = sample.MapDelta.X;
            double y = sample.MapDelta.Y;

            double denominator = (g * x) + (h * y) + 1;
            if (Math.Abs(denominator) < 1e-6)
                return false;

            double numeratorX = (a * x) + (b * y) + c;
            double numeratorY = (d * x) + (e * y) + f;
            double projectedX = numeratorX / denominator;
            double projectedY = numeratorY / denominator;

            double errorX = projectedX - sample.ScreenX;
            double errorY = projectedY - sample.ScreenY;
            worstPixels = Math.Max(worstPixels, Math.Sqrt((errorX * errorX) + (errorY * errorY)));

            // Derivata della mappa nel campione: le due colonne dicono di quanti
            // pixel si sposta il punto per una casella in x e per una in y.
            double duDx = ((a * denominator) - (numeratorX * g)) / (denominator * denominator);
            double duDy = ((b * denominator) - (numeratorX * h)) / (denominator * denominator);
            double dvDx = ((d * denominator) - (numeratorY * g)) / (denominator * denominator);
            double dvDy = ((e * denominator) - (numeratorY * h)) / (denominator * denominator);

            double jacobian = (duDx * dvDy) - (duDy * dvDx);
            if (Math.Abs(jacobian) < 1e-9)
                return false;

            double tileX = ((dvDy * errorX) - (duDy * errorY)) / jacobian;
            double tileY = ((duDx * errorY) - (dvDx * errorX)) / jacobian;
            double tiles = Math.Sqrt((tileX * tileX) + (tileY * tileY));
            if (tiles > worstTiles)
            {
                worstTiles = tiles;
                worstIndex = index;
            }
        }

        return true;
    }

    /// <summary>Whether the character's own pixel falls inside the window.</summary>
    private static bool AnchorIsInside(double x, double y, int clientWidth, int clientHeight)
        => x >= 0 && x < clientWidth && y >= 0 && y < clientHeight;

    private static bool TryNumber(string field, out double value)
        => double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
