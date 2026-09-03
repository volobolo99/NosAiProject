using System.Text.Json;
using Microsoft.Data.Sqlite;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate2;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Navigation;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Safety;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Formatting, exit codes and audit of <c>--step</c> (S4 / C2-4). The chain and
/// the executor stay where they are; this pins what the operator command prints
/// and records.
/// </summary>
public sealed class SingleStepCommandTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 2, 14, 0, 0, DateTimeKind.Utc);
    private static readonly IntPtr Session = 0x7100;
    private static readonly ActuationAuthority Operator = ActuationAuthority.Commanded(SingleStepCommand.Flag);

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "nosai-step-" + Guid.NewGuid().ToString("N"));

    public SingleStepCommandTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
    }

    private static MapGrid Grid()
    {
        var cells = new byte[100];
        for (var y = 0; y < 10; y++)
            cells[(y * 10) + 5] = (byte)MapCellFlags.WalkBlocked;
        return new MapGrid(mapId: 1, width: 10, height: 10, cells);
    }

    private static OccupancyView FreshView() =>
        new(Array.Empty<SelectableEntity>(), Now);

    private sealed class ProjectionStandIn : IScreenProjection
    {
        public bool Works { get; set; } = true;
        public string? Failure { get; set; }
        public GeometryShape Scale { get; set; } = new(1024, 768, 96);

        public bool TryProject(int mapX, int mapY, out int screenX, out int screenY, out string? failureReason)
        {
            screenX = 100 + (mapX * 32);
            screenY = 200 + (mapY * 16);
            failureReason = Works ? null : Failure ?? "projection_refused";
            return Works;
        }
    }

    private sealed class ChainRig
    {
        public string? AuthorityRefusal { get; set; }
        public bool Armed { get; set; } = true;
        public ProjectionStandIn Projection { get; } = new();

        public StepGuardChain Build() => new(
            () => AuthorityRefusal,
            () => RuntimeSafetyPolicy.SafeDefault with { LiveInputEnabled = Armed },
            Projection);
    }

    private static StepRequest Request(
        MapPoint? from = null,
        MapPoint? to = null,
        MapGrid? grid = null,
        OccupancyView? view = null) =>
        new(
            from ?? new MapPoint(2, 2),
            to ?? new MapPoint(3, 2),
            grid ?? Grid(),
            view ?? FreshView(),
            Now);

    private static GeometryStamp StatedGeometry(IntPtr window) => new(
        new GeometryEpoch(window, new PixelRect(0, 0, 1024, 768), 96, 0xABCD),
        new DateTimeOffset(Now));

    private static readonly MovementVerifier Fast =
        new(window: TimeSpan.FromMilliseconds(60), tolerance: TimeSpan.FromMilliseconds(10),
            pollInterval: TimeSpan.FromMilliseconds(2));

    private static SingleStepRun Run(
        StepRequest request,
        ActuationAuthority? authority = null,
        bool missingAuthority = false,
        ChainRig? rig = null,
        RecordingInputBackend? recorder = null,
        Func<PositionReading?>? readPosition = null)
    {
        rig ??= new ChainRig();
        recorder ??= new RecordingInputBackend();
        var gate = new GatedInputBackend(
            recorder, () => RuntimeSafetyPolicy.SafeDefault with { LiveInputEnabled = rig.Armed });
        var executor = new SingleStepExecutor(
            rig.Build(), gate, () => Session, Fast, readGeometry: StatedGeometry);
        ActuationAuthority used = missingAuthority ? default : authority ?? Operator;
        return SingleStepCommand.Execute(
            in request,
            executor,
            in used,
            readPosition ?? (() => null),
            timestampUtc: Now);
    }

    private static string GuardLine(string text, StepGuard guard) =>
        text.Split(["\r\n", "\n"], StringSplitOptions.None)
            .Single(line => line.StartsWith(guard + ":", StringComparison.Ordinal));

    [Fact]
    public void TheRuntimeWiresTheStepFlag()
    {
        string root = RepositoryRoot();
        string program = File.ReadAllText(Path.Combine(root, "src", "NosAi.Runtime", "Program.cs"));
        Assert.Contains("--step", program, StringComparison.Ordinal);
        Assert.Contains("SingleStepCommand.Run", program, StringComparison.Ordinal);
        Assert.Contains("\"--step\"", program, StringComparison.Ordinal);
    }

    [Fact]
    public void ShapeRefusalPrintsTheLadderWithNotEvaluatedBelow()
    {
        SingleStepRun run = Run(Request(from: new MapPoint(2, 2), to: new MapPoint(2, 2)));

        Assert.Contains("request: map=1 from=2,2 to=2,2", run.Text, StringComparison.Ordinal);
        Assert.Contains("Refused  " + StepGuardChain.ZeroLengthReason, GuardLine(run.Text, StepGuard.Shape), StringComparison.Ordinal);
        Assert.Contains("NotEvaluated", GuardLine(run.Text, StepGuard.Geometry), StringComparison.Ordinal);
        Assert.Contains("NotEvaluated", GuardLine(run.Text, StepGuard.Authority), StringComparison.Ordinal);
        Assert.Contains("NotEvaluated", GuardLine(run.Text, StepGuard.Policy), StringComparison.Ordinal);
        Assert.Contains("NotEvaluated", GuardLine(run.Text, StepGuard.Occupancy), StringComparison.Ordinal);
        Assert.Contains("NotEvaluated", GuardLine(run.Text, StepGuard.Projection), StringComparison.Ordinal);
        Assert.DoesNotContain("pixel:", run.Text, StringComparison.Ordinal);
        Assert.Contains("not-emitted: " + StepGuardChain.ZeroLengthReason, run.Text, StringComparison.Ordinal);
        Assert.Equal(SingleStepCommand.ExitGuardRefused, run.ExitCode);
    }

    [Fact]
    public void GeometryRefusalPrintsTheReasonAndLeavesLaterGuardsUnevaluated()
    {
        SingleStepRun run = Run(Request(from: new MapPoint(4, 3), to: new MapPoint(5, 3)));

        Assert.Contains("Passed", GuardLine(run.Text, StepGuard.Shape), StringComparison.Ordinal);
        Assert.Contains("Refused  " + StepGuardChain.DestinationBlockedPrefix, GuardLine(run.Text, StepGuard.Geometry), StringComparison.Ordinal);
        Assert.Contains("NotEvaluated", GuardLine(run.Text, StepGuard.Authority), StringComparison.Ordinal);
        Assert.Equal(SingleStepCommand.ExitGuardRefused, run.ExitCode);
    }

    [Fact]
    public void AuthorityRefusalPrintsTheSessionReason()
    {
        var rig = new ChainRig { AuthorityRefusal = "authority_no_session" };
        SingleStepRun run = Run(Request(), rig: rig);

        Assert.Contains("Passed", GuardLine(run.Text, StepGuard.Geometry), StringComparison.Ordinal);
        Assert.Contains("Refused  authority_no_session", GuardLine(run.Text, StepGuard.Authority), StringComparison.Ordinal);
        Assert.Contains("NotEvaluated", GuardLine(run.Text, StepGuard.Policy), StringComparison.Ordinal);
        Assert.Equal(SingleStepCommand.ExitGuardRefused, run.ExitCode);
    }

    [Fact]
    public void PolicyRefusalNamesLiveInputNotArmed()
    {
        var rig = new ChainRig { Armed = false };
        SingleStepRun run = Run(Request(), rig: rig);

        Assert.Contains("Passed", GuardLine(run.Text, StepGuard.Authority), StringComparison.Ordinal);
        Assert.Contains("Refused  " + StepGuardChain.InputNotArmedReason, GuardLine(run.Text, StepGuard.Policy), StringComparison.Ordinal);
        Assert.Contains("NotEvaluated", GuardLine(run.Text, StepGuard.Occupancy), StringComparison.Ordinal);
        Assert.Equal(SingleStepCommand.ExitGuardRefused, run.ExitCode);
    }

    [Fact]
    public void OccupancyRefusalNamesNeverObservedAndLeavesProjectionUnevaluated()
    {
        SingleStepRun run = Run(Request(view: new OccupancyView(null, Now)));

        Assert.Contains("Passed", GuardLine(run.Text, StepGuard.Policy), StringComparison.Ordinal);
        Assert.Contains("Refused  " + OccupancyFreshness.NeverObservedReason, GuardLine(run.Text, StepGuard.Occupancy), StringComparison.Ordinal);
        Assert.Contains("NotEvaluated", GuardLine(run.Text, StepGuard.Projection), StringComparison.Ordinal);
        Assert.Equal(SingleStepCommand.ExitGuardRefused, run.ExitCode);
    }

    [Fact]
    public void ProjectionRefusalPrintsTheCalibrationReason()
    {
        var rig = new ChainRig();
        rig.Projection.Works = false;
        rig.Projection.Failure = UncalibratedScreenProjection.NotCalibratedReason;
        SingleStepRun run = Run(Request(), rig: rig);

        Assert.Contains("Passed", GuardLine(run.Text, StepGuard.Occupancy), StringComparison.Ordinal);
        Assert.Contains("Refused  " + UncalibratedScreenProjection.NotCalibratedReason, GuardLine(run.Text, StepGuard.Projection), StringComparison.Ordinal);
        Assert.Equal(SingleStepCommand.ExitGuardRefused, run.ExitCode);
    }

    [Fact]
    public void MissingActuationAuthorityIsRefusedAndEmitsNothing()
    {
        var recorder = new RecordingInputBackend();
        SingleStepRun run = Run(Request(), missingAuthority: true, recorder: recorder);

        Assert.False(run.Step.Emitted);
        Assert.Contains(ActuationAuthority.MissingReason, run.Step.EmissionRefusal, StringComparison.Ordinal);
        Assert.Contains("not-emitted: " + SingleStepExecutor.ScopeRefusedPrefix, run.Text, StringComparison.Ordinal);
        Assert.Contains(ActuationAuthority.MissingReason, run.Text, StringComparison.Ordinal);
        Assert.Contains("pixel:", run.Text, StringComparison.Ordinal);
        Assert.Empty(recorder.Events);
        Assert.Equal(SingleStepCommand.ExitNotEmitted, run.ExitCode);

        RuntimeEvent authorization = Assert.Single(run.Events);
        Assert.Equal(SingleStepCommand.AuthorizationEventType, authorization.EventType);
        Assert.Equal("none", Field(Payload(authorization), SingleStepCommand.AuthorityField));
    }

    [Fact]
    public void ARefusedStepWritesOnlyTheAuthorizationEventAndNoInput()
    {
        var recorder = new RecordingInputBackend();
        var rig = new ChainRig { Armed = false };
        SingleStepRun run = Run(Request(), rig: rig, recorder: recorder);

        Assert.Empty(recorder.Events);
        RuntimeEvent authorization = Assert.Single(run.Events);
        Assert.Equal(SingleStepCommand.AuthorizationEventType, authorization.EventType);
        JsonElement payload = Payload(authorization);
        Assert.Equal("operator:" + SingleStepCommand.Flag, Field(payload, SingleStepCommand.AuthorityField));
        Assert.False(string.IsNullOrEmpty(Field(payload, SingleStepCommand.AuthorityField)));
        Assert.Equal("false", Field(payload, "authorized"));
        Assert.Equal(nameof(StepGuard.Policy), Field(payload, "refusedAt"));
        Assert.Equal(StepGuardChain.InputNotArmedReason, Field(payload, "refusalReason"));
        Assert.Equal(nameof(StepGuardState.NotEvaluated), Field(payload, "guard.Occupancy"));
    }

    [Fact]
    public void AnEmittedStepWritesThreeEventsInOrderEachNamingTheAuthority()
    {
        var recorder = new RecordingInputBackend();
        PositionReading arrival = new(new MapPoint(3, 2), Now.AddYears(1), DataSourceKind.Live);
        SingleStepRun run = Run(
            Request(),
            recorder: recorder,
            readPosition: () => arrival);

        Assert.True(run.Step.Emitted);
        Assert.Equal(MovementOutcome.Succeeded, run.Step.Verification.Outcome);
        Assert.Equal(new[] { "move-absolute:196,232", "click:Left" }, recorder.Events);
        Assert.Equal(SingleStepCommand.ExitSucceeded, run.ExitCode);
        Assert.Contains("pixel: 196,232 scale=1024x768 dpi=96", run.Text, StringComparison.Ordinal);
        Assert.Contains("verifier: Succeeded", run.Text, StringComparison.Ordinal);
        Assert.Contains("ms", run.Text, StringComparison.Ordinal);

        Assert.Equal(3, run.Events.Count);
        Assert.Equal(SingleStepCommand.AuthorizationEventType, run.Events[0].EventType);
        Assert.Equal(SingleStepCommand.EmissionEventType, run.Events[1].EventType);
        Assert.Equal(SingleStepCommand.VerificationEventType, run.Events[2].EventType);

        foreach (RuntimeEvent runtimeEvent in run.Events)
        {
            string named = Field(Payload(runtimeEvent), SingleStepCommand.AuthorityField);
            Assert.False(string.IsNullOrEmpty(named));
            Assert.Equal("operator:" + SingleStepCommand.Flag, named);
        }

        JsonElement emission = Payload(run.Events[1]);
        Assert.Equal("196", Field(emission, "screenX"));
        Assert.Equal("232", Field(emission, "screenY"));
        Assert.Equal(nameof(MovementOutcome.Succeeded), Field(Payload(run.Events[2]), "outcome"));
    }

    [Fact]
    public void EventLogReaderReplaysTheThreeEventsInTheSameOrder()
    {
        PositionReading arrival = new(new MapPoint(3, 2), Now.AddYears(1), DataSourceKind.Live);
        SingleStepRun run = Run(Request(), readPosition: () => arrival);
        string database = Path.Combine(_directory, "telemetry.db");

        SingleStepCommand.Persist(run.Events, database);
        EventLogReplay replay = EventLogReader.Read(database);

        Assert.True(replay.IsComplete);
        Assert.Equal(3, replay.EventCount);
        string[] types = replay.Events.Select(e => e.EventType).ToArray();
        Assert.Equal(
            new[]
            {
                SingleStepCommand.AuthorizationEventType,
                SingleStepCommand.EmissionEventType,
                SingleStepCommand.VerificationEventType
            },
            types);
        Assert.All(replay.Events, e =>
            Assert.False(string.IsNullOrEmpty(Field(Payload(e), SingleStepCommand.AuthorityField))));
    }

    [Fact]
    public void StalledAndUnobservedHaveDistinctExitCodes()
    {
        var stalled = new PositionReading(new MapPoint(2, 2), Now.AddYears(1), DataSourceKind.Live);
        SingleStepRun stalledRun = Run(Request(), readPosition: () => stalled);
        Assert.Equal(MovementOutcome.Stalled, stalledRun.Step.Verification.Outcome);
        Assert.Equal(SingleStepCommand.ExitStalled, stalledRun.ExitCode);

        SingleStepRun unobserved = Run(Request(), readPosition: () => null);
        Assert.True(unobserved.Step.Emitted);
        Assert.Equal(MovementOutcome.Unobserved, unobserved.Step.Verification.Outcome);
        Assert.Equal(SingleStepCommand.ExitUnobserved, unobserved.ExitCode);

        var elsewhere = new PositionReading(new MapPoint(2, 3), Now.AddYears(1), DataSourceKind.Live);
        SingleStepRun displaced = Run(Request(), readPosition: () => elsewhere);
        Assert.Equal(SingleStepCommand.ExitDisplaced, displaced.ExitCode);
    }

    [Fact]
    public void NotAdjacentRefusalPrintsTheExactShapeReasonAndLeavesLaterGuardsUnevaluated()
    {
        var recorder = new RecordingInputBackend();
        SingleStepRun run = Run(Request(from: new MapPoint(2, 2), to: new MapPoint(4, 2)), recorder: recorder);

        Assert.Contains("Refused  " + StepGuardChain.NotAdjacentPrefix + ":2,0", GuardLine(run.Text, StepGuard.Shape), StringComparison.Ordinal);
        Assert.Contains("NotEvaluated", GuardLine(run.Text, StepGuard.Geometry), StringComparison.Ordinal);
        Assert.Contains("NotEvaluated", GuardLine(run.Text, StepGuard.Authority), StringComparison.Ordinal);
        Assert.Contains("NotEvaluated", GuardLine(run.Text, StepGuard.Policy), StringComparison.Ordinal);
        Assert.Contains("NotEvaluated", GuardLine(run.Text, StepGuard.Occupancy), StringComparison.Ordinal);
        Assert.Contains("NotEvaluated", GuardLine(run.Text, StepGuard.Projection), StringComparison.Ordinal);
        Assert.Equal(SingleStepCommand.ExitGuardRefused, run.ExitCode);
        Assert.Empty(recorder.Events);
        Assert.Equal(SingleStepCommand.AuthorizationEventType, Assert.Single(run.Events).EventType);
    }

    [Fact]
    public void OriginNotWalkableRefusalPrintsTheGeometryReason()
    {
        SingleStepRun run = Run(Request(from: new MapPoint(5, 2), to: new MapPoint(6, 2)));

        Assert.Contains("Passed", GuardLine(run.Text, StepGuard.Shape), StringComparison.Ordinal);
        Assert.Contains("Refused  " + StepGuardChain.OriginNotWalkablePrefix, GuardLine(run.Text, StepGuard.Geometry), StringComparison.Ordinal);
        Assert.Contains("NotEvaluated", GuardLine(run.Text, StepGuard.Authority), StringComparison.Ordinal);
        Assert.Equal(SingleStepCommand.ExitGuardRefused, run.ExitCode);
    }

    [Fact]
    public void OccupiedDestinationPrintsTheOccupancyReasonAndWritesOnlyAuthorization()
    {
        var recorder = new RecordingInputBackend();
        var occupied = new OccupancyView(
            new[] { new SelectableEntity(77, new MapPoint(3, 2), null, Now) },
            Now);
        SingleStepRun run = Run(Request(view: occupied), recorder: recorder);

        Assert.Contains("Passed", GuardLine(run.Text, StepGuard.Policy), StringComparison.Ordinal);
        Assert.Contains(
            "Refused  " + OccupancyFreshness.DestinationOccupiedPrefix + ":77",
            GuardLine(run.Text, StepGuard.Occupancy),
            StringComparison.Ordinal);
        Assert.Contains("NotEvaluated", GuardLine(run.Text, StepGuard.Projection), StringComparison.Ordinal);
        Assert.Empty(recorder.Events);
        RuntimeEvent authorization = Assert.Single(run.Events);
        Assert.Equal(SingleStepCommand.AuthorizationEventType, authorization.EventType);
        Assert.DoesNotContain(run.Events, e => e.EventType == SingleStepCommand.EmissionEventType);
        Assert.Equal(SingleStepCommand.ExitGuardRefused, run.ExitCode);
    }

    [Fact]
    public void AuditPayloadsAreFlatJsonWithNoMachinePaths()
    {
        PositionReading arrival = new(new MapPoint(3, 2), Now.AddYears(1), DataSourceKind.Live);
        SingleStepRun run = Run(Request(), readPosition: () => arrival);

        Assert.Equal(3, run.Events.Count);
        foreach (RuntimeEvent runtimeEvent in run.Events)
        {
            using JsonDocument document = JsonDocument.Parse(runtimeEvent.PayloadJson);
            Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                Assert.Equal(JsonValueKind.String, property.Value.ValueKind);
                string value = property.Value.GetString() ?? string.Empty;
                Assert.DoesNotContain(":\\", value, StringComparison.Ordinal);
                Assert.DoesNotContain("/Users/", value, StringComparison.Ordinal);
                Assert.DoesNotContain("C:\\", value, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void TheLiveCommandTakesTheGatedBackendFromCompositionAndNamesTheOperator()
    {
        string root = RepositoryRoot();
        string command = File.ReadAllText(Path.Combine(root, "src", "NosAi.Runtime", "Navigation", "SingleStepCommand.cs"));
        string program = File.ReadAllText(Path.Combine(root, "src", "NosAi.Runtime", "Program.cs"));

        Assert.Contains("RuntimeComposition.CreateSafe", command, StringComparison.Ordinal);
        Assert.Contains("ActuationAuthority.Commanded(Flag)", command, StringComparison.Ordinal);
        Assert.Contains("components.InputBackend is not GatedInputBackend", command, StringComparison.Ordinal);
        Assert.DoesNotContain("new Win32InputBackend", command, StringComparison.Ordinal);
        Assert.DoesNotContain("--force", command, StringComparison.Ordinal);
        Assert.DoesNotContain("--force", program, StringComparison.Ordinal);
        Assert.Contains("SingleStepCommand.ExitUsage", program, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureVerified", command, StringComparison.Ordinal);
    }

    private static JsonElement Payload(RuntimeEvent runtimeEvent) =>
        JsonDocument.Parse(runtimeEvent.PayloadJson).RootElement;

    private static string Field(JsonElement payload, string name) =>
        payload.GetProperty(name).GetString() ?? string.Empty;

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;
        Assert.True(directory is not null, "Repository root not found.");
        return directory!.FullName;
    }
}
