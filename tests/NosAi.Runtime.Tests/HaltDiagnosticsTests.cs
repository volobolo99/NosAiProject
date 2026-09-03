using System.Text.Json;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Observability;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Safety;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// A transition into halt writes one dump with the declared fields, and only then.
/// </summary>
public sealed class HaltDiagnosticsTests : IDisposable
{
    private readonly string _directory;

    public HaltDiagnosticsTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "nosai-halt-dump-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    private static RecoveryController Controller()
        => new(new TrustBoundary(TrustTier.Tier4_FullAutonomous), maxRetries: 2);

    [Fact]
    public void AHaltTransitionWritesExactlyOneDumpWithTheDeclaredFields()
    {
        var commit = new CommitPointRefusalDump("commit_geometry_changed", new DateTime(2026, 9, 2, 10, 0, 0, DateTimeKind.Utc));
        var authority = new SessionAuthorityDump(false, "authority_window_not_foreground", false, "medium", "medium", true);
        var stages = new PipelineStageBoard();
        stages.Record("Observe", true);
        stages.Record("Execute", false, "verification_failed");

        var dumper = new HaltDiagnosticsDumper(_directory, new HaltDiagnosticsContext
        {
            LastCommitPointRefusal = () => commit,
            LastSessionAuthority = () => authority,
            LastStageOutcomes = stages.Snapshot
        });

        RecoveryController recovery = Controller();
        dumper.Attach(recovery);

        var mode = RuntimeMode.Normal;
        for (var i = 0; i < 4; i++)
            recovery.HandleFailure(ref mode);

        Assert.Equal(RecoveryState.Halted, recovery.State);
        Assert.Equal(1, dumper.Written);

        string[] files = Directory.GetFiles(_directory, "halt-*.json");
        string path = Assert.Single(files);
        Assert.StartsWith("halt-", Path.GetFileName(path), StringComparison.Ordinal);
        Assert.EndsWith(".json", path, StringComparison.Ordinal);

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.Equal("Throttled", root.GetProperty("previousState").GetString());
        Assert.Equal("Halted", root.GetProperty("newState").GetString());
        Assert.True(root.GetProperty("transitionedAtUtc").GetDateTimeOffset() != default);
        Assert.Equal(4, root.GetProperty("failuresInWindow").GetInt32());
        Assert.Equal(4, root.GetProperty("failureWindow").GetArrayLength());
        Assert.Equal(4, root.GetProperty("windowOccupancy").GetInt32());
        Assert.Equal("commit_geometry_changed", root.GetProperty("lastCommitPointRefusal").GetProperty("reason").GetString());
        Assert.Equal("authority_window_not_foreground", root.GetProperty("lastSessionAuthority").GetProperty("refusalReason").GetString());

        var outcomes = root.GetProperty("lastStageOutcomes");
        Assert.True(outcomes.GetArrayLength() >= 11);
        Assert.Contains(outcomes.EnumerateArray(), e =>
            e.GetProperty("stage").GetString() == "Observe" && e.GetProperty("ok").GetBoolean());
        Assert.Contains(outcomes.EnumerateArray(), e =>
            e.GetProperty("stage").GetString() == "Execute"
            && e.GetProperty("ok").ValueKind == JsonValueKind.False
            && e.GetProperty("fault").GetString() == "verification_failed");
        Assert.Contains(outcomes.EnumerateArray(), e =>
            e.GetProperty("stage").GetString() == "Verify"
            && e.GetProperty("ok").ValueKind == JsonValueKind.Null
            && e.GetProperty("fault").GetString() == PipelineStageBoard.NeverRanFault);
    }

    [Fact]
    public void FailuresWhileAlreadyHaltedDoNotWriteAnotherDump()
    {
        var dumper = new HaltDiagnosticsDumper(_directory);
        RecoveryController recovery = Controller();
        dumper.Attach(recovery);

        var mode = RuntimeMode.Normal;
        for (var i = 0; i < 4; i++)
            recovery.HandleFailure(ref mode);

        Assert.Equal(1, dumper.Written);

        for (var i = 0; i < 5; i++)
            recovery.HandleFailure(ref mode);

        Assert.Equal(1, dumper.Written);
        Assert.Single(Directory.GetFiles(_directory, "halt-*.json"));
    }

