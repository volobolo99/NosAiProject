using System.Runtime.Versioning;
using NosAi.Core.Memory;
using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Exploration;
using NosAi.Core.WorldModel.Reconstruction;
using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.GameData;
using NosAi.Runtime.Gate2;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Orchestration;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Safety;
using NosAi.Runtime.Testing;
using NosAi.Runtime.WorldModel.Fusion;
using NosAi.Storage;

// Aliased rather than importing NosAi.Runtime.Autonomy wholesale: that
// namespace also declares a Goal, which would collide with
// NosAi.Core.WorldModel.Goal in this file.
using CatalogueClassifier = NosAi.Runtime.Autonomy.CatalogueClassifier;

namespace NosAi.Runtime.Navigation;

/// <summary>
/// The operator command that scouts one frontier of the current map
/// (AP-04 "Exploration"): it reads the real World Model
/// (<see cref="MapReconstructionSource"/>, AP-03) plus the player's live
/// position, asks <see cref="ExplorationPlanner"/> (AP-04/A3) which unvisited
/// walkable region is worth walking to, and walks there by calling
/// <see cref="WalkCommand.Execute"/> <b>unchanged</b> -- the exact per-cell walk,
/// guard, verify and replan loop <c>--walk</c> already runs.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this shape and not a Gate3Runtime bridge.</b>
/// <see cref="docs/agents/phases/AP-04/AP-04_A2A4_DEEPSEEK_scout_command.md"/>:
/// wiring AP-04's <see cref="NavigationPlan"/> into <c>Gate3Runtime</c>'s
/// candidate/Guard/Trust/Safety pipeline is not an additive task today (closed
/// candidate list, placeholder movement prediction, click-teleport effector) --
/// that is AP-08's work. What is real and reusable today is
/// <see cref="WalkCommand.Execute"/>, and the legitimate authority for this
/// automation is exactly the one <c>--walk</c>/<c>--screen-autocalibrate</c> use:
/// a person typed a named command, so the multi-step routine that follows is
/// <see cref="ActuationAuthority.Commanded"/> -- ADR-0020's commanded family,
/// zero bypass, zero duplication of the walk logic.
/// </para>
/// <para>
/// <b>Naming.</b> The command is <c>--scout</c>, deliberately not <c>--explore</c>:
/// it is an operator-facing verb built on the <c>Exploration</c> World Model
/// contracts (<see cref="ExplorationFootprint"/>, <see cref="ExplorationPlanner"/>),
/// not a new domain concept of its own.
/// </para>
/// <para>
/// <b>What the risk term can and cannot see.</b> A live mob feed <i>is</i>
/// wired in now (AP-05, Q-099): each round captures the client's own game
/// connection and projects the entities it reports into real
/// <see cref="Mob"/> records for
/// <see cref="ExplorationPlanner.BuildFrontierCandidates"/>. Three honest
/// limits remain, and each leaves <see cref="FrontierCandidate.Risk"/> at
/// exactly <c>0</c> rather than inventing a number. The capture backend needs
/// Administrator: without it the round runs risk-blind and says so. Only a
/// vnum the reference catalogue establishes as a monster becomes a
/// <see cref="Mob"/> at all. And risk counts only mobs known hostile, which
/// today means only one the wire recorded hitting this character -- a mob
/// merely standing there contributes nothing, because nothing has established
/// that it would fight.
/// </para>
/// </remarks>
public static class ScoutCommand
{
    /// <summary>The flag, and the name recorded as the commanded authority.</summary>
    public const string Flag = "--scout";

    /// <summary>Where the audit events are attributed.</summary>
    public const string SourceModule = "Navigation";

    /// <summary>Session id of an operator scout round, not a Gate 2 cycle.</summary>
    public const string OperatorSessionId = "operator-scout";

    /// <summary>Nothing left to scout this cycle: FullyExplored, or no walkable tile has ever been reconstructed.</summary>
    public const int ExitNothingToScout = 6;

