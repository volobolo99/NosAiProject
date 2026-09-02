using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using NosAi.LiveIntegration;
using NosAi.Navigation.Pathfinding;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate2;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Orchestration;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Testing;

namespace NosAi.Runtime.Navigation;

/// <summary>The printed report, the exit code, and the audit events of one <c>--walk</c>.</summary>
/// <param name="Text">What the operator sees: the path, each segment, and the totals.</param>
/// <param name="ExitCode">Zero only when the character is on the destination, or when a dry run finished previewing an admitted path.</param>
/// <param name="Events">
/// Admission, each live step (authorization always; emission and verification
/// only when the irreversible step left), each adopted replan, and the finish.
/// Every payload names the act's authority.
/// </param>
/// <param name="StepsEmitted">Irreversible steps that actually left.</param>
/// <param name="StepsSucceeded">Emitted steps the verifier saw arrive on the asked-for cell.</param>
/// <param name="StepsStalled">Emitted steps the verifier watched stay on the origin.</param>
/// <param name="ReplansExecuted">Replacement paths the controller adopted.</param>
/// <param name="StoppedBecause">Why the loop ended, named. Null only if nothing was written.</param>
public readonly record struct WalkRun(
    string Text,
    int ExitCode,
    IReadOnlyList<RuntimeEvent> Events,
    long StepsEmitted,
    long StepsSucceeded,
    long StepsStalled,
    long ReplansExecuted,
    string? StoppedBecause);

/// <summary>
/// The operator command that walks an admitted path to an absolute cell (C2-7).
/// </summary>
/// <remarks>
/// <para>
/// A cycle around the four methods <see cref="PathWalkController"/> already
/// exposes: <see cref="PathWalkController.TryStart"/>, <see cref="PathWalkController.Next"/>,
/// <see cref="PathWalkController.NoteStepOutcome"/>, and
/// <see cref="PathWalkController.TryAdoptReplan"/>. It does not decide when to
/// replan, when to stop, or which cell to step onto. Those answers come from the
/// controller. The irreversible step, when there is one, goes through
/// <see cref="SingleStepExecutor"/>; this command has no other route to the
/// input backend.
/// </para>
/// <para>
/// The authority of the act is <see cref="ActuationAuthority.Commanded"/> named
/// <c>--walk</c>: no scope opens without it (ADR-0020). The same construction
/// <c>--step</c> uses, not a second one. The command does not arm live input
/// and does not take a session-authority probe. Both would be acts it did not
/// print as its own.
/// </para>
/// <para>
/// <c>--dry-run</c> admits the path and prints the guard ladder for every
/// segment. It never calls <see cref="SingleStepExecutor.Step"/>, so nothing
/// reaches the input backend even when every guard would have passed.
/// </para>
/// </remarks>
public static class WalkCommand
{
    /// <summary>The flag, and the name recorded as the commanded authority.</summary>
    public const string Flag = "--walk";

    /// <summary>
    /// Admits and prints without emitting. The operator's look at a path before
    /// leaving it to walk.
    /// </summary>
    public const string DryRunFlag = "--dry-run";

    /// <summary>Where the audit events are attributed.</summary>
    public const string SourceModule = "Navigation";

    /// <summary>Session id of an operator walk, not a Gate 2 cycle.</summary>
    public const string OperatorSessionId = "operator-walk";

    /// <summary>Written when a path is offered to the controller, admitted or not.</summary>
    public const string AdmissionEventType = "walk.admission";

    /// <summary>Written when a replacement path is adopted.</summary>
    public const string ReplanEventType = "walk.replan";

    /// <summary>Written once, when the loop ends, with the totals and the reason.</summary>
    public const string FinishedEventType = "walk.finished";

    /// <summary>JSON field every audit event carries. Never omitted, never empty.</summary>
    public const string AuthorityField = "authority";

    /// <summary>
    /// Why a dry-run segment was not emitted. Named here because the command is
    /// what chose not to call the executor; the guards may have authorised it.
    /// </summary>
    public const string DryRunNotEmittedReason = "walk_dry_run";