    [Fact]
    public void AFailedProbeIsANewTransitionAndWritesASecondDump()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));
        var recovery = new RecoveryController(
            new TrustBoundary(TrustTier.Tier4_FullAutonomous),
            maxRetries: 2,
            clock: clock,
            baseCooldown: TimeSpan.FromSeconds(5));
        var dumper = new HaltDiagnosticsDumper(_directory);
        dumper.Attach(recovery);

        var mode = RuntimeMode.Normal;
        for (var i = 0; i < 4; i++)
            recovery.HandleFailure(ref mode);

        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.True(recovery.TryBeginAction(ref mode, out _));
        recovery.HandleFailure(ref mode);

        Assert.Equal(2, dumper.Written);
        Assert.Equal(2, Directory.GetFiles(_directory, "halt-*.json").Length);
    }

    [Fact]
    public void TheDumpPhotographsTheLastCommitRefusalEvenAfterALaterAuthorisedDecision()
    {
        var desktop = new FakeCommitDesktop();
        var validator = new CommitPointValidator(desktop, new FakeCommitHuman());
        desktop.Foreground = new IntPtr(0x2000);
        Assert.False(validator.Validate(Request()).IsAuthorised);

        desktop.Foreground = new IntPtr(0x1000);
        Assert.True(validator.Validate(Request()).IsAuthorised);

        var dumper = new HaltDiagnosticsDumper(_directory, new HaltDiagnosticsContext
        {
            LastCommitPointRefusal = () =>
            {
                CommitDecision? refusal = validator.LastRefusal;
                return refusal is { RefusalReason: { } reason }
                    ? new CommitPointRefusalDump(reason, null)
                    : null;
            }
        });

        RecoveryController recovery = Controller();
        dumper.Attach(recovery);
        var mode = RuntimeMode.Normal;
        for (var i = 0; i < 4; i++)
            recovery.HandleFailure(ref mode);

        using var doc = JsonDocument.Parse(File.ReadAllText(Assert.Single(Directory.GetFiles(_directory, "halt-*.json"))));
        Assert.Equal(
            CommitPointValidator.NotForegroundReason,
            doc.RootElement.GetProperty("lastCommitPointRefusal").GetProperty("reason").GetString());
    }

    [Fact]
    public void ADumpWriteFailureDoesNotThrowOutOfTheHaltTransition()
    {
        string blocker = Path.Combine(_directory, "not-a-directory");
        File.WriteAllText(blocker, "x");
        var dumper = new HaltDiagnosticsDumper(blocker);
        RecoveryController recovery = Controller();
        dumper.Attach(recovery);

        var mode = RuntimeMode.Normal;
        for (var i = 0; i < 4; i++)
            recovery.HandleFailure(ref mode);

        Assert.Equal(RecoveryState.Halted, recovery.State);
        Assert.Equal(1, recovery.Halts);
        Assert.Equal(0, dumper.Written);
    }

    private static GeometryEpoch Epoch() =>
        new(new IntPtr(0x1000), new PixelRect(0, 0, 1024, 768), 96, new IntPtr(0xABCD));

    private static CommitRequest Request() =>
        new(
            new GeometryStamp(Epoch(), DateTimeOffset.UnixEpoch),
            ScreenX: 500,
            ScreenY: 400,
            Scale: new GeometryShape(1024, 768, 96));

    private sealed class FakeCommitDesktop : ICommitEnvironment
    {
        public IntPtr Foreground { get; set; } = new IntPtr(0x1000);
        public IntPtr ForegroundWindow() => Foreground;
        public IntPtr RootWindowFromPoint(int x, int y) => new IntPtr(0x1000);
        public bool? IsCloaked(IntPtr window) => false;
        public GeometryEpoch ReadEpoch(IntPtr window) => Epoch();
    }

    private sealed class FakeCommitHuman : IHumanInputMonitor
    {
        public bool IsWatching => true;
        public TimeSpan? SinceLastHumanInput => TimeSpan.FromMinutes(1);
        public long HumanEventCount => 0;
        public long InjectedEventCount => 0;
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}
