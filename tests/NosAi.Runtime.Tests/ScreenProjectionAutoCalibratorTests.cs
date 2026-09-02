using NosAi.Runtime.Perception;
using Xunit;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The unattended half of F2-3: which pixels get clicked, and which sets of
/// readings are allowed to become a transform.
/// </summary>
/// <remarks>
/// Nobody reads the output of this path before it writes, so every judgement it
/// makes has to hold on its own. The cases below are the ones a real run on the
/// live client got wrong.
/// </remarks>
public sealed class ScreenProjectionAutoCalibratorTests
{
    private static readonly PixelRect Client = new(852, 99, 1024, 768);

    /// <summary>
    /// The readings the command actually took on 1 Sep 2026, second run. Four of
    /// the five agree with the first run's readings at the same pixels; the fifth
    /// disagrees with its own counterpart by eleven tiles.
    /// </summary>
    private static List<ScreenProjectionSample> LiveRunThatMustNotFit() =>
    [
        new(new MapPoint(4, 1), 654, 443),
        new(new MapPoint(1, 7), 532, 536),
        new(new MapPoint(-3, 3), 390, 478),
        new(new MapPoint(-5, -5), 370, 325),
        new(new MapPoint(-2, -4), 492, 232),
    ];

    /// <summary>
    /// The run this floor was added after. Five pairs, one of them contradicted
    /// by the rest: dropping it leaves four, four nearly determine six unknowns,
    /// and the fit that came out was rotated by about thirty degrees and reported
    /// a residual of 0.74 of a tile. A calibration nothing is left to check is
    /// worse than no calibration, because every click after it lands somewhere
    /// nobody chose and the cycle finds out only after acting.
    /// </summary>
    [Fact]
    public void A_set_that_only_agrees_once_it_is_cut_below_the_floor_is_refused()
    {
        Assert.False(ScreenProjectionAutoCalibrator.Solve(
            LiveRunThatMustNotFit(), Client,
            out ScreenProjectionCalibration calibration, out int dropped, out string? reason));

        Assert.False(calibration.IsCalibrated);
        Assert.Equal(0, dropped);
        Assert.NotNull(reason);
    }

    /// <summary>
    /// And with enough pairs to spare, one bad click is still survivable: the
    /// largest agreeing set is fitted and the odd one out is dropped.
    /// </summary>
    [Fact]
    public void One_click_that_hit_an_obstacle_is_dropped_and_the_rest_are_fitted()
    {
        List<ScreenProjectionSample> samples = Measured(
            (10, 3), (4, 11), (-6, 9), (-11, 2), (-9, -6), (-2, -11), (6, -9));

        // A click that walked somewhere other than where it was aimed.
        samples[3] = new ScreenProjectionSample(samples[3].MapDelta, 120, 700);

        Assert.True(ScreenProjectionAutoCalibrator.Solve(
            samples, Client,
            out ScreenProjectionCalibration calibration, out int dropped, out string? reason), reason);

        Assert.True(calibration.IsCalibrated);
        Assert.Equal(1, dropped);
        Assert.Equal(ScreenProjectionAutoCalibrator.MinimumFittedSamples, samples.Count - dropped);
    }

    /// <summary>
    /// Nothing is dropped when nothing disagrees. Dropping a sample that fits is
    /// throwing away evidence, and it is the evidence that makes the residual mean
    /// anything.
    /// </summary>
    [Fact]
    public void Samples_that_agree_are_all_kept()
    {
        List<ScreenProjectionSample> samples = Measured(
            (10, 3), (4, 11), (-6, 9), (-11, 2), (-9, -6), (-2, -11), (6, -9));

        Assert.True(ScreenProjectionAutoCalibrator.Solve(
            samples, Client, out ScreenProjectionCalibration calibration, out int dropped, out string? reason), reason);

        Assert.Equal(0, dropped);
        Assert.Equal(samples.Count - 3, calibration.VerifiedAgainstSamples);
    }

    [Fact]
    public void Fewer_pairs_than_the_floor_is_refused_by_name()
    {
        List<ScreenProjectionSample> samples = Measured((10, 3), (4, 11), (-6, 9), (-11, 2), (-9, -6));

        Assert.False(ScreenProjectionAutoCalibrator.Solve(
            samples, Client, out _, out _, out string? reason));

        Assert.Equal("not_enough_samples:5_of_6", reason);
    }

    /// <summary>
    /// The two full runs that produced tile sizes of 37 px and 56 px minutes apart
    /// on the same client. Each of them was written to disk at the time: the search
    /// for an agreeing subset found one, and nothing then asked whether that subset
    /// determined anything. Both must now come out refused, because a calibration
    /// that does not survive being taken twice is not a measurement.
    /// </summary>
    [Fact]
    public void Two_runs_that_disagreed_with_each_other_are_both_refused()
    {
        var passA = new List<ScreenProjectionSample>
        {
            new(new MapPoint(2, -1), 590, 416),
            new(new MapPoint(3, 3), 606, 506),
            new(new MapPoint(0, 2), 523, 468),
            new(new MapPoint(-1, 5), 453, 526),
            new(new MapPoint(-2, 0), 445, 435),
            new(new MapPoint(-4, -2), 360, 404),
            new(new MapPoint(-3, -6), 434, 352),
            new(new MapPoint(0, -2), 418, 262),
            new(new MapPoint(1, -1), 664, 364),
        };

        var passB = new List<ScreenProjectionSample>
        {
            new(new MapPoint(2, 4), 606, 506),
            new(new MapPoint(1, 3), 523, 468),
            new(new MapPoint(-1, 5), 453, 526),
            new(new MapPoint(-2, 0), 445, 435),
            new(new MapPoint(-4, -2), 360, 404),
            new(new MapPoint(-3, -6), 434, 352),
            new(new MapPoint(-2, -3), 418, 262),
            new(new MapPoint(1, -1), 664, 364),
        };

        foreach (List<ScreenProjectionSample> samples in new[] { passA, passB })
        {
            Assert.False(ScreenProjectionAutoCalibrator.Solve(
                samples, Client,
                out ScreenProjectionCalibration calibration, out _, out string? reason),
                $"a calibration was produced where none is determined: {reason}");

            Assert.False(calibration.IsCalibrated);
        }
    }

