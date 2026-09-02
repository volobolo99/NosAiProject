using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Orchestration;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Safety;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// X-P2: the commit point is wired, the five conditions are printable, and the
/// three real-client refusals have the same names on a stated desktop as they
/// will on a live one.
/// </summary>
public sealed class InputGuardsProbeTests
{
    private static readonly IntPtr Session = 0x1000;
    private static readonly IntPtr Other = 0x2000;
    private static readonly IntPtr Monitor = 0xABCD;

    private static GeometryEpoch Epoch(int x = 0, int y = 0, uint dpi = 96) =>
        new(Session, new PixelRect(x, y, 1024, 768), dpi, Monitor);

    private static CommitRequest Request(GeometryEpoch? stamp = null) =>
        new(
            new GeometryStamp(stamp ?? Epoch(), DateTimeOffset.UnixEpoch),
            ScreenX: 512,
            ScreenY: 384,
            Scale: new GeometryShape(1024, 768, 96));

    private sealed class FakeDesktop : ICommitEnvironment
    {
        public IntPtr Foreground { get; set; } = Session;
        public IntPtr RootAtPoint { get; set; } = Session;
        public bool? Cloaked { get; set; } = false;
        public GeometryEpoch Live { get; set; } = Epoch();

        public IntPtr ForegroundWindow() => Foreground;
        public IntPtr RootWindowFromPoint(int x, int y) => RootAtPoint;
        public bool? IsCloaked(IntPtr window) => Cloaked;
        public GeometryEpoch ReadEpoch(IntPtr window) => Live;
    }

    private sealed class FakeHuman : IHumanInputMonitor
    {
        public bool IsWatching { get; set; } = true;
        public TimeSpan? SinceLastHumanInput { get; set; } = TimeSpan.FromMinutes(1);
        public long HumanEventCount => 0;
        public long InjectedEventCount => 0;
    }

    [Fact]
    public void TheRuntimeWiresTheInputGuardsFlag()
    {
        string root = RepositoryRoot();
        string program = File.ReadAllText(Path.Combine(root, "src", "NosAi.Runtime", "Program.cs"));
        Assert.Contains("--input-guards", program, StringComparison.Ordinal);
        Assert.Contains("InputGuardsProbe.Run", program, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCommitPathUsesLibraryImportNotDllImport()
    {
        string root = RepositoryRoot();
        string environment = File.ReadAllText(Path.Combine(
            root, "src", "NosAi.Runtime", "LowLevel", "CommitPointValidator.cs"));
        string monitor = File.ReadAllText(Path.Combine(
            root, "src", "NosAi.Runtime", "LowLevel", "HumanInputMonitor.cs"));

        Assert.Contains("[LibraryImport(\"user32.dll\")]", environment, StringComparison.Ordinal);
        Assert.Contains("[LibraryImport(\"dwmapi.dll\")]", environment, StringComparison.Ordinal);
        Assert.DoesNotContain("[DllImport", environment, StringComparison.Ordinal);

        Assert.Contains("[LibraryImport(\"user32.dll\"", monitor, StringComparison.Ordinal);
        Assert.Contains("[LibraryImport(\"kernel32.dll\")]", monitor, StringComparison.Ordinal);
        Assert.DoesNotContain("[DllImport", monitor, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAutonomousCompositionRequiresTheCommitPoint()
    {
        var components = RuntimeComposition.CreateSafe();

        Assert.IsType<GatedInputBackend>(components.InputBackend);
        Assert.True(((GatedInputBackend)components.InputBackend).RequiresCommitPoint);
        Assert.IsType<HumanInputMonitor>(components.HumanInput);
    }

    [Fact]
    public void TheCertificationCompositionStaysPolicyOnly()
    {
        var components = RuntimeComposition.Create(
            RuntimeSafetyPolicy.SafeDefault,
            new RecordingInputBackend());

        Assert.IsType<GatedInputBackend>(components.InputBackend);
        Assert.False(((GatedInputBackend)components.InputBackend).RequiresCommitPoint);
        Assert.Same(NotWatchingHumanInput.Instance, components.HumanInput);
    }

    [Fact]
    public void AClearDesktopReportsEveryConditionAndAuthorises()
    {
        InputGuardReading reading = InputGuardsProbe.Observe(
            new FakeDesktop(), new FakeHuman(), Request());
        string report = InputGuardsProbe.Format(reading);

        Assert.True(reading.Authorised, reading.RefusalReason);
        Assert.Null(reading.RefusalReason);
        Assert.Contains("geometry:", report, StringComparison.Ordinal);
        Assert.Contains("foreground:", report, StringComparison.Ordinal);
        Assert.Contains("point:", report, StringComparison.Ordinal);
        Assert.Contains("cloak:", report, StringComparison.Ordinal);
        Assert.Contains("human:", report, StringComparison.Ordinal);
        Assert.Contains("scale:", report, StringComparison.Ordinal);
        Assert.Contains("verdict:    authorised", report, StringComparison.Ordinal);
    }

    /// <summary>Real-client proof 1, on a stated desktop: the window moved mid-act.</summary>
    [Fact]
    public void AWindowMovedSinceTheStampIsANamedGeometryRefusal()
    {
        var desktop = new FakeDesktop { Live = Epoch(x: 300, y: 80) };

        InputGuardReading reading = InputGuardsProbe.Observe(desktop, new FakeHuman(), Request());

        Assert.False(reading.Authorised);
        Assert.StartsWith(CommitPointValidator.GeometryChangedPrefix, reading.RefusalReason, StringComparison.Ordinal);
        Assert.Contains(GeometryEpoch.MovedReason, reading.RefusalReason, StringComparison.Ordinal);
        Assert.Contains("changed:", reading.Geometry, StringComparison.Ordinal);
        Assert.Contains("verdict:    refused", InputGuardsProbe.Format(reading), StringComparison.Ordinal);
    }

    /// <summary>Real-client proof 2: a third window owns the exact click pixel.</summary>
    [Fact]
    public void AThirdWindowOnTheClickPointIsANamedPointRefusal()
    {
        var desktop = new FakeDesktop { RootAtPoint = Other };

        InputGuardReading reading = InputGuardsProbe.Observe(desktop, new FakeHuman(), Request());

        Assert.False(reading.Authorised);
        Assert.Equal(CommitPointValidator.PointNotOursReason, reading.RefusalReason);
        Assert.Contains("other", reading.Point, StringComparison.Ordinal);
    }

    /// <summary>Real-client proof 3: a hand on the mouse during the programme.</summary>
    [Fact]
    public void AHandOnTheMouseIsANamedHumanRefusal()
    {
        var human = new FakeHuman { SinceLastHumanInput = TimeSpan.FromMilliseconds(10) };

        InputGuardReading reading = InputGuardsProbe.Observe(new FakeDesktop(), human, Request());

        Assert.False(reading.Authorised);
        Assert.StartsWith(CommitPointValidator.HumanActiveReason, reading.RefusalReason, StringComparison.Ordinal);
        Assert.Contains("recent", reading.Human, StringComparison.Ordinal);
    }

    [Fact]
    public void ProbeRefusesOffWindows()
    {
        if (OperatingSystem.IsWindows())
            return;

        Assert.Equal(2, InputGuardsProbe.Run());
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;
        Assert.True(directory is not null, "Repository root not found: no NosAi.sln above the test assembly.");
        return directory!.FullName;
    }
}
