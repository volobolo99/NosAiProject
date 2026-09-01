using NosAi.Runtime.LowLevel;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The last line before the desktop: the coordinate that does not exist, and the
/// monitor that tells the commit point whether a person is at the keyboard.
/// </summary>
public sealed class InputBoundaryTests
{
    /// <summary>
    /// A point off the virtual desktop is refused, not carried to the nearest edge.
    /// </summary>
    /// <remarks>
    /// This was the one place on the whole path where a coordinate error became an act
    /// instead of a refusal: <c>Math.Clamp</c> turned "that pixel does not exist" into
    /// a real click on the border of a screen. The guards upstream mean it does not
    /// bite today, which is exactly why it had to be fixed — a last line of defence
    /// that silently corrects removes the evidence that the earlier guards were wrong.
    /// </remarks>
    [Fact]
    public void APointOffTheVirtualDesktopIsRefusedAndNotClampedToTheEdge()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var backend = new Win32InputBackend();

        // Far outside any desktop. Nothing is sent, so the operator's cursor does not
        // move: the refusal happens before SendInput.
        Assert.False(backend.MoveAbsolute(int.MinValue / 2, int.MinValue / 2));
        Assert.StartsWith(
            Win32InputBackend.PointOffVirtualDesktopReason,
            backend.LastFailureReason,
            StringComparison.Ordinal);

        Assert.False(backend.MoveAbsolute(int.MaxValue / 2, 0));
        Assert.StartsWith(
            Win32InputBackend.PointOffVirtualDesktopReason,
            backend.LastFailureReason,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The refusal names the point and the desktop it was measured against, because
    /// "false" alone leaves nobody able to tell a bad coordinate from a dead API.
    /// </summary>
    [Fact]
    public void TheRefusalCarriesThePointAndTheDesktopItWasJudgedAgainst()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var backend = new Win32InputBackend();
        backend.MoveAbsolute(-999_999, -999_999);

        Assert.Contains("-999999,-999999", backend.LastFailureReason, StringComparison.Ordinal);
        Assert.Contains("outside", backend.LastFailureReason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The release capability can only release. That is what lets the abort path skip
    /// the policy gate without the exception being a hole.
    /// </summary>
    [Fact]
    public void TheReleaseBackendExposesNothingThatPresses()
    {
        System.Reflection.MethodInfo[] methods = typeof(IInputReleaseBackend).GetMethods();

        Assert.Equal(2, methods.Length);
        Assert.All(methods, m =>
            Assert.StartsWith("Release", m.Name, StringComparison.Ordinal));
    }

    // ------------------------------------------------------------ the monitor

    /// <summary>
    /// A monitor that is not running reports null, never a long idle time. The commit
    /// point reads null as a refusal, so the difference is the difference between
    /// standing down and acting on evidence nobody gathered.
    /// </summary>
    [Fact]
    public void AMonitorThatIsNotWatchingReportsUnknownAndNotIdle()
    {
        IHumanInputMonitor monitor = NotWatchingHumanInput.Instance;

        Assert.False(monitor.IsWatching);
        Assert.Null(monitor.SinceLastHumanInput);
    }

    /// <summary>
    /// The real hooks install and come down cleanly. Before the first human event the
    /// monitor reports null rather than a long idle: it has been watching for a moment
    /// and seen nobody, which is not the same as nobody being there.
    /// </summary>
    [Fact]
    public void TheHooksInstallAndReportUnknownBeforeTheFirstHumanEvent()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var monitor = new HumanInputMonitor();

        Assert.True(monitor.TryStart(out string? failure), failure);
        Assert.True(monitor.IsWatching);

        // Starting twice is idempotent rather than a second set of hooks.
        Assert.True(monitor.TryStart(out _));

        if (monitor.HumanEventCount == 0)
            Assert.Null(monitor.SinceLastHumanInput);
    }

    [Fact]
    public void DisposingTheMonitorStopsItWatching()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var monitor = new HumanInputMonitor();
        Assert.True(monitor.TryStart(out string? failure), failure);

        monitor.Dispose();

        Assert.False(monitor.IsWatching);

        // And disposing twice is not an error.
        monitor.Dispose();
    }
}
