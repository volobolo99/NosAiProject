using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception;
using Xunit;

// Deliberately the flat NosAi.Runtime.Tests namespace, matching
// ScreenPerceptionTests.cs's own convention -- a nested
// "NosAi.Runtime.Tests.Perception" would shadow the production
// NosAi.Runtime.Perception namespace for every other test file in this
// project that refers to it unqualified from NosAi.Runtime.Tests (e.g.
// Gate3DecisionLoopTests.cs's "Perception.Network.GameEndpoint"), since C#
// namespace lookup prefers the closer enclosing declaration.
namespace NosAi.Runtime.Tests;

/// <summary>
/// AP-02/A4: <see cref="ScreenVitalsCapture"/> is the real wiring sequence
/// (process id -&gt; <see cref="ClientWindowLocator"/> -&gt;
/// <see cref="DxgiDesktopDuplicationSource"/> -&gt; <see cref="PerceptionPipeline"/>
/// + <see cref="ScreenVitalReader"/>) behind a <see cref="VisualObservation"/>.
/// This sandbox has no Windows desktop, so only the fail-closed side of that
/// sequence is exercised here: no process id, no platform support for the
/// window/DXGI calls, and the DXGI-creation failure itself. Every assertion
/// is against an honest <see cref="VisualObservation.Unobserved(string, DateTime?)"/>
/// with a specific, diagnosable reason -- never an exception escaping
/// <see cref="ScreenVitalsCapture.Capture"/>.
/// </summary>
public sealed class ScreenVitalsCaptureTests
{
    private static readonly DateTime T0 = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Constructor_RejectsNullProcessIdDelegate()
    {
        Assert.Throws<ArgumentNullException>(() => new ScreenVitalsCapture(null!));
    }

    [Fact]
    public void Capture_WithNoProcessId_ReturnsUnobserved_WithASpecificReason()
    {
        using var capture = new ScreenVitalsCapture(() => null, clock: () => T0);

        VisualObservation observation = capture.Capture();

        Assert.False(observation.Frame.FrameAcquired);
        Assert.Equal(ScreenVitalsCapture.NoProcessIdReason, observation.Frame.UnavailableReason);
        Assert.False(observation.Vitals.Hp.Current.HasValue);
        Assert.Equal(ScreenVitalsCapture.NoProcessIdReason, observation.Vitals.Hp.FailureReason);
        Assert.False(observation.HasTarget.HasValue);
        Assert.Equal(ScreenVitalsCapture.NoProcessIdReason, observation.HasTarget.FailureReason);
        Assert.Equal(T0, observation.ObservedAtUtc);
    }

    [Fact]
    public void Capture_WithAProcessId_OnANonWindowsHost_FailsClosed_BeforeTouchingClientWindowLocator()
    {
        // This test only means what it says on a non-Windows CI/sandbox host,
        // where OperatingSystem.IsWindows() is false -- exactly this sandbox.
        // On a real Windows target the same call would proceed to
        // ClientWindowLocator.TryFind instead.
        Assert.False(OperatingSystem.IsWindows(), "This test asserts the non-Windows fail-closed path.");

        using var capture = new ScreenVitalsCapture(() => 4242, clock: () => T0);

        VisualObservation observation = capture.Capture();

        Assert.False(observation.Frame.FrameAcquired);
        Assert.Equal(ScreenVitalsCapture.RequiresWindowsReason, observation.Frame.UnavailableReason);
        Assert.Equal(T0, observation.ObservedAtUtc);
    }

    [Fact]
    public void Capture_IsRepeatable_AndKeepsReportingTheSameHonestReason()
    {
        int calls = 0;
        using var capture = new ScreenVitalsCapture(() => { calls++; return 4242; }, clock: () => T0);

        VisualObservation first = capture.Capture();
        VisualObservation second = capture.Capture();

        Assert.Equal(2, calls);
        Assert.Equal(first.Frame.UnavailableReason, second.Frame.UnavailableReason);
        Assert.False(first.Frame.FrameAcquired);
        Assert.False(second.Frame.FrameAcquired);
    }

    [Fact]
    public void Capture_UsesTheInjectedClock_ForTheObservedInstant()
    {
        DateTime injected = T0.AddMinutes(5);
        using var capture = new ScreenVitalsCapture(() => null, clock: () => injected);

        VisualObservation observation = capture.Capture();

        Assert.Equal(injected, observation.ObservedAtUtc);
    }

    [Fact]
    public void TryEnsurePipeline_OnANonWindowsHost_FailsClosed_WithDxgisOwnReason()
    {
        // Exercises the DXGI-creation step directly (internal, see the type's
        // own remarks on TryEnsurePipeline) since Capture() itself never
        // reaches it on this host -- it already fails one step earlier, at
        // the Windows-only client window lookup. DxgiDesktopDuplicationSource
        // .TryCreate reports "dxgi_requires_windows" cleanly rather than
        // throwing, so this branch is genuinely testable without a real
        // Windows target.
        Assert.False(OperatingSystem.IsWindows());

        using var capture = new ScreenVitalsCapture(() => 4242, clock: () => T0);

        bool ok = capture.TryEnsurePipeline(out string? failureReason);

        Assert.False(ok);
        Assert.Equal("dxgi_requires_windows", failureReason);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var capture = new ScreenVitalsCapture(() => null, clock: () => T0);

        capture.Dispose();
        var exception = Record.Exception(() => capture.Dispose());

        Assert.Null(exception);
    }

    [Fact]
    public void Capture_AfterDispose_ThrowsObjectDisposedException()
    {
        var capture = new ScreenVitalsCapture(() => null, clock: () => T0);
        capture.Dispose();

        Assert.Throws<ObjectDisposedException>(() => capture.Capture());
    }

    [Fact]
    public void Capture_NeverThrowsForOrdinaryUnavailability_RegardlessOfProcessIdPresence()
    {
        using var withPid = new ScreenVitalsCapture(() => 999, clock: () => T0);
        using var withoutPid = new ScreenVitalsCapture(() => null, clock: () => T0);

        var exWith = Record.Exception(() => withPid.Capture());
        var exWithout = Record.Exception(() => withoutPid.Capture());

        Assert.Null(exWith);
        Assert.Null(exWithout);
    }
}
