using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The DPI awareness regime a calibration was estimated under, and the refusal that
/// keeps it from being read in the wrong unit.
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/CONTROLLO_PERSONAGGIO_ATTUAZIONE.md</c> § 6.2. The regime turned out to
/// be a function of the command used to launch: measured on 1 Sep 2026,
/// <c>NosAi.Runtime.exe</c> reports PerMonitorV2 and <c>dotnet NosAi.Runtime.dll</c>
/// reports PerMonitor, because the manifest is embedded in the apphost and the
/// <c>dotnet</c> host carries its own. Nothing recorded which one a calibration came
/// from.
/// </para>
/// <para>
/// The operator's display is at 125%, not 100%, so aware and unaware genuinely
/// disagree about every rectangle here: the same window measures 1536x912 to an
/// unaware reader and 1920x1140 to an aware one.
/// </para>
/// </remarks>
public sealed class ScreenProjectionRegimeTests
{
    private const int ClientWidth = 1024;
    private const int ClientHeight = 768;
    private static readonly DateTime At = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly MapPoint Standing = new(100, 100);

    private static ScreenProjectionCalibration CalibratedUnder(DpiAwarenessRegime regime)
    {
        const double a = 16.0, b = -16.0, c = 512.0, d = 8.0, e = 8.0, f = 380.0;

        List<ScreenProjectionSample> samples =
            new[] { (6, 2), (-4, 5), (1, -7), (8, 8) }
                .Select(p => new ScreenProjectionSample(
                    new MapPoint(p.Item1, p.Item2),
                    (int)Math.Round((a * p.Item1) + (b * p.Item2) + c),
                    (int)Math.Round((d * p.Item1) + (e * p.Item2) + f)))
                .ToList();

        Assert.True(ScreenProjectionCalibration.TrySolve(
            samples, ClientWidth, ClientHeight, At,
            out ScreenProjectionCalibration calibration, out string? reason,
            regime: regime), reason);

        return calibration;
    }

    private static CalibratedScreenProjection ProjectionUnder(
        DpiAwarenessRegime calibratedUnder,
        DpiAwarenessRegime runningUnder,
        int clientWidth = ClientWidth,
        int clientHeight = ClientHeight) =>
        new(
            CalibratedUnder(calibratedUnder),
            () => new PixelRect(0, 0, clientWidth, clientHeight),
            () => ClassifiedValue<MapPoint>.Live(Standing, At),
            () => runningUnder);

    [Fact]
    public void The_same_regime_projects_normally()
    {
        CalibratedScreenProjection projection = ProjectionUnder(
            DpiAwarenessRegime.PerMonitorV2, DpiAwarenessRegime.PerMonitorV2);

        Assert.True(projection.TryProject(105, 100, out _, out _, out string? reason), reason);
    }

    /// <summary>
    /// The case the whole change is for: same client size, different unit. The size
    /// comparison sees two identical rectangles and has nothing to object to.
    /// </summary>
    [Fact]
    public void A_calibration_from_another_regime_is_refused_at_the_same_client_size()
    {
        CalibratedScreenProjection projection = ProjectionUnder(
            DpiAwarenessRegime.Unaware, DpiAwarenessRegime.PerMonitorV2);

        Assert.False(projection.TryProject(105, 100, out _, out _, out string? reason));
        Assert.StartsWith(CalibratedScreenProjection.RegimeChangedReason, reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The trap found on the operator's machine: calibrate from the apphost, run
    /// under <c>dotnet exec</c>, and every pixel in the file was measured by a
    /// different process regime from the one about to click.
    /// </summary>
    [Fact]
    public void A_calibration_from_the_apphost_is_refused_under_dotnet_exec()
    {
        CalibratedScreenProjection projection = ProjectionUnder(
            DpiAwarenessRegime.PerMonitorV2, DpiAwarenessRegime.PerMonitor);

        Assert.False(projection.TryProject(105, 100, out _, out _, out string? reason));
        Assert.Contains("permonitorv2_to_permonitor", reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The refusal names the regime change, not a resize. The two faults have
    /// different remedies and "the client size changed" does not lead anyone to
    /// "run it from the command you calibrated with".
    /// </summary>
    [Fact]
    public void The_refusal_names_the_regime_and_not_a_resize()
    {
        CalibratedScreenProjection projection = ProjectionUnder(
            DpiAwarenessRegime.Unaware, DpiAwarenessRegime.PerMonitorV2);

        Assert.False(projection.TryProject(105, 100, out _, out _, out string? reason));
        Assert.NotEqual(CalibratedScreenProjection.ClientResizedReason, reason);
        Assert.Contains("unaware_to_permonitorv2", reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// A regime that could not be read is not the regime that was recorded, so it
    /// refuses on both sides. Unknown authorises nothing (DOMAIN-10).
    /// </summary>
    [Fact]
    public void An_unknown_regime_on_either_side_is_a_refusal_never_a_pass()
    {
        CalibratedScreenProjection running = ProjectionUnder(
            DpiAwarenessRegime.PerMonitorV2, DpiAwarenessRegime.Unknown);
        Assert.False(running.TryProject(105, 100, out _, out _, out string? runningReason));
        Assert.StartsWith(CalibratedScreenProjection.RegimeChangedReason, runningReason, StringComparison.Ordinal);

        CalibratedScreenProjection stored = ProjectionUnder(
            DpiAwarenessRegime.Unknown, DpiAwarenessRegime.PerMonitorV2);
        Assert.False(stored.TryProject(105, 100, out _, out _, out string? storedReason));
        Assert.StartsWith(CalibratedScreenProjection.RegimeChangedReason, storedReason, StringComparison.Ordinal);

        // Even two Unknowns, which compare equal, must not pass: a reading that
        // failed twice is not agreement.
        CalibratedScreenProjection both = ProjectionUnder(
            DpiAwarenessRegime.Unknown, DpiAwarenessRegime.Unknown);
        Assert.False(both.TryProject(105, 100, out _, out _, out _));
    }

    /// <summary>
    /// The division of labour, stated as a test. The regime is checked first, so a
    /// calibration that is wrong in both ways reports the cause that came first and
    /// is not fixed by resizing.
    /// </summary>
    [Fact]
    public void The_regime_is_judged_before_the_client_size()
    {
        CalibratedScreenProjection projection = ProjectionUnder(
            DpiAwarenessRegime.Unaware,
            DpiAwarenessRegime.PerMonitorV2,
            clientWidth: 1280,
            clientHeight: 960);

        Assert.False(projection.TryProject(105, 100, out _, out _, out string? reason));
        Assert.StartsWith(CalibratedScreenProjection.RegimeChangedReason, reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the size comparison keeps its own job. Same regime, different rectangle:
    /// a different zoom and a different layout, so the measured transform no longer
    /// describes what is on screen.
    /// </summary>
    [Fact]
    public void The_client_size_comparison_still_catches_a_resize_within_one_regime()
    {
        CalibratedScreenProjection projection = ProjectionUnder(
            DpiAwarenessRegime.PerMonitorV2,
            DpiAwarenessRegime.PerMonitorV2,
            clientWidth: 1280,
            clientHeight: 960);

        Assert.False(projection.TryProject(105, 100, out _, out _, out string? reason));
        Assert.Equal(CalibratedScreenProjection.ClientResizedReason, reason);
    }
}
