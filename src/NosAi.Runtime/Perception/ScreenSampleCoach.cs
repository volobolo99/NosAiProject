using System.Globalization;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Perception;

/// <summary>What the coach did with one offered sample, and what to do next.</summary>
public sealed record ScreenSampleVerdict(bool Accepted, string? Reason, string Advice);

/// <summary>
/// How well the samples collected so far pin the transform down.
/// </summary>
/// <param name="Spread">
/// The determinant of the centred normal matrix of the tile deltas — how much
/// area the clicks cover. It is the quantity that decides whether the fit has
/// any leverage, and it is reported rather than hidden because an operator who
/// can see it rise knows the session is going somewhere.
/// </param>
public sealed record ScreenSampleCoverage(
    int Accepted, int SectorsFilled, double Spread, int FarthestTiles);

/// <summary>
/// Stands next to the operator while the screen samples are collected, and says
/// what the next click has to look like for the set to determine a transform.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists, measured.</b> On 2026-09-07 a five-click session was
/// refused by <see cref="ScreenProjectionCalibration"/> with
/// <c>scale_not_determined:72x43pct</c>. The clicks were sound — refitted against
/// the older set their worst residual was 1.41 tiles, better than nine of the
/// twelve samples already on file — but they all fell in one direction and two
/// were the identical pixel, so the centred normal matrix of the deltas had a
/// determinant of 150 against the 893 000 of the twelve-click set that had
/// calibrated successfully. Six thousand times less leverage, from clicks that
/// followed the instruction on screen exactly: <i>"5 volte, in direzioni
/// diverse"</i>. The instruction was the defect.
/// </para>
/// <para>
/// <b>The three rules, and what each one is for.</b> A click is refused when the
/// character was still walking (the position it is measured against is read up to
/// one poll late, and a walking character has already left it — this is the
/// systematic error that put six of the twelve old samples above the 1.5-tile
/// residual threshold); when its pixel was already sampled (a repeat adds no
/// geometry but inflates the degrees of freedom, so it makes the fit look better
/// determined than it is); and when it lands nearer than
/// <see cref="MinimumDeltaTiles"/> (a short lever arm measures the pitch through
/// the quantisation noise of a single tile).
/// </para>
/// <para>
/// The coach never fits anything itself: <see cref="TrySolve"/> already owns that
/// arithmetic, and a second copy of it here would be a second thing to be wrong.
/// </para>
/// </remarks>
public sealed class ScreenSampleCoach
{
    /// <summary>
    /// How many samples to ask for. Twelve, because that is the size of the one
    /// set in this repository that ever produced an accepted calibration.
    /// </summary>
    public const int DefaultWantedSamples = 12;

    /// <summary>
    /// The shortest click that carries usable geometry, in tiles.
    /// </summary>
    /// <remarks>
    /// A click resolves to a whole tile, so every sample carries up to half a tile
    /// of quantisation in each axis whatever the operator does. At six tiles that
    /// noise is under a tenth of the lever arm; at two it is a quarter of it.
    /// </remarks>
    public const int MinimumDeltaTiles = 6;

    /// <summary>The eight directions the clicks are asked to cover.</summary>
    public const int SectorCount = 8;

    /// <summary>The character had not finished the previous walk.</summary>
    public const string CharacterWasMovingReason = "character_was_moving";

    /// <summary>That exact pixel is already in the set.</summary>
    public const string PixelAlreadySampledReason = "pixel_already_sampled";

    /// <summary>The click landed too near the character to measure the pitch.</summary>
    public const string TooCloseReason = "delta_below_minimum_tiles";

    private static readonly string[] SectorNames =
    [
        "a destra", "in basso a destra", "in basso", "in basso a sinistra",
        "a sinistra", "in alto a sinistra", "in alto", "in alto a destra"
    ];

    private readonly List<ScreenProjectionSample> _accepted = new();
    private readonly HashSet<(int X, int Y)> _pixels = new();
    private readonly bool[] _sectors = new bool[SectorCount];

    /// <summary>How many accepted samples the coach is waiting for.</summary>
    public int Wanted { get; }

    public ScreenSampleCoach(int wanted = DefaultWantedSamples)
    {
        if (wanted < 3)
            throw new ArgumentOutOfRangeException(
                nameof(wanted), "Tre campioni sono il minimo che determini una mappa affine.");
        Wanted = wanted;
    }

    /// <summary>The samples that passed all three rules, in the order they arrived.</summary>
    public IReadOnlyList<ScreenProjectionSample> Accepted => _accepted;

    /// <summary>Whether enough samples have been accepted to stop asking.</summary>
    public bool IsSatisfied => _accepted.Count >= Wanted;