    /// <summary>The character is on the cell that was asked for, or was already there.</summary>
    public const int ExitArrived = 0;

    /// <summary>A dry run finished previewing an admitted path. Nothing was emitted.</summary>
    public const int ExitPreviewed = 0;

    /// <summary>The path was refused before anything was emitted.</summary>
    public const int ExitNotAdmitted = 1;

    /// <summary>The flag was present without two integer cell coordinates.</summary>
    public const int ExitUsage = 2;

    /// <summary>
    /// The walk ended without arriving: replan limit, displacement, unverified
    /// limit, a world nobody has looked at, a replan that could not be adopted.
    /// </summary>
    public const int ExitAbandoned = 3;

    /// <summary>The controller asked for a step and a guard, or the gate, refused it.</summary>
    public const int ExitGuardRefused = 4;

    /// <summary>The planner produced no route to the asked-for cell.</summary>
    public const int ExitNoPath = 5;

    /// <summary>Reported off Windows, where there is no session window to bind.</summary>
    public const string NotWindowsReason = "walk_requires_windows";

    /// <summary>Reported when the composed backend is not the gated one.</summary>
    public const string UngatedBackendReason = "walk_input_backend_not_gated";

    /// <summary>Console entry for <c>--walk &lt;gx&gt; &lt;gy&gt;</c>, optionally <c>--dry-run</c>.</summary>
    public static int Run(int gx, int gy, bool dryRun = false)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine($"[REFUSED] {NotWindowsReason}");
            return ExitAbandoned;
        }

        return RunWindows(gx, gy, dryRun);
    }

    /// <summary>
    /// Plans, admits, and either previews or walks to <paramref name="destination"/>.
    /// Tests pass a recording backend through <paramref name="executor"/>; the live
    /// command builds that executor from <see cref="RuntimeComposition"/>.
    /// </summary>
    /// <param name="authority">
    /// Required. The live command always passes
    /// <see cref="ActuationAuthority.Commanded"/>(<see cref="Flag"/>). A default
    /// value is <see cref="ActuationAuthorityKind.None"/> and the gate refuses it
    /// by name — that is the missing-authority case, not a way to skip the
    /// parameter.
    /// </param>
    /// <param name="chain">
    /// The same ladder the executor holds. Dry-run asks it per segment because
    /// the executor does not expose it; live emission still goes only through
    /// <see cref="SingleStepExecutor.Step"/>.
    /// </param>
    public static WalkRun Execute(
        MapPoint destination,
        MapPoint origin,
        in MapGrid grid,
        OccupancyView view,
        PathWalkController controller,
        StepGuardChain chain,
        SingleStepExecutor executor,
        in ActuationAuthority authority,
        Func<PositionReading?> readPosition,
        bool dryRun,
        string sessionId = OperatorSessionId,
        DateTime? timestampUtc = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(readPosition);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        DateTime atUtc = timestampUtc ?? TimeProvider.System.GetUtcNow().UtcDateTime;
        string named = NamedAuthority(in authority);
        var text = new StringBuilder();
        var events = new List<RuntimeEvent>();
        long stepsEmitted = 0;
        long stepsSucceeded = 0;
        long stepsStalled = 0;

        text.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"=== walk to {destination.X},{destination.Y}{(dryRun ? " dry-run" : string.Empty)} ==="));

        WalkRun Finish(int exitCode, string? stoppedBecause, long replansExecuted)
        {
            AppendSummary(text, stepsEmitted, stepsSucceeded, stepsStalled, replansExecuted, stoppedBecause);
            events.Add(Event(sessionId, FinishedEventType, atUtc, FinishedPayload(
                named, stepsEmitted, stepsSucceeded, stepsStalled, replansExecuted, stoppedBecause,
                arrived: exitCode == ExitArrived && !dryRun)));
            return new WalkRun(
                text.ToString(),
                exitCode,
                events,
                stepsEmitted,
                stepsSucceeded,
                stepsStalled,
                replansExecuted,
                stoppedBecause);
        }

        if (!grid.IsLoaded)
        {
            text.AppendLine(PathRevalidation.GridNotLoadedReason);
            events.Add(Event(sessionId, AdmissionEventType, atUtc, AdmissionPayload(
                named, in grid, origin, destination, admitted: false, cells: 0,
                PathRevalidation.GridNotLoadedReason)));
            return Finish(ExitNotAdmitted, PathRevalidation.GridNotLoadedReason, 0);
        }

        if (origin == destination)
        {
            var here = new[] { origin };
            text.AppendLine(FormatPath(here));
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"cells: {here.Length}"));
            events.Add(Event(sessionId, AdmissionEventType, atUtc, AdmissionPayload(
                named, in grid, origin, destination, admitted: true, cells: 1, refusalReason: null)));
            return Finish(ExitArrived, nameof(WalkOutcome.Arrived), 0);
        }

        IReadOnlyList<MapPoint> path = Plan(in grid, origin, destination, out CalculatedPathResult planned);
        if (path.Count == 0)
        {
            string noPath = planned.FailureReason.ToString();
            text.AppendLine(noPath);
            events.Add(Event(sessionId, AdmissionEventType, atUtc, AdmissionPayload(
                named, in grid, origin, destination, admitted: false, cells: 0, noPath)));
            return Finish(ExitNoPath, noPath, 0);
        }

        if (!controller.TryStart(in grid, path, origin, out string? admissionRefusal))
        {
            text.AppendLine(FormatPath(path));
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"cells: {path.Count}"));
            text.AppendLine(admissionRefusal);
            events.Add(Event(sessionId, AdmissionEventType, atUtc, AdmissionPayload(
                named, in grid, origin, destination, admitted: false, cells: path.Count, admissionRefusal)));
            return Finish(ExitNotAdmitted, admissionRefusal, 0);
        }

        text.AppendLine(FormatPath(path));
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"cells: {path.Count}"));
        events.Add(Event(sessionId, AdmissionEventType, atUtc, AdmissionPayload(
            named, in grid, origin, destination, admitted: true, cells: path.Count, refusalReason: null)));

        if (dryRun)
        {
            IReadOnlyList<MapPoint> admitted = controller.Path;
            for (int i = 0; i + 1 < admitted.Count; i++)
            {
                var request = new StepRequest(admitted[i], admitted[i + 1], grid, view, atUtc);
                StepAuthorization authorization = chain.Authorize(in request);
                text.Append(FormatPreview(in request, authorization));
            }

            return Finish(ExitPreviewed, DryRunNotEmittedReason, 0);
        }

        MapPoint at = origin;
        while (true)
        {
            if (timestampUtc is null)
                atUtc = TimeProvider.System.GetUtcNow().UtcDateTime;

            WalkDecision decision = controller.Next(in grid, at, in view, atUtc);
            switch (decision.Outcome)
            {
                case WalkOutcome.Arrived:
                    return Finish(ExitArrived, nameof(WalkOutcome.Arrived), controller.ReplansAdopted);

                case WalkOutcome.Abandoned:
                    return Finish(
                        ExitAbandoned,
                        decision.Reason ?? PathWalkController.NoPathReason,
                        controller.ReplansAdopted);

                case WalkOutcome.Replan:
                {
                    IReadOnlyList<MapPoint> replacement = Plan(in grid, at, destination, out CalculatedPathResult replan);
                    if (replacement.Count == 0)
                    {
                        string noPath = replan.FailureReason.ToString();
                        text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                            $"replan {decision.ReplansUsed}: {noPath}"));
                        events.Add(Event(sessionId, ReplanEventType, atUtc, ReplanPayload(
                            named, noPath, decision.ReplansUsed, cells: 0, adopted: false)));
                        return Finish(ExitNoPath, noPath, controller.ReplansAdopted);
                    }

                    if (!controller.TryAdoptReplan(in grid, replacement, at, out string? replanRefusal))
                    {
                        text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                            $"replan {decision.ReplansUsed}: {replanRefusal}"));
                        events.Add(Event(sessionId, ReplanEventType, atUtc, ReplanPayload(
                            named, replanRefusal ?? decision.Reason, decision.ReplansUsed,
                            replacement.Count, adopted: false)));
                        return Finish(ExitAbandoned, replanRefusal, controller.ReplansAdopted);
                    }

                    text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                        $"replan {decision.ReplansUsed}: {decision.Reason}"));
                    text.AppendLine(FormatPath(controller.Path));
                    events.Add(Event(sessionId, ReplanEventType, atUtc, ReplanPayload(
                        named, decision.Reason, decision.ReplansUsed, controller.Path.Count, adopted: true)));
                    continue;
                }

                case WalkOutcome.Stepping:
                {
                    MapPoint to = decision.StepTo!.Value;
                    var request = new StepRequest(at, to, grid, view, atUtc);
                    StepReport report = executor.Step(in request, in authority, readPosition);
                    text.Append(SingleStepCommand.Format(in request, report));
                    events.AddRange(SingleStepCommand.Audit(
                        in request, report, in authority, sessionId, atUtc));
                    MovementVerification verification = report.Verification;
                    controller.NoteStepOutcome(in verification);

                    if (report.Emitted)
                    {
                        stepsEmitted++;
                        if (verification.Outcome == MovementOutcome.Succeeded)
                            stepsSucceeded++;
                        else if (verification.Outcome == MovementOutcome.Stalled)
                            stepsStalled++;
                    }
                    else
                    {
                        return Finish(
                            ExitGuardRefused,
                            report.EmissionRefusal ?? report.Authorization.RefusalReason,
                            controller.ReplansAdopted);
                    }

                    if (verification.Observed is { } observed)
                        at = observed;
                    else if (verification.Outcome == MovementOutcome.Succeeded)
                        at = to;

                    continue;
                }

                default:
                    return Finish(
                        ExitAbandoned,
                        decision.Reason ?? decision.Outcome.ToString(),
                        controller.ReplansAdopted);
            }
        }
    }

    /// <summary>
    /// The dry-run block for one segment: the same ladder <c>--step</c> prints,
    /// and a named non-emission rather than a verifier line.
    /// </summary>
    public static string FormatPreview(in StepRequest request, StepAuthorization authorization)
    {
        string refusal = authorization.IsAuthorized
            ? DryRunNotEmittedReason
            : authorization.RefusalReason ?? DryRunNotEmittedReason;

        var report = new StepReport(
            authorization,
            false,
            refusal,
            MovementVerification.NotAttempted(refusal),
            null);

        return SingleStepCommand.Format(in request, report);
    }

    [SupportedOSPlatform("windows")]
    private static int RunWindows(int gx, int gy, bool dryRun)
    {
        RuntimeComponents components = RuntimeComposition.CreateSafe();
        if (components.InputBackend is not GatedInputBackend gated)
        {
            Console.WriteLine($"[REFUSED] {UngatedBackendReason}");
            return ExitAbandoned;
        }

        if (!TryFindWindow(out ClientWindow window, out int processId, out string? windowFailure))
        {
            Console.WriteLine($"[REFUSED] {windowFailure}");
            return ExitAbandoned;
        }

        if (!ClientMemorySession.TryAttach(out ClientMemorySession? session, out string? attachFailure, processId))
        {
            Console.WriteLine($"[REFUSED] {attachFailure}");
            return ExitAbandoned;
        }

        using (session)
        {
            if (!session!.TryReadPlayer(out PlayerObjectReading player, out string? readFailure))
            {
                Console.WriteLine($"[REFUSED] {readFailure}");
                return ExitAbandoned;
            }

            if (!session.TryReadMapId(out int mapId, out string? mapFailure))
            {
                Console.WriteLine($"[REFUSED] {mapFailure}");
                return ExitAbandoned;
            }

            MapGrid grid = default;
            if (MapGridExtractor.TryResolveDedicatedMapsDirectory(out string mapsDirectory, out string? volumeReason))
            {
                if (!MapGridExtractor.TryInfo(mapsDirectory, mapId, out grid, out _, out string? gridReason))
                    Console.WriteLine($"[WARN] {gridReason}");
            }
            else
            {
                Console.WriteLine($"[WARN] {volumeReason}");
            }

            components.SessionAuthority?.BeginSession(window.Handle, processId);

            if (components.HumanInput is HumanInputMonitor monitor
                && !monitor.TryStart(out string? watchFailure))
            {
                Console.WriteLine($"[WARN] human monitor: {watchFailure}");
            }

            string repo = TestSuiteRunner.FindRepositoryRoot(Environment.CurrentDirectory)
                          ?? TestSuiteRunner.FindRepositoryRoot()
                          ?? Directory.GetCurrentDirectory();
            ScreenProjectionCalibration calibration = ScreenProjectionCalibration.Load(
                Path.Combine(repo, ScreenProjectionCalibration.RelativePath), out _);

            ClientMemorySession attached = session;
            var projection = new CalibratedScreenProjection(
                calibration,
                () => ClientWindowLocator.TryFind(processId, out _)?.ClientArea ?? window.ClientArea,
                () => attached.TryReadPlayer(out PlayerObjectReading current, out string? why)
                    ? ClassifiedValue<MapPoint>.Live(new MapPoint(current.X, current.Y))
                    : ClassifiedValue<MapPoint>.Unknown(why ?? "player_unreadable"),
                clientDpi: () => GeometryStamp.Take(window.Handle, TimeProvider.System).Epoch.Dpi);

            var chain = new StepGuardChain(
                () => components.SessionAuthority?.CurrentRefusal() ?? SessionActuationAuthority.NoSessionReason,
                () => components.Safety.Policy,
                projection);

            var executor = new SingleStepExecutor(chain, gated, () => window.Handle);
            var origin = new MapPoint(player.X, player.Y);
            var destination = new MapPoint(gx, gy);
            DateTime now = TimeProvider.System.GetUtcNow().UtcDateTime;
            // Nothing has looked through the occupancy feed in this process. An empty
            // list would claim it had looked and seen nothing; null is that it has
            // not looked, which OccupancyFreshness already refuses by name.
            var view = new OccupancyView(null, now);
            ActuationAuthority authority = ActuationAuthority.Commanded(Flag);
            var controller = new PathWalkController();

            WalkRun run = Execute(
                destination,
                origin,
                in grid,
                view,
                controller,
                chain,
                executor,
                in authority,
                () => attached.TryReadPlayer(out PlayerObjectReading current, out _)
                    ? new PositionReading(
                        new MapPoint(current.X, current.Y),
                        TimeProvider.System.GetUtcNow().UtcDateTime,
                        DataSourceKind.Live)
                    : null,
                dryRun);

            Console.Write(run.Text);

            try
            {
                SingleStepCommand.Persist(run.Events);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] audit_not_persisted:{ex.GetType().Name}");
            }

            return run.ExitCode;
        }
    }

    /// <summary>
    /// A route on static geometry only. Occupancy is a reason to re-route, not a
    /// reason to stamp the planner: that would be composing a routing policy the
    /// controller already owns.
    /// </summary>
    private static IReadOnlyList<MapPoint> Plan(
        in MapGrid grid,
        MapPoint from,
        MapPoint to,
        out CalculatedPathResult result)
    {
        var data = MapGridData.CreateFullyWalkable(grid.MapId, "walk", grid.Width, grid.Height);
        StaticGeometryLayer.Project(in grid, data);
        result = new AStarPathfinder().FindPath(
            data, new GridPoint(from.X, from.Y), new GridPoint(to.X, to.Y));
        return result.IsPathFound
            ? PathRevalidation.ToCells(result.Waypoints)
            : Array.Empty<MapPoint>();
    }

    private static string FormatPath(IReadOnlyList<MapPoint> path)
    {
        var text = new StringBuilder("path:");
        foreach (MapPoint cell in path)
            text.Append(CultureInfo.InvariantCulture, $" ({cell.X},{cell.Y})");
        return text.ToString();
    }

    private static void AppendSummary(
        StringBuilder text,
        long stepsEmitted,
        long stepsSucceeded,
        long stepsStalled,
        long replansExecuted,
        string? stoppedBecause)
    {
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"steps-emitted: {stepsEmitted}"));
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"steps-succeeded: {stepsSucceeded}"));
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"steps-stalled: {stepsStalled}"));
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"replans: {replansExecuted}"));
        text.AppendLine($"stopped: {stoppedBecause}");
    }

    private static string NamedAuthority(in ActuationAuthority authority)
    {
        string named = authority.Describe();
        return string.IsNullOrEmpty(named) ? "none" : named;
    }

    private static RuntimeEvent Event(string sessionId, string type, DateTime at, string payload) =>
        new(Guid.NewGuid(), sessionId, 0, at, SourceModule, type, EventPriority.NormalAudit, payload);

    private static string AdmissionPayload(
        string authority,
        in MapGrid grid,
        MapPoint origin,
        MapPoint destination,
        bool admitted,
        int cells,
        string? refusalReason) =>
        JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AuthorityField] = authority,
            ["mapId"] = grid.MapId.ToString(CultureInfo.InvariantCulture),
            ["originX"] = origin.X.ToString(CultureInfo.InvariantCulture),
            ["originY"] = origin.Y.ToString(CultureInfo.InvariantCulture),
            ["destX"] = destination.X.ToString(CultureInfo.InvariantCulture),
            ["destY"] = destination.Y.ToString(CultureInfo.InvariantCulture),
            ["admitted"] = admitted ? "true" : "false",
            ["cells"] = cells.ToString(CultureInfo.InvariantCulture),
            ["refusalReason"] = refusalReason ?? string.Empty
        });

    private static string ReplanPayload(
        string authority,
        string? reason,
        int replansUsed,
        int cells,
        bool adopted) =>
        JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AuthorityField] = authority,
            ["reason"] = reason ?? string.Empty,
            ["replansUsed"] = replansUsed.ToString(CultureInfo.InvariantCulture),
            ["cells"] = cells.ToString(CultureInfo.InvariantCulture),
            ["adopted"] = adopted ? "true" : "false"
        });

    private static string FinishedPayload(
        string authority,
        long stepsEmitted,
        long stepsSucceeded,
        long stepsStalled,
        long replansExecuted,
        string? stoppedBecause,
        bool arrived) =>
        JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AuthorityField] = authority,
            ["stepsEmitted"] = stepsEmitted.ToString(CultureInfo.InvariantCulture),
            ["stepsSucceeded"] = stepsSucceeded.ToString(CultureInfo.InvariantCulture),
            ["stepsStalled"] = stepsStalled.ToString(CultureInfo.InvariantCulture),
            ["replans"] = replansExecuted.ToString(CultureInfo.InvariantCulture),
            ["stoppedBecause"] = stoppedBecause ?? string.Empty,
            ["arrived"] = arrived ? "true" : "false"
        });

    [SupportedOSPlatform("windows")]
    private static bool TryFindWindow(out ClientWindow window, out int processId, out string? failureReason)
    {
        processId = 0;
        foreach (string name in RealClientConnector.DefaultProcessNames)
        {
            foreach (System.Diagnostics.Process process in System.Diagnostics.Process.GetProcessesByName(name))
            {
                using (process)
                {
                    ClientWindow? found = ClientWindowLocator.TryFind(process.Id, out string? why);
                    if (found is not null)
                    {
                        window = found;
                        processId = process.Id;
                        failureReason = null;
                        return true;
                    }

                    failureReason = why;
                }
            }
        }

        window = null!;
        failureReason = $"{InputGuardsProbe.WindowNotLocatedReason}:{string.Join('/', RealClientConnector.DefaultProcessNames)}";
        return false;
    }
}
