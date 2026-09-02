using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The second row of the table in <c>docs/CONTROLLO_PERSONAGGIO_ATTUAZIONE.md</c>
/// § 6.2: what the commit point compares, and what each of the four changes it has to
/// notice looks like.
/// </summary>
/// <remarks>
/// <para>
/// § 2.1 makes "geometry epoch unchanged since authorisation" the first condition of
/// the commit, and § 6.3 recorded that there was no such value. These are the cases
/// that fix its shape.
/// </para>
/// <para>
/// The comparison is tested on constructed epochs rather than on real windows: the
/// reading is one Win32 call per component and cannot be made to move on demand,
/// while the comparison is the part that decides whether an act is emitted.
/// </para>
/// </remarks>
public sealed class GeometryEpochTests
{
    private static readonly IntPtr Window = 0x1234;
    private static readonly IntPtr Monitor = 0xABCD;

    private static GeometryEpoch Epoch(
        int x = 100, int y = 200, int width = 1024, int height = 768,
        uint dpi = 96, IntPtr? monitor = null, IntPtr? window = null) =>
        new(window ?? Window, new PixelRect(x, y, width, height), dpi, monitor ?? Monitor);

    // ------------------------------------------------- what must not change it

    [Fact]
    public void AnUnchangedGeometryIsUnchanged()
    {
        Assert.True(GeometryEpoch.Unchanged(Epoch(), Epoch(), out string? reason), reason);
        Assert.Null(reason);
    }

    // ------------------------------------------------------ the four changes