    /// <summary>
    /// Offers one click to the coach.
    /// </summary>
    /// <param name="characterWasAtRest">
    /// Whether the character had arrived at its previous destination before this
    /// click. Passed in rather than inferred, because only the caller watching the
    /// client can know it, and inferring it here from the sample alone would be a
    /// guess dressed as a measurement.
    /// </param>
    public ScreenSampleVerdict Offer(ScreenProjectionSample sample, bool characterWasAtRest)
    {
        if (!characterWasAtRest)
            return new ScreenSampleVerdict(false, CharacterWasMovingReason, Advice);

        if (_pixels.Contains((sample.ScreenX, sample.ScreenY)))
            return new ScreenSampleVerdict(false, PixelAlreadySampledReason, Advice);

        if (DistanceTiles(sample.MapDelta) < MinimumDeltaTiles)
            return new ScreenSampleVerdict(false, TooCloseReason, Advice);

        _accepted.Add(sample);
        _pixels.Add((sample.ScreenX, sample.ScreenY));
        _sectors[SectorOf(sample.MapDelta)] = true;
        return new ScreenSampleVerdict(true, null, Advice);
    }

    /// <summary>What the operator should do next, in one sentence.</summary>
    public string Advice
    {
        get
        {
            if (IsSatisfied)
                return "Basta così: i campioni bastano, chiudi la raccolta.";

            int missing = Array.IndexOf(_sectors, false);
            if (missing >= 0)
                return string.Create(CultureInfo.InvariantCulture,
                    $"Fermati, poi clicca {SectorNames[missing]}, ad almeno {MinimumDeltaTiles} caselle.");

            // Every direction is covered; what is left is simply more of them, and
            // farther out, because the pitch is measured along the longest arm.
            return string.Create(CultureInfo.InvariantCulture,
                $"Tutte le direzioni sono coperte: fermati e clicca ancora, più lontano che puoi. "
                + $"Ne mancano {Wanted - _accepted.Count}.");
        }
    }

    /// <summary>How much area the accepted clicks cover, for the operator to watch.</summary>
    public ScreenSampleCoverage Coverage
    {
        get
        {
            int filled = _sectors.Count(static x => x);
            int farthest = _accepted.Count == 0
                ? 0
                : (int)Math.Round(_accepted.Max(s => DistanceTiles(s.MapDelta)));
            return new ScreenSampleCoverage(_accepted.Count, filled, Spread(), farthest);
        }
    }

    /// <summary>
    /// Asks <see cref="ScreenProjectionCalibration"/> whether the set solves yet.
    /// </summary>
    /// <remarks>
    /// The same call the operator's <c>--screen-calibrate</c> makes, run against the
    /// samples in hand: what it answers here is what it will answer then, so the
    /// session can stop when the answer is yes instead of finding out afterwards.
    /// </remarks>
    public bool TrySolve(
        int clientWidth, int clientHeight, DateTime calibratedAtUtc,
        out ScreenProjectionCalibration calibration, out string? failureReason,
        DpiAwarenessRegime? regime = null, uint clientDpi = 0) =>
        ScreenProjectionCalibration.TrySolve(
            _accepted, clientWidth, clientHeight, calibratedAtUtc,
            out calibration, out failureReason, regime, clientDpi);

    /// <summary>
    /// The determinant of the centred normal matrix of the tile deltas: the
    /// quantity whose smallness refused the five-click session.
    /// </summary>
    private double Spread()
    {
        if (_accepted.Count < 2) return 0;
        double meanX = _accepted.Average(s => (double)s.MapDelta.X);
        double meanY = _accepted.Average(s => (double)s.MapDelta.Y);
        double sxx = 0, syy = 0, sxy = 0;
        foreach (ScreenProjectionSample s in _accepted)
        {
            double dx = s.MapDelta.X - meanX;
            double dy = s.MapDelta.Y - meanY;
            sxx += dx * dx;
            syy += dy * dy;
            sxy += dx * dy;
        }

        return (sxx * syy) - (sxy * sxy);
    }

    private static double DistanceTiles(MapPoint delta) =>
        Math.Sqrt((double)((delta.X * delta.X) + (delta.Y * delta.Y)));

    /// <summary>
    /// Which of the eight directions a delta points in. Screen y grows downward,
    /// which is why the sector names read "in basso" for a positive y.
    /// </summary>
    private static int SectorOf(MapPoint delta)
    {
        double angle = Math.Atan2(delta.Y, delta.X);
        int sector = (int)Math.Round(angle / (2 * Math.PI / SectorCount));
        return ((sector % SectorCount) + SectorCount) % SectorCount;
    }
}