    /// <summary>Reported off Windows, where there is no session window to bind.</summary>
    public const string NotWindowsReason = "scout_requires_windows";

    /// <summary>Reported when the composed backend is not the gated one.</summary>
    public const string UngatedBackendReason = "scout_input_backend_not_gated";

    /// <summary>
    /// One round of scouting, computed and walked without any I/O of its own.
    /// </summary>
    /// <remarks>
    /// Everything I/O-shaped is a parameter, exactly like
    /// <see cref="WalkCommand.Execute"/>: this is what makes the round unit-testable
    /// with the <c>WalkCommandTests</c> rig. It never touches the network, client
    /// memory or an input backend itself.
    /// </remarks>
    /// <param name="map">This cycle's reconstructed map (AP-03), for the same map as <paramref name="footprint"/>.</param>
    /// <param name="footprint">The footprint carried from the previous round on this map, or <see cref="ExplorationFootprint.Empty"/> to start one.</param>
    /// <param name="playerPosition">The player's live position this round.</param>
    /// <param name="mobs">
    /// This cycle's known mobs; may legitimately be empty when no live mob feed is
    /// available -- every candidate's <see cref="FrontierCandidate.Risk"/> is then
    /// zero (see the class remarks; no mob position is ever fabricated).
    /// </param>
    /// <param name="origin">The map cell the walk starts from (the player's tile).</param>
    /// <param name="grid">The client's static geometry grid for this map, used by the guard chain and the path planner.</param>
    /// <param name="view">Occupancy view for the walk (fresh, stamped, empty when nothing moving has been observed).</param>
    /// <param name="controller">The walk controller the round drives (fresh per round).</param>
    /// <param name="chain">The same guard ladder the executor holds (fresh per round).</param>
    /// <param name="executor">Emits steps through the gated backend only (fresh per round).</param>
    /// <param name="authority">The authority of the walk: <see cref="ActuationAuthority.Commanded"/>(<see cref="Flag"/>).</param>
    /// <param name="readPosition">Re-reads the observed grid position while the verifier's window is open.</param>
    /// <param name="onEvidence">
    /// Called once per emitted step, in order, with the canonical evidence projected
    /// through <see cref="MovementVerificationProjector"/>. Never called for a step
    /// that was never emitted (a guard refusal ends the round before any step).
    /// </param>
    /// <param name="updatedFootprint">
    /// The footprint this round folded the player's position into, ready for the
    /// next round on the same map.
    /// </param>
    /// <param name="plan">The plan this round built and walked (or refused), for the caller's report.</param>
    /// <param name="nowUtc">The instant this round runs at, stamped on every fact it creates.</param>
    /// <returns>
    /// The result of walking toward the chosen frontier, or <see langword="null"/>
    /// when <paramref name="plan"/> (out) has no reachable waypoint -- the caller
    /// must check <paramref name="plan"/> in that case, not assume a "null run"
    /// means failure.
    /// </returns>
    public static WalkRun? ExecuteOneRound(
        MapModel map,
        ExplorationFootprint footprint,
        WorldPosition playerPosition,
        EquatableArray<Mob> mobs,
        MapPoint origin,
        in MapGrid grid,
        OccupancyView view,
        PathWalkController controller,
        StepGuardChain chain,
        SingleStepExecutor executor,
        in ActuationAuthority authority,
        Func<PositionReading?> readPosition,
        Action<MovementExecutionEvidence>? onEvidence,
        out ExplorationFootprint updatedFootprint,
        out NavigationPlan plan,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(footprint);
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(readPosition);

        updatedFootprint = ExplorationPlanner.UpdateFootprint(
            footprint, map, WorldFact<WorldPosition>.Live(playerPosition, confidence: 1d, nowUtc), nowUtc);

        IReadOnlyList<FrontierCandidate> candidates = ExplorationPlanner.BuildFrontierCandidates(
            map, updatedFootprint, playerPosition, mobs);
        FrontierCandidate? selected = ExplorationPlanner.SelectNextFrontier(candidates);
        plan = ExplorationPlanner.BuildNavigationPlan(map.Id, selected, nowUtc);

        if (!plan.IsReachable.HasValue || !plan.IsReachable.Value || plan.Waypoints.Count == 0)
            return null;

        NavigationWaypoint waypoint = plan.Waypoints[0];
        // ExplorationPlanner.BuildNavigationPlan always constructs the waypoint from
        // a TileCoordinate's own integer Column/Row (no fractional part exists), so
        // truncation is an exact statement of that equivalence, never a rounding.
        var destination = new MapPoint((int)waypoint.Position.X, (int)waypoint.Position.Y);

        void OnStepVerified(MapPoint requested, MovementVerification verification)
        {
            if (onEvidence is null) return;
            MovementExecutionEvidence evidence = MovementVerificationProjector.Project(
                map.Id, requested, in verification, nowUtc);
            onEvidence(evidence);
        }

        return WalkCommand.Execute(
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
            timestampUtc: nowUtc,
            onStepVerified: OnStepVerified);
    }