    // ------------------------------------------------------------ the pixels

    /// <summary>
    /// The probes have to reach, and reach further sideways than down.
    /// </summary>
    /// <remarks>
    /// A tighter second ring was tried and removed: an offset of two or three tiles
    /// is mostly quantisation, so a near probe cannot pin the scale down and costs a
    /// far probe that could have. Then the circle went too, for the same reason in
    /// the other direction - a tile is about twice as wide as it is high, so equal
    /// pixels sideways buy half the tiles, and the horizontal reach was what the
    /// live client kept failing to determine.
    /// </remarks>
    [Fact]
    public void The_probe_points_reach_wide_enough_to_measure_a_scale()
    {
        IReadOnlyList<(int X, int Y)> points =
            ScreenProjectionAutoCalibrator.ProbePoints(Client, ScreenProjectionAutoCalibrator.SampleCount);

        Assert.Equal(ScreenProjectionAutoCalibrator.SampleCount, points.Count);

        int spanX = points.Max(p => p.X) - points.Min(p => p.X);
        int spanY = points.Max(p => p.Y) - points.Min(p => p.Y);

        Assert.True(spanX > Client.Width * 0.6, $"a horizontal reach of {spanX} px is too tight");
        Assert.True(spanY > Client.Height * 0.5, $"a vertical reach of {spanY} px is too tight");

        // Wider than tall, because a tile is.
        Assert.True(spanX > spanY, "the probes reach no further sideways than down");
    }

    /// <summary>
    /// A probe point outside the rendered world is a click on the interface, and a
    /// click on the interface does not walk anywhere.
    /// </summary>
    [Fact]
    public void Every_probe_point_is_inside_the_client_area()
    {
        IReadOnlyList<(int X, int Y)> points =
            ScreenProjectionAutoCalibrator.ProbePoints(Client, ScreenProjectionAutoCalibrator.SampleCount);

        Assert.All(points, p =>
        {
            Assert.InRange(p.X, 0, Client.Width - 1);
            Assert.InRange(p.Y, 0, Client.Height - 1);
        });

        Assert.Equal(points.Count, points.Distinct().Count());
    }

    /// <summary>
    /// Offsets all pointing the same way determine nothing, whatever ring they
    /// came from.
    /// </summary>
    [Fact]
    public void The_probe_points_point_in_different_directions()
    {
        IReadOnlyList<(int X, int Y)> points =
            ScreenProjectionAutoCalibrator.ProbePoints(Client, ScreenProjectionAutoCalibrator.SampleCount);

        double centreX = Client.Width / 2.0;
        double centreY = Client.Height / 2.0;

        List<double> angles = points
            .Select(p => Math.Atan2(p.Y - centreY, p.X - centreX))
            .OrderBy(a => a)
            .ToList();

        for (var i = 1; i < angles.Count; i++)
            Assert.True(angles[i] - angles[i - 1] > 0.15, "two probe points lie in nearly the same direction");
    }

    // ----------------------------------------------------------------- helper

    /// <summary>
    /// Pairs as the client would report them: the pixel is where the click really
    /// landed, and the offset beside it is the whole tile the client resolved that
    /// pixel to, so each pair carries up to half a tile of slip.
    /// </summary>
    /// <remarks>
    /// The offsets are spread about ten tiles out, which is what the probe ring
    /// produces on a real client. Nearer than that and half a tile of slip is most
    /// of the reading, and the fit stops determining a scale - which is a real
    /// property of the method, not an inconvenience of the fixture.
    /// </remarks>
    private static List<ScreenProjectionSample> Measured(params (int X, int Y)[] offsets)
    {
        const double a = 31.6, b = -1.4, c = 518.0;
        const double d = -0.1, e = 14.8, f = 433.0;

        var slips = new (double X, double Y)[]
        {
            (0.3, -0.4), (-0.4, 0.3), (0.2, 0.45), (-0.35, -0.2), (0.45, 0.1), (-0.2, -0.45), (0.1, 0.35),
        };

        var samples = new List<ScreenProjectionSample>(offsets.Length);
        for (var i = 0; i < offsets.Length; i++)
        {
            double x = offsets[i].X + slips[i % slips.Length].X;
            double y = offsets[i].Y + slips[i % slips.Length].Y;
            samples.Add(new ScreenProjectionSample(
                new MapPoint(offsets[i].X, offsets[i].Y),
                (int)Math.Round((a * x) + (b * y) + c),
                (int)Math.Round((d * x) + (e * y) + f)));
        }

        return samples;
    }
}
