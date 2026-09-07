using System.Globalization;
using System.Runtime.Versioning;
using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Quests;
using NosAi.LiveIntegration;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate1;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Navigation;
using NosAi.Runtime.Orchestration;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Perception.Network;
using NosAi.Runtime.Testing;
using NosAi.Runtime.WorldModel.Fusion;

namespace NosAi.Runtime.Navigation;

/// <summary>
/// The operator command that verifies one <c>Collect</c> quest-objective
/// attempt at one operator-named position (AP-06 "Quest Intelligence",
/// OCR/UI/**network** evidence): it walks to <c>&lt;x&gt; &lt;y&gt;</c>,
/// reads the player's own inventory count of <c>&lt;vnum&gt;</c> before and
/// after (a real, wire-confirmed count via <c>ivn</c> -- not OCR, not a
/// guess), and reports whether the count increased.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this buys, and what it deliberately does not.</b> It confirms
/// "the player's own count of this item increased after walking to this
/// position". It does <b>not</b> discover ground items automatically
/// (<see cref="GameplayObservation.GroundItems"/> is not enumerated to find
/// the nearest matching item -- a real, buildable future extension), does
/// not pick the best item to fetch, retry on failure, or handle any quest
/// semantics beyond the one objective the operator names directly.
/// </para>
/// <para>
/// <b>Which objective kinds this covers.</b> Only
/// <see cref="QuestObjectiveKind.Collect"/>. <c>Travel</c> needs no new code
/// (<see cref="WalkCommand.Execute"/> already does it);
/// <c>Kill</c>/<c>Dialogue</c>/<c>Interact</c>/<c>Deliver</c> stay blocked
/// (no entity classification for Kill, no interaction primitive or
/// verification channel for the rest). The collect slice is the one with a
/// real, already-decoded network path independent of the OCR/ML gap.
/// </para>
/// <para>
/// <b>Authority.</b> The walk is a commanded act like every other operator
/// command (<see cref="ActuationAuthority.Commanded"/>(<see cref="Flag"/>),
/// ADR-0020) and passes through the same <see cref="StepGuardChain"/> and
/// gated executor <c>--walk</c>/<c>--scout</c> use. Nothing here arms input.
/// </para>
/// </remarks>
public static class CollectCommand
{
    /// <summary>The flag, and the name recorded as the commanded authority.</summary>
    public const string Flag = "--collect";

    /// <summary>Where the audit events are attributed.</summary>
    public const string SourceModule = "Navigation";

    /// <summary>Session id of an operator collect round, not a Gate cycle.</summary>
    public const string OperatorSessionId = "operator-collect";

    /// <summary>Reported off Windows, where there is no session window to bind.</summary>
    public const string NotWindowsReason = "collect_requires_windows";

    /// <summary>Reported when the composed backend is not the gated one.</summary>
    public const string UngatedBackendReason = "collect_input_backend_not_gated";

    /// <summary>Reported when the live gameplay provider cannot be constructed.</summary>
    public const string GameplayUnavailableReason = "collect_gameplay_provider_unavailable";

    /// <summary>
    /// Reported when <c>vnum</c> is present but blank, or <c>rounds</c> is
    /// less than one. <see cref="Program"/>'s dispatch only checks argument
    /// *count*, not content, so a caller that resolves a vnum to an empty
    /// string must get a clean refusal here, not an unhandled exception --
    /// the same [REFUSED] boundary every other guard in this command already
    /// gives (same fix as AP-05/A6's <c>EngageCommand.Run</c>).
    /// </summary>
    public const string InvalidArgumentsReason = "collect_requires_non_blank_vnum_and_positive_rounds";