    /// <summary>Console entry for <c>--scout</c>, optionally <c>--watch &lt;n&gt;</c> rounds.</summary>
    public static int Run(int rounds = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rounds, 1);

        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine($"[REFUSED] {NotWindowsReason}");
            return WalkCommand.ExitAbandoned;
        }

        return RunWindows(rounds);
    }

    /// <summary>
    /// This round's mobs as the canonical World Model sees them, or an empty
    /// list when there is no entity feed to see them with.
    /// </summary>
    /// <remarks>
    /// Empty here means "nothing was observed", never "nothing is there" --
    /// the same distinction <see cref="ExplorationPlanner.BuildFrontierCandidates"/>
    /// draws for a mob whose hostility is Unknown. A round that cannot observe
    /// runs risk-blind, exactly as every round did before this feed existed.
    /// The projection is per round and is deliberately not run through
    /// <c>WorldModelTemporalEnricher</c>: this command keeps no previous
    /// snapshot, so a mob's velocity stays Unknown here and its hostility rests
    /// on <c>GameplayObservation.HitBy</c> being sticky across polls.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    private static EquatableArray<Mob> ObserveMobs(
        LiveObservationScope? feed,
        CatalogueClassifier classifier,
        EntityId playerId,
        long round,
        DateTime nowUtc)
    {
        if (feed is null)
            return EquatableArray<Mob>.Empty;

        GameplayObservation observation = feed.Gateway.Capture().Gameplay;
        return GameplayObservationProjector
            .Project(observation, playerId, round, nowUtc, classifier.Classify)
            .Mobs;
    }

    /// <summary>
    /// The live composition. Mirror of <see cref="WalkCommand"/>'s own
    /// <c>RunWindows</c> (same <see cref="RuntimeComposition.CreateSafe"/>,
    /// window lookup, <see cref="ClientMemorySession.TryAttach"/>,
    /// <see cref="StepGuardChain"/>, <see cref="SingleStepExecutor"/>,
    /// <see cref="GatedInputBackend"/>, projection construction): a thin,
    /// untested-by-design shell, exactly like <c>WalkCommand.RunWindows</c>, which
    /// has no unit test either -- only <see cref="ExecuteOneRound"/> (and, through
    /// it, <see cref="WalkCommand.Execute"/>) does.
    /// </summary>
    /// <param name="rounds">How many frontier walks to attempt in this one process invocation.</param>
    [SupportedOSPlatform("windows")]
    private static int RunWindows(int rounds)
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

            ClientMemorySession attached = session!;
            var projection = new CalibratedScreenProjection(
                calibration,
                () => ClientWindowLocator.TryFind(processId, out _)?.ClientArea ?? window.ClientArea,
                () => attached.TryReadPlayer(out PlayerObjectReading current, out string? why)
                    ? ClassifiedValue<MapPoint>.Live(new MapPoint(current.X, current.Y))
                    : ClassifiedValue<MapPoint>.Unknown(why ?? "player_unreadable"),
                clientDpi: () => GeometryStamp.Take(window.Handle, TimeProvider.System).Epoch.Dpi);

            // One MapReconstructionSource for the whole invocation, reused across
            // rounds: it caches per map id, so repeated rounds on the same map never
            // re-touch the grid file or the store. Disposed on the way out.
            using var mapReconstruction = new MapReconstructionSource(logger: null);

            // Session-local memory of visited tiles, keyed to the current map id. If a
            // round observes a different map than the footprint was started on, the
            // footprint is reset for the new map rather than silently reusing another
            // map's visited history.
            ExplorationFootprint footprint = ExplorationFootprint.Empty(
                new MapId("unknown-map"), "scout_command_session_start");

            // Tracks the previous round's (map, position) reading so a map
            // change between two consecutive rounds can be recognised as a
            // portal crossing (PortalCrossingDetector) -- null before the
            // first round runs. Independent of `footprint`: this is about
            // recording a fact for the map just left, not the one just
            // entered.
            MapPositionReading? previousReading = null;

            // Ledger persistence is opportunistic history, never a gate: a
            // missing NOSAI-SSD volume warns and records nothing -- it must
            // never become a reason this command refuses.
            using ActionOutcomeLedgerStore? ledgerStore =
                ActionOutcomeLedgerStore.TryOpenFromVolume(new SqliteJournalOptions(), out string? ledgerFailure);
            if (ledgerStore is null)
                Console.WriteLine($"[WARN] action_outcome_ledger_unavailable:{ledgerFailure}");

            // AP-05: the entity feed the frontier ranker's risk term reads.
            // Opportunistic in exactly the sense the ledger above is: the
            // capture backend needs Administrator, and when it will not open
            // this warns and leaves the mob list empty -- which is precisely
            // how this command behaved before the feed existed. It must never
            // become a reason to refuse.
            using LiveObservationScope? entityFeed =
                LiveObservationScope.TryOpen(processId, out string? entityFeedFailure);
            if (entityFeed is null)
                Console.WriteLine($"[WARN] entity_feed_unavailable:{entityFeedFailure} -- il ranker delle frontiere resta cieco al rischio");

            // Without the catalogue nothing is established as a monster and the
            // projection yields no mobs, so opening it is only worth attempting
            // when the feed itself opened.
            GameReferenceDatabase? entityCatalogue = null;
            if (entityFeed is not null && !GameReferenceLocator.TryOpen(out entityCatalogue, out string? entityCatalogueFailure))
                Console.WriteLine($"[WARN] entity_catalogue_unavailable:{entityCatalogueFailure} -- nessun vnum verra' stabilito come mostro");

            using GameReferenceDatabase? entityCatalogueLifetime = entityCatalogue;
            var entityClassifier = new CatalogueClassifier(entityCatalogue);
            var entityPlayerId = new EntityId(string.Create(
                System.Globalization.CultureInfo.InvariantCulture, $"player-{processId}"));

            for (int round = 1; round <= rounds; round++)
            {
                Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"=== scout round {round} of {rounds} ==="));

                if (!attached.TryReadPlayer(out PlayerObjectReading player, out string? readFailure))
                {
                    Console.WriteLine($"[REFUSED] {readFailure}");
                    return WalkCommand.ExitAbandoned;
                }

                if (!attached.TryReadMapId(out int mapId, out string? mapFailure))
                {
                    Console.WriteLine($"[REFUSED] {mapFailure}");
                    return WalkCommand.ExitAbandoned;
                }

                DateTime now = TimeProvider.System.GetUtcNow().UtcDateTime;
                var currentMapId = new MapId(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"map-{mapId}"));

                var currentReading = new MapPositionReading(currentMapId, new WorldPosition(player.X, player.Y), now);
                if (previousReading is { } previous)
                {
                    Portal? crossing = PortalCrossingDetector.DetectCrossing(previous, currentReading);
                    if (crossing is not null)
                        mapReconstruction.RecordPortalCrossing(crossing, now);
                }
                previousReading = currentReading;

                if (!footprint.MapId.Equals(currentMapId))
                {
                    footprint = ExplorationFootprint.Empty(currentMapId, "scout_command_session_start", now);
                }

                // The minimal snapshot MapReconstructionSource.Resolve needs: it only
                // reads the map id off the snapshot's own Map to decide what to
                // reconstruct; everything else stays honestly unknown.
                WorldModelSnapshot snapshotForResolve =
                    WorldModelSnapshot.Unknown("scout_command_local_read", now) with
                    {
                        Map = MapModel.Unknown(currentMapId, "scout_command_local_read", now)
                    };
                MapModel map = mapReconstruction.Resolve(snapshotForResolve, now);

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

                var chain = new StepGuardChain(
                    () => components.SessionAuthority?.CurrentRefusal() ?? SessionActuationAuthority.NoSessionReason,
                    () => components.Safety.Policy,
                    projection);

                var executor = new SingleStepExecutor(chain, gated, () => window.Handle);
                var origin = new MapPoint(player.X, player.Y);
                // Nothing has looked through the occupancy feed in this process. An
                // empty list would claim it had looked and seen nothing; null is that
                // it has not looked, which OccupancyFreshness already refuses by name.
                var view = new OccupancyView(null, now);
                ActuationAuthority authority = ActuationAuthority.Commanded(Flag);
                var controller = new PathWalkController();

                // The frontier ranker's risk term reads this round's mobs. It is
                // empty when no feed opened, which is not a claim that nothing
                // is there -- see ObserveMobs.
                EquatableArray<Mob> roundMobs =
                    ObserveMobs(entityFeed, entityClassifier, entityPlayerId, round, now);
                var worldPlayerPosition = new WorldPosition(player.X, player.Y);
                // The evidence line is printed by a lambda, and a lambda cannot capture
                // the ref-typed `map` parameter of the call below, so the round's map
                // is copied into a local first (MapModel is an immutable record).
                MapModel roundMap = map;

                WalkRun? run = ExecuteOneRound(
                    map,
                    footprint,
                    worldPlayerPosition,
                    roundMobs,
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
                            NosAi.Runtime.Contracts.DataSourceKind.Live)
                        : null,
                    onEvidence: evidence =>
                    {
                        string detail = evidence.Detail is { } named ? $" ({named})" : string.Empty;
                        Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                            $"step-evidence: {evidence.Result} requested={evidence.Requested.Column},{evidence.Requested.Row}{detail}"));

                        ActionOutcomeRecorder.RecordMovement(
                            ledgerStore,
                            new ActionId(Guid.NewGuid().ToString("N")),
                            "scout-step",
                            issuedAtUtc: now,
                            evidence,
                            MemoryType.Spatial,
                            context: $"scout:{roundMap.Id.Value}",
                            recordedAtUtc: now);
                    },
                    out ExplorationFootprint updatedFootprint,
                    out NavigationPlan plan,
                    now);

                footprint = updatedFootprint;

                if (run is null)
                {
                    // Nothing reachable this round: print why and stop early rather
                    // than looping the remaining rounds uselessly.
                    Console.WriteLine($"nothing-to-scout: map={roundMap.Id.Value}"
                        + (plan.IsReachable.HasValue && !plan.IsReachable.Value
                            ? " (explored)"
                            : plan.IsReachable.Reason is { } reason ? $" ({reason})" : string.Empty));
                    return ExitNothingToScout;
                }

                Console.Write(run.Value.Text);

                if (run.Value.ExitCode != WalkCommand.ExitArrived)
                {
                    // Abandoned/refused walk: do not keep scouting.
                    return run.Value.ExitCode;
                }

                if (round < rounds)
                    Console.WriteLine();
            }

            return WalkCommand.ExitArrived;
        }
    }

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