    /// <summary>
    /// A move alone. The coordinate in a pending action was computed from the old
    /// origin, so it now points somewhere nobody chose — even though the calibration
    /// behind it is still perfectly good.
    /// </summary>
    [Fact]
    public void AWindowThatMovedIsAChange()
    {
        Assert.False(GeometryEpoch.Unchanged(Epoch(), Epoch(x: 140, y: 260), out string? reason));
        Assert.StartsWith(GeometryEpoch.MovedReason, reason, StringComparison.Ordinal);
        Assert.Contains("100,200_to_140,260", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AWindowThatResizedIsAChange()
    {
        Assert.False(GeometryEpoch.Unchanged(Epoch(), Epoch(width: 1280, height: 960), out string? reason));
        Assert.StartsWith(GeometryEpoch.ResizedReason, reason, StringComparison.Ordinal);
        Assert.Contains("1024x768_to_1280x960", reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The case the client-size comparison structurally cannot see, and the reason
    /// the epoch carries a DPI at all: the rectangle is identical and the window is
    /// being drawn at a different scale.
    /// </summary>
    [Fact]
    public void ADpiChangeAtTheSameSizeIsAChange()
    {
        Assert.False(GeometryEpoch.Unchanged(Epoch(dpi: 96), Epoch(dpi: 120), out string? reason));
        Assert.StartsWith(GeometryEpoch.DpiChangedReason, reason, StringComparison.Ordinal);
        Assert.Contains("96_to_120", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AMonitorChangeIsAChange()
    {
        Assert.False(GeometryEpoch.Unchanged(Epoch(), Epoch(monitor: 0xBEEF), out string? reason));
        Assert.Equal(GeometryEpoch.MonitorChangedReason, reason);
    }

    /// <summary>
    /// A different window is not a moved window: the client was restarted under the
    /// runtime and everything measured against the old one is stale.
    /// </summary>
    [Fact]
    public void ADifferentWindowIsReportedAsSuchAndNotAsAMove()
    {
        Assert.False(GeometryEpoch.Unchanged(Epoch(), Epoch(window: 0x9999, x: 140), out string? reason));
        Assert.Equal(GeometryEpoch.WindowChangedReason, reason);
    }

    /// <summary>
    /// The four have different remedies, so when several change at once the reason
    /// names the most structural rather than the first one a loop happened to reach.
    /// </summary>
    [Fact]
    public void TheMostStructuralChangeIsTheOneReported()
    {
        // Moved and resized: a resize is the one that invalidates the transform.
        Assert.False(GeometryEpoch.Unchanged(
            Epoch(), Epoch(x: 500, width: 1280), out string? resized));
        Assert.StartsWith(GeometryEpoch.ResizedReason, resized, StringComparison.Ordinal);

        // Moved and rescaled at the same size: the scale is the one nothing else sees.
        Assert.False(GeometryEpoch.Unchanged(
            Epoch(), Epoch(x: 500, dpi: 144), out string? rescaled));
        Assert.StartsWith(GeometryEpoch.DpiChangedReason, rescaled, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two-monitor case at the same scale: position and monitor both change,
    /// DPI does not. The reason names the monitor, because a move is the weaker
    /// fact and would send someone to recompute a coordinate that is already
    /// invalid for a more structural reason.
    /// </summary>
    [Fact]
    public void DraggingOntoAnotherMonitorAtTheSameScaleNamesTheMonitorNotTheMove()
    {
        Assert.False(GeometryEpoch.Unchanged(
            Epoch(), Epoch(x: 2000, y: 100, monitor: 0xBEEF), out string? reason));
        Assert.Equal(GeometryEpoch.MonitorChangedReason, reason);
    }

    /// <summary>
    /// The two-monitor case at different scales (100% → 150%): DPI, monitor and
    /// position all change, rectangle size does not. DPI is named first because a
    /// scale change in Settings moves the DPI without moving the window, and
    /// naming the monitor there would be wrong.
    /// </summary>
    [Fact]
    public void DraggingOntoAMonitorAtADifferentScaleNamesTheDpi()
    {
        Assert.False(GeometryEpoch.Unchanged(
            Epoch(dpi: 96),
            Epoch(x: 2000, y: 100, dpi: 144, monitor: 0xBEEF),
            out string? reason));
        Assert.StartsWith(GeometryEpoch.DpiChangedReason, reason, StringComparison.Ordinal);
        Assert.Contains("96_to_144", reason, StringComparison.Ordinal);
    }

    // ----------------------------------------------------------- the unknown

    /// <summary>
    /// Two geometries that could not be read are not two geometries that agree. This
    /// is the one comparison where passing by knowing nothing emits a real click.
    /// </summary>
    [Fact]
    public void UnknownMatchesNothingIncludingAnotherUnknown()
    {
        Assert.False(GeometryEpoch.Unchanged(GeometryEpoch.Unknown, Epoch(), out string? stamped));
        Assert.Equal(GeometryEpoch.UnknownReason, stamped);

        Assert.False(GeometryEpoch.Unchanged(Epoch(), GeometryEpoch.Unknown, out string? current));
        Assert.Equal(GeometryEpoch.UnknownReason, current);

        Assert.False(GeometryEpoch.Unchanged(
            GeometryEpoch.Unknown, GeometryEpoch.Unknown, out string? both));
        Assert.Equal(GeometryEpoch.UnknownReason, both);
    }

    [Fact]
    public void APartiallyReadableEpochIsNotKnown()
    {
        Assert.False(GeometryEpoch.Unknown.IsKnown);
        Assert.False(Epoch(dpi: 0).IsKnown);
        Assert.False(Epoch(width: 0).IsKnown);
        Assert.False(Epoch(window: IntPtr.Zero).IsKnown);
        Assert.True(Epoch().IsKnown);
    }

    /// <summary>
    /// Reading a window that does not exist gives Unknown rather than a partial
    /// epoch: a partial one would compare equal on whatever was readable, which is
    /// agreement about the wrong thing.
    /// </summary>
    [Fact]
    public void ReadingNothingGivesUnknown()
    {
        Assert.False(GeometryEpoch.Read(IntPtr.Zero).IsKnown);
        Assert.False(GeometryEpoch.Read((ClientWindow?)null).IsKnown);

        if (OperatingSystem.IsWindows())
            Assert.False(GeometryEpoch.Read(0x7FFF_FFFF).IsKnown);
    }

    // ------------------------------------------------------------- the shape

    /// <summary>
    /// The storable projection of an epoch. A window handle and a monitor handle mean
    /// nothing outside the session that read them, so a calibration that stored a whole
    /// epoch would be refused on every restart — a check that always fires checks
    /// nothing.
    /// </summary>
    [Fact]
    public void TheShapeIsTheSizeAndTheScaleAndNotThePosition()
    {
        GeometryShape moved = Epoch(x: 900, y: 900).Shape;

        Assert.Equal(Epoch().Shape, moved);
        Assert.Equal(new GeometryShape(1024, 768, 96), Epoch().Shape);

        Assert.NotEqual(Epoch().Shape, Epoch(dpi: 120).Shape);
        Assert.NotEqual(Epoch().Shape, Epoch(width: 800).Shape);
    }

    [Fact]
    public void AShapeMissingAnyComponentIsNotKnown()
    {
        Assert.True(new GeometryShape(1024, 768, 96).IsKnown);
        Assert.False(new GeometryShape(1024, 768, 0).IsKnown);
        Assert.False(new GeometryShape(0, 768, 96).IsKnown);
        Assert.False(default(GeometryShape).IsKnown);
    }

    // ------------------------------------------------------------- the stamp

    /// <summary>
    /// What the envelope carries. Taken once at authorisation and never refreshed:
    /// a stamp that re-read itself would agree with itself at every moment, which is
    /// the one behaviour that turns the commit check into decoration.
    /// </summary>
    [Fact]
    public void AStampThatWasNeverReadRefusesRatherThanPasses()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));
        var stamp = new GeometryStamp(GeometryEpoch.Unknown, clock.GetUtcNow());

        Assert.False(stamp.IsKnown);
        Assert.False(stamp.StillCurrent(clock, TimeSpan.FromMilliseconds(50), out string? reason, out _));
        Assert.Equal(GeometryEpoch.UnknownReason, reason);
    }

    /// <summary>
    /// The age is a measurement and is returned whether the check passes or fails.
    /// § 2.1: there is no zero-risk window, there has to be a measured one.
    /// </summary>
    [Fact]
    public void TheAgeIsReportedEvenWhenTheGeometryCheckIsWhatFailed()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));
        var stamp = new GeometryStamp(Epoch(), clock.GetUtcNow());

        clock.Advance(TimeSpan.FromMilliseconds(17));

        // The window handle is invented, so the re-read gives Unknown and the geometry
        // check is what refuses; the elapsed time still comes back.
        Assert.False(stamp.StillCurrent(clock, TimeSpan.FromSeconds(1), out _, out TimeSpan age));
        Assert.Equal(TimeSpan.FromMilliseconds(17), age);
    }

    [Fact]
    public void TakingAStampRecordsTheInstantItWasRead()
    {
        var at = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(at);

        GeometryStamp stamp = GeometryStamp.Take(IntPtr.Zero, clock);

        Assert.Equal(at, stamp.TakenAtUtc);
        Assert.False(stamp.IsKnown);
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