    /// <summary>
    /// Walks to <paramref name="destination"/> and reports whether the
    /// player's observed count of <paramref name="item"/> increased.
    /// </summary>
    /// <remarks>
    /// Pure with respect to I/O and the clock: the before/after observations
    /// are already-captured <see cref="GameplayObservation"/> values and the
    /// walk is driven entirely through the injected rig, exactly like
    /// <see cref="ScoutCommand.ExecuteOneRound"/>. The returned before/after
    /// counts are raw <see cref="WorldFact{T}"/> readings -- interpreting them
    /// ("increased", "unchanged", "still unknown") is the caller's job.
    /// </remarks>
    /// <param name="destination">The map cell to walk to.</param>
    /// <param name="origin">The map cell the walk starts from (the player's tile).</param>
    /// <param name="item">The item whose count the objective tracks (the operator's vnum as an <see cref="ItemId"/>).</param>
    /// <param name="grid">The client's static geometry grid for this map.</param>
    /// <param name="view">Occupancy view for the walk (fresh, stamped, empty when nothing moving has been observed).</param>
    /// <param name="controller">The walk controller the round drives (fresh per round).</param>
    /// <param name="chain">The same guard ladder the executor holds (fresh per round).</param>
    /// <param name="executor">Emits steps through the gated backend only (fresh per round).</param>
    /// <param name="authority">The authority of the walk: <see cref="ActuationAuthority.Commanded"/>(<see cref="Flag"/>).</param>
    /// <param name="readPosition">Re-reads the observed grid position while the verifier's window is open.</param>
    /// <param name="before">The live observation captured immediately before the walk starts.</param>
    /// <param name="after">The live observation captured immediately after the walk finishes (whatever its outcome).</param>
    /// <param name="playerId">The controlled character's entity id, used by the projection.</param>
    /// <param name="nowUtc">The instant this round runs at, stamped on every fact it creates.</param>
    /// <returns>The walk result plus the raw before/after item counts.</returns>
    public static (WalkRun Walk, WorldFact<int> Before, WorldFact<int> After) ExecuteOneRound(
        MapPoint destination,
        MapPoint origin,
        ItemId item,
        in MapGrid grid,
        OccupancyView view,
        PathWalkController controller,
        StepGuardChain chain,
        SingleStepExecutor executor,
        in ActuationAuthority authority,
        Func<PositionReading?> readPosition,
        GameplayObservation before,
        GameplayObservation after,
        EntityId playerId,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(readPosition);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var target = new QuestObjectiveTarget(QuestObjectiveKind.Collect, item: item);

        // version: 0 -- the projection's version parameter serves the World
        // Model's own replay/versioning concern, which this one-shot command
        // does not participate in; a fixed 0 is honest and documented as such.
        WorldModelSnapshot beforeSnapshot = GameplayObservationProjector.Project(before, playerId, version: 0, nowUtc);
        WorldFact<int> beforeCount = QuestGraphPlanner.AssessCollectProgress(target, beforeSnapshot.Player.Inventory, nowUtc);

        WalkRun run = WalkCommand.Execute(
            destination,
            origin,
            in grid,
            view,
            controller,
            chain,
            executor,
            in authority,
            readPosition,
            dryRun: false,
            sessionId: OperatorSessionId,
            timestampUtc: nowUtc);

        WorldModelSnapshot afterSnapshot = GameplayObservationProjector.Project(after, playerId, version: 0, nowUtc);
        WorldFact<int> afterCount = QuestGraphPlanner.AssessCollectProgress(target, afterSnapshot.Player.Inventory, nowUtc);

        return (run, beforeCount, afterCount);
    }

    /// <summary>
    /// Console entry for <c>--collect &lt;x&gt; &lt;y&gt; &lt;vnum&gt;
    /// [&lt;requiredCount&gt;]</c>, optionally <c>--watch &lt;n&gt;</c> rounds.
    /// </summary>
    public static int Run(int x, int y, string vnum, int? requiredCount, int rounds = 1)
    {
        if (string.IsNullOrWhiteSpace(vnum) || rounds < 1)
        {
            Console.WriteLine($"[REFUSED] {InvalidArgumentsReason}");
            return WalkCommand.ExitAbandoned;
        }

        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine($"[REFUSED] {NotWindowsReason}");
            return WalkCommand.ExitAbandoned;
        }

