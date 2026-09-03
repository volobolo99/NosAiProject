using System.Text.Json;
using Microsoft.Data.Sqlite;
using NosAi.Navigation.Pathfinding;
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
/// Formatting, dry-run isolation and replan-limit of <c>--walk</c> (C2-7). The
/// controller, the chain and the executor stay where they are; this pins what
/// the operator command prints, that a dry run never reaches the input
/// backend, and that the fourth consecutive replan is an abandonment.
/// </summary>
public sealed class WalkCommandTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 2, 14, 0, 0, DateTimeKind.Utc);
    private static readonly IntPtr Session = 0x7200;
    private static readonly ActuationAuthority Operator = ActuationAuthority.Commanded(WalkCommand.Flag);

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "nosai-walk-" + Guid.NewGuid().ToString("N"));

    public WalkCommandTests() => Directory.CreateDirectory(_directory);

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

    private static MapGrid OpenMap() => new(mapId: 1, width: 10, height: 10, new byte[100]);

    private static OccupancyView FreshView() =>
        new(Array.Empty<SelectableEntity>(), Now);

    private static OccupancyView Occupied(MapPoint cell) =>
        new(new[] { new SelectableEntity(99, cell, null, Now) }, Now);

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

    private sealed class WalkRig
    {
        public ChainRig Chain { get; } = new();
        public RecordingInputBackend Recorder { get; } = new();
        public PathWalkController Controller { get; } = new();

        public GatedInputBackend Gate() => new(
            Recorder, () => RuntimeSafetyPolicy.SafeDefault with { LiveInputEnabled = Chain.Armed });
    }

    private static WalkRun Run(
        MapPoint destination,
        MapPoint? origin = null,
        MapGrid? grid = null,
        OccupancyView? view = null,
        bool dryRun = false,
        WalkRig? rig = null,
        ActuationAuthority? authority = null,
        Func<PositionReading?>? readPosition = null)
    {
        rig ??= new WalkRig();
        MapPoint from = origin ?? new MapPoint(2, 2);
        MapGrid map = grid ?? OpenMap();
        OccupancyView occupancy = view ?? FreshView();
        StepGuardChain chain = rig.Chain.Build();
        SingleStepExecutor executor = new(
            chain, rig.Gate(), () => Session, Fast, readGeometry: StatedGeometry);
        ActuationAuthority used = authority ?? Operator;
        return WalkCommand.Execute(
            destination,
            from,
            in map,
            occupancy,
            rig.Controller,
            chain,
            executor,
            in used,
            readPosition ?? (() => null),
            dryRun,
            timestampUtc: Now);
    }

    private static string GuardLine(string text, StepGuard guard) =>
        text.Split(["\r\n", "\n"], StringSplitOptions.None)
            .Single(line => line.StartsWith(guard + ":", StringComparison.Ordinal));

    private static string Preview(StepRequest request, ChainRig? rig = null)
    {
        rig ??= new ChainRig();
        StepAuthorization authorization = rig.Build().Authorize(in request);
        return WalkCommand.FormatPreview(in request, authorization);
    }

    [Fact]
    public void TheRuntimeWiresTheWalkFlag()
    {
        string root = RepositoryRoot();
        string program = File.ReadAllText(Path.Combine(root, "src", "NosAi.Runtime", "Program.cs"));
        Assert.Contains(WalkCommand.Flag, program, StringComparison.Ordinal);
        Assert.Contains("WalkCommand.Run", program, StringComparison.Ordinal);
        Assert.Contains("\"" + WalkCommand.Flag + "\"", program, StringComparison.Ordinal);
        Assert.Contains(WalkCommand.DryRunFlag, program, StringComparison.Ordinal);
    }

    [Fact]
    public void ShapeRefusalPrintsTheLadderWithNotEvaluatedBelow()
    {
        string text = Preview(Request(from: new MapPoint(2, 2), to: new MapPoint(2, 2)));

        Assert.Contains("request: map=1 from=2,2 to=2,2", text, StringComparison.Ordinal);
        Assert.Contains("Refused  " + StepGuardChain.ZeroLengthReason, GuardLine(text, StepGuard.Shape), StringComparison.Ordinal);
        Assert.Contains("NotEvaluated", GuardLine(text, StepGuard.Geometry), StringComparison.Ordinal);
        Assert.Contains("NotEvaluated", GuardLine(text, StepGuard.Authority), StringComparison.Ordinal);
        Assert.Contains("NotEvaluated", GuardLine(text, StepGuard.Policy), StringComparison.Ordinal);
        Assert.Contains("NotEvaluated", GuardLine(text, StepGuard.Occupancy), StringComparison.Ordinal);
        Assert.Contains("NotEvaluated", GuardLine(text, StepGuard.Projection), StringComparison.Ordinal);
        Assert.DoesNotContain("pixel:", text, StringComparison.Ordinal);
        Assert.Contains("not-emitted: " + StepGuardChain.ZeroLengthReason, text, StringComparison.Ordinal);
    }

    [Fact]
    public void GeometryRefusalPrintsTheReasonAndLeavesLaterGuardsUnevaluated()
    {
        string text = Preview(Request(from: new MapPoint(4, 3), to: new MapPoint(5, 3)));

        Assert.Contains("Passed", GuardLine(text, StepGuard.Shape), StringComparison.Ordinal);
        Assert.Contains("Refused  " + StepGuardChain.DestinationBlockedPrefix, GuardLine(text, StepGuard.Geometry), StringComparison.Ordinal);
        Assert.Contains("NotEvaluated", GuardLine(text, StepGuard.Authority), StringComparison.Ordinal);
        Assert.Contains("NotEvaluated", GuardLine(text, StepGuard.Policy), StringComparison.Ordinal);
        Assert.Contains("NotEvaluated", GuardLine(text, StepGuard.Occupancy), StringComparison.Ordinal);
        Assert.Contains("NotEvaluated", GuardLine(text, StepGuard.Projection), StringComparison.Ordinal);
    }

    [Fact]
    public void AuthorityRefusalPrintsTheSessionReason()
    {
        var rig = new WalkRig();
        rig.Chain.AuthorityRefusal = "authority_no_session";
        WalkRun run = Run(new MapPoint(3, 2), dryRun: true, rig: rig);

        Assert.Contains("Passed", GuardLine(run.Text, StepGuard.Geometry), StringComparison.Ordinal);
        Assert.Contains("Refused  authority_no_session", GuardLine(run.Text, StepGuard.Authority), StringComparison.Ordinal);
        Assert.Contains("NotEvaluated", GuardLine(run.Text, StepGuard.Policy), StringComparison.Ordinal);
        Assert.Contains("NotEvaluated", GuardLine(run.Text, StepGuard.Occupancy), StringComparison.Ordinal);
        Assert.Contains("NotEvaluated", GuardLine(run.Text, StepGuard.Projection), StringComparison.Ordinal);
        Assert.Empty(rig.Recorder.Events);
        Assert.Equal(WalkCommand.ExitPreviewed, run.ExitCode);
    }

    [Fact]
    public void PolicyRefusalNamesLiveInputNotArmed()
    {
        var rig = new WalkRig();
        rig.Chain.Armed = false;
        WalkRun run = Run(new MapPoint(3, 2), dryRun: true, rig: rig);

        Assert.Contains("Passed", GuardLine(run.Text, StepGuard.Authority), StringComparison.Ordinal);
        Assert.Contains("Refused  " + StepGuardChain.InputNotArmedReason, GuardLine(run.Text, StepGuard.Policy), StringComparison.Ordinal);
        Assert.Contains("NotEvaluated", GuardLine(run.Text, StepGuard.Occupancy), StringComparison.Ordinal);
        Assert.Contains("NotEvaluated", GuardLine(run.Text, StepGuard.Projection), StringComparison.Ordinal);
        Assert.Empty(rig.Recorder.Events);
    }

    [Fact]
    public void OccupancyRefusalNamesNeverObservedAndLeavesProjectionUnevaluated()
    {
        var rig = new WalkRig();
        WalkRun run = Run(new MapPoint(3, 2), view: new OccupancyView(null, Now), dryRun: true, rig: rig);

        Assert.Contains("Passed", GuardLine(run.Text, StepGuard.Policy), StringComparison.Ordinal);
        Assert.Contains("Refused  " + OccupancyFreshness.NeverObservedReason, GuardLine(run.Text, StepGuard.Occupancy), StringComparison.Ordinal);
        Assert.Contains("NotEvaluated", GuardLine(run.Text, StepGuard.Projection), StringComparison.Ordinal);
        Assert.Empty(rig.Recorder.Events);
    }

    [Fact]
    public void ProjectionRefusalPrintsTheCalibrationReason()
    {
        var rig = new WalkRig();
        rig.Chain.Projection.Works = false;
        rig.Chain.Projection.Failure = UncalibratedScreenProjection.NotCalibratedReason;
        WalkRun run = Run(new MapPoint(3, 2), dryRun: true, rig: rig);

        Assert.Contains("Passed", GuardLine(run.Text, StepGuard.Occupancy), StringComparison.Ordinal);
        Assert.Contains("Refused  " + UncalibratedScreenProjection.NotCalibratedReason, GuardLine(run.Text, StepGuard.Projection), StringComparison.Ordinal);
        Assert.Empty(rig.Recorder.Events);
    }

    [Fact]
    public void DryRunDoesNotReachTheInputBackend()
    {
        var rig = new WalkRig();
        WalkRun run = Run(new MapPoint(6, 2), dryRun: true, rig: rig);

        Assert.Contains("path:", run.Text, StringComparison.Ordinal);
        Assert.Contains("(2,2)", run.Text, StringComparison.Ordinal);
        Assert.Contains("(6,2)", run.Text, StringComparison.Ordinal);
        Assert.Contains("pixel:", run.Text, StringComparison.Ordinal);
        Assert.Contains("not-emitted: " + WalkCommand.DryRunNotEmittedReason, run.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("click:", run.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("move-absolute:", run.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("verifier:", run.Text, StringComparison.Ordinal);
        Assert.Empty(rig.Recorder.Events);
        Assert.Equal(0, run.StepsEmitted);
        Assert.Equal(WalkCommand.ExitPreviewed, run.ExitCode);
        Assert.Equal(WalkCommand.DryRunNotEmittedReason, run.StoppedBecause);
    }

    [Fact]
    public void TheReplanLimitStopsWithoutReachingTheInputBackend()
    {
        var rig = new WalkRig();
        var origin = new MapPoint(2, 2);
        var destination = new MapPoint(8, 2);
        OccupancyView blocked = Occupied(new MapPoint(3, 2));

        WalkRun run = Run(destination, origin, OpenMap(), blocked, dryRun: false, rig: rig);

        Assert.StartsWith(PathWalkController.ReplanLimitPrefix + ":", run.StoppedBecause);
        Assert.Equal(WalkCommand.ExitAbandoned, run.ExitCode);
        Assert.Equal(ReplanPolicy.DefaultMaxConsecutiveReplans, run.ReplansExecuted);
        Assert.Equal(0, run.StepsEmitted);
        Assert.Empty(rig.Recorder.Events);
        Assert.DoesNotContain("click:", run.Text, StringComparison.Ordinal);
        Assert.Contains("replan 1:", run.Text, StringComparison.Ordinal);
        Assert.Contains("replan 2:", run.Text, StringComparison.Ordinal);
        Assert.Contains("replan 3:", run.Text, StringComparison.Ordinal);
        Assert.Contains("stopped: " + PathWalkController.ReplanLimitPrefix, run.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void EventsNameTheWalkAuthorityAndNeverOmitIt()
    {
        var rig = new WalkRig();
        WalkRun run = Run(new MapPoint(6, 2), dryRun: true, rig: rig);

        Assert.Contains(run.Events, e => e.EventType == WalkCommand.AdmissionEventType);
        Assert.Contains(run.Events, e => e.EventType == WalkCommand.FinishedEventType);
        Assert.DoesNotContain(run.Events, e => e.EventType == SingleStepCommand.EmissionEventType);

        string expected = "operator:" + WalkCommand.Flag;
        foreach (RuntimeEvent runtimeEvent in run.Events)
        {
            string named = Field(Payload(runtimeEvent), WalkCommand.AuthorityField);
            Assert.False(string.IsNullOrEmpty(named));
            Assert.Equal(expected, named);
        }
    }

    [Fact]
    public void ALiveStepAbortWritesAuthorizationWithoutEmissionAndStops()
    {
        var rig = new WalkRig();
        rig.Chain.Armed = false;
        PositionReading arrival = new(new MapPoint(3, 2), Now.AddYears(1), DataSourceKind.Live);
        WalkRun run = Run(new MapPoint(3, 2), dryRun: false, rig: rig, readPosition: () => arrival);

        Assert.Empty(rig.Recorder.Events);
        Assert.Equal(0, run.StepsEmitted);
        Assert.Equal(WalkCommand.ExitGuardRefused, run.ExitCode);
        Assert.Equal(StepGuardChain.InputNotArmedReason, run.StoppedBecause);
        Assert.Contains("Refused  " + StepGuardChain.InputNotArmedReason, run.Text, StringComparison.Ordinal);
        Assert.Contains(run.Events, e => e.EventType == SingleStepCommand.AuthorizationEventType);
        Assert.DoesNotContain(run.Events, e => e.EventType == SingleStepCommand.EmissionEventType);

        RuntimeEvent authorization = run.Events.Single(e => e.EventType == SingleStepCommand.AuthorizationEventType);
        Assert.Equal("operator:" + WalkCommand.Flag, Field(Payload(authorization), WalkCommand.AuthorityField));
    }

    [Fact]
    public void AnAdjacentWalkArrivesAndRecordsTheVerifier()
    {
        var rig = new WalkRig();
        PositionReading arrival = new(new MapPoint(3, 2), Now.AddYears(1), DataSourceKind.Live);
        WalkRun run = Run(new MapPoint(3, 2), dryRun: false, rig: rig, readPosition: () => arrival);

        Assert.Equal(WalkCommand.ExitArrived, run.ExitCode);
        Assert.Equal(nameof(WalkOutcome.Arrived), run.StoppedBecause);
        Assert.Equal(1, run.StepsEmitted);
        Assert.Equal(1, run.StepsSucceeded);
        Assert.Equal(0, run.StepsStalled);
        Assert.Equal(0, run.ReplansExecuted);
        Assert.Equal(new[] { "move-absolute:196,232", "click:Left" }, rig.Recorder.Events);
        Assert.Contains("pixel: 196,232 scale=1024x768 dpi=96", run.Text, StringComparison.Ordinal);
        Assert.Contains("verifier: Succeeded", run.Text, StringComparison.Ordinal);
        Assert.Contains("ms", run.Text, StringComparison.Ordinal);
        Assert.Contains(run.Events, e => e.EventType == SingleStepCommand.EmissionEventType);
        Assert.Contains(run.Events, e => e.EventType == SingleStepCommand.VerificationEventType);
        Assert.All(run.Events, e =>
            Assert.Equal("operator:" + WalkCommand.Flag, Field(Payload(e), WalkCommand.AuthorityField)));
    }

    [Fact]
    public void EventLogReaderReplaysWalkEventsWithTheAuthorityAttached()
    {
        var rig = new WalkRig();
        WalkRun run = Run(new MapPoint(4, 2), dryRun: true, rig: rig);
        string database = Path.Combine(_directory, "telemetry.db");

        SingleStepCommand.Persist(run.Events, database);
        EventLogReplay replay = EventLogReader.Read(database);

        Assert.True(replay.IsComplete);
        Assert.Equal(run.Events.Count, replay.EventCount);
        RuntimeEvent[] replayed = replay.Events.ToArray();
        Assert.Equal(WalkCommand.AdmissionEventType, replayed[0].EventType);
        Assert.Equal(WalkCommand.FinishedEventType, replayed[^1].EventType);
        Assert.All(replayed, e =>
            Assert.False(string.IsNullOrEmpty(Field(Payload(e), WalkCommand.AuthorityField))));
    }

    [Fact]
    public void ABlockedDestinationProducesNoPathAndEmitsNothing()
    {
        var rig = new WalkRig();
        WalkRun run = Run(
            new MapPoint(8, 3),
            origin: new MapPoint(2, 3),
            grid: Grid(),
            dryRun: false,
            rig: rig);

        Assert.Equal(PathFailureReason.Unreachable.ToString(), run.StoppedBecause);
        Assert.Equal(WalkCommand.ExitNoPath, run.ExitCode);
        Assert.Empty(rig.Recorder.Events);
        Assert.Equal(0, run.StepsEmitted);
    }

    [Fact]
    public void ANeverObservedWorldAbandonsBeforeAnyEmission()
    {
        var rig = new WalkRig();
        WalkRun run = Run(
            new MapPoint(6, 2),
            view: new OccupancyView(null, Now),
            dryRun: false,
            rig: rig);

        Assert.Equal(OccupancyFreshness.NeverObservedReason, run.StoppedBecause);
        Assert.Equal(WalkCommand.ExitAbandoned, run.ExitCode);
        Assert.Empty(rig.Recorder.Events);
        Assert.Equal(0, run.StepsEmitted);
        Assert.DoesNotContain("verifier:", run.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AlreadyOnTheDestinationIsAnArrivalWithoutEmission()
    {
        var rig = new WalkRig();
        var here = new MapPoint(4, 4);
        WalkRun run = Run(here, origin: here, dryRun: false, rig: rig);

        Assert.Equal(WalkCommand.ExitArrived, run.ExitCode);
        Assert.Equal(nameof(WalkOutcome.Arrived), run.StoppedBecause);
        Assert.Equal(0, run.StepsEmitted);
        Assert.Empty(rig.Recorder.Events);
        Assert.Contains("cells: 1", run.Text, StringComparison.Ordinal);
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