        return RunWindows(x, y, vnum, requiredCount, rounds);
    }

    /// <summary>
    /// The live composition: attach to the running client for the walk
    /// (position/map/grid/projection, the same shape as
    /// <see cref="WalkCommand"/>'s own <c>RunWindows</c>), and build the live
    /// <see cref="IGameplayProvider"/> the same way
    /// <see cref="Gate1ObservationChannel.FromPackets"/> does -- a real
    /// WinDivert capture of the client's own game connection, reassembled,
    /// decoded and published by <see cref="NetworkGameplayProvider"/>.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static int RunWindows(int x, int y, string vnum, int? requiredCount, int rounds)
    {
        RuntimeComponents components = RuntimeComposition.CreateSafe();
        if (components.InputBackend is not GatedInputBackend gated)
        {
            Console.WriteLine($"[REFUSED] {UngatedBackendReason}");
            return WalkCommand.ExitAbandoned;
        }

        if (!TryFindWindow(out ClientWindow window, out int processId, out string? windowFailure))
        {
            Console.WriteLine($"[REFUSED] {windowFailure}");
            return WalkCommand.ExitAbandoned;
        }

        if (!ClientMemorySession.TryAttach(out ClientMemorySession? session, out string? attachFailure, processId))
        {
            Console.WriteLine($"[REFUSED] {attachFailure}");
            return WalkCommand.ExitAbandoned;
        }

        using (session)
        {
            LiveObservationScope? live = LiveObservationScope.TryOpen(processId, out string? gatewayFailure);
            if (live is null)
            {
                Console.WriteLine($"[REFUSED] {GameplayUnavailableReason}:{gatewayFailure}");
                return WalkCommand.ExitAbandoned;
            }

            using (live)
            {
                return RunWindowsCore(x, y, vnum, requiredCount, rounds, components, gated, window, processId, session!, live.Gateway);
            }
        }
    }

    /// <summary>The walk/gateway rounds, once every live resource is open.</summary>
    [SupportedOSPlatform("windows")]
    private static int RunWindowsCore(
        int x,
        int y,
        string vnum,
        int? requiredCount,
        int rounds,
        RuntimeComponents components,
        GatedInputBackend gated,
        ClientWindow window,
        int processId,
        ClientMemorySession session,
        LiveObservationGateway gateway)
    {
        if (!session.TryReadPlayer(out PlayerObjectReading player, out string? readFailure))
        {
            Console.WriteLine($"[REFUSED] {readFailure}");
            return WalkCommand.ExitAbandoned;
        }

        if (!session.TryReadMapId(out int mapId, out string? mapFailure))
        {
            Console.WriteLine($"[REFUSED] {mapFailure}");
            return WalkCommand.ExitAbandoned;
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

        var projection = new CalibratedScreenProjection(
            calibration,
            () => ClientWindowLocator.TryFind(processId, out _)?.ClientArea ?? window.ClientArea,
            () => session.TryReadPlayer(out PlayerObjectReading current, out string? why)
                ? ClassifiedValue<MapPoint>.Live(new MapPoint(current.X, current.Y))
                : ClassifiedValue<MapPoint>.Unknown(why ?? "player_unreadable"),
            clientDpi: () => GeometryStamp.Take(window.Handle, TimeProvider.System).Epoch.Dpi);

        var chain = new StepGuardChain(
            () => components.SessionAuthority?.CurrentRefusal() ?? SessionActuationAuthority.NoSessionReason,
            () => components.Safety.Policy,
            projection);

        var executor = new SingleStepExecutor(chain, gated, () => window.Handle);
        var origin = new MapPoint(player.X, player.Y);
        var destination = new MapPoint(x, y);
        var item = new ItemId(vnum);
        // The World Model's player id is the session's character id; on a live
        // client the wire's own id is the ground truth, but this command does not
        // hold a wire player id (the capture feed is only polled for inventory).
        // A stable per-process id is used so the projection has a valid id to key
        // on without claiming to know the character id.
        var playerId = new EntityId(string.Create(CultureInfo.InvariantCulture, $"player-{processId}"));
        ActuationAuthority authority = ActuationAuthority.Commanded(Flag);

        for (int round = 1; round <= rounds; round++)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"=== collect round {round} of {rounds}: item {vnum} at {x},{y} ==="));

            DateTime now = TimeProvider.System.GetUtcNow().UtcDateTime;
            // Nothing has looked through the occupancy feed in this process. An
            // empty list would claim it had looked and seen nothing; null is that
            // it has not looked, which OccupancyFreshness already refuses by name.
            var view = new OccupancyView(null, now);
            var controller = new PathWalkController();

            GameplayObservation before = gateway.Capture().Gameplay;
            (WalkRun run, WorldFact<int> beforeCount, WorldFact<int> afterCount) = ExecuteOneRound(
                destination,
                origin,
                item,
                in grid,
                view,
                controller,
                chain,
                executor,
                in authority,
                () => session.TryReadPlayer(out PlayerObjectReading current, out _)
                    ? new PositionReading(
                        new MapPoint(current.X, current.Y),
                        TimeProvider.System.GetUtcNow().UtcDateTime,
                        NosAi.Runtime.Contracts.DataSourceKind.Live)
                    : null,
                before,
                after: gateway.Capture().Gameplay,
                playerId,
                now);

            Console.Write(run.Text);
            PrintVerdict(item, beforeCount, afterCount, requiredCount);
            Console.WriteLine();
        }

        return WalkCommand.ExitArrived;
    }

    /// <summary>One <c>collect</c> verdict line: the before/after counts and what they say.</summary>
    private static void PrintVerdict(ItemId item, WorldFact<int> before, WorldFact<int> after, int? requiredCount)
    {
        string itemValue = item.Value;
        if (before.HasValue && after.HasValue)
        {
            string direction = after.Value > before.Value ? "COLLECTED" : "NO_CHANGE_OBSERVED";
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"collect-evidence: {direction} item={itemValue} before={before.Value} after={after.Value}"));

            if (requiredCount is { } required && after.Value >= required)
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"OBJECTIVE_SATISFIED: item={itemValue} after={after.Value} >= required={required}"));
        }
        else
        {
            string reason = !before.HasValue
                ? before.Reason ?? "before_count_unknown"
                : after.Reason ?? "after_count_unknown";
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"collect-evidence: UNOBSERVED item={itemValue} reason={reason}"));
        }
    }

    /// <summary>
    /// Finds the running client's window and process id, or names why it could
    /// not.
    /// </summary>
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
