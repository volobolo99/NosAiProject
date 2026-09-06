using System.Runtime.Versioning;
using NosAi.Core.Memory;
using NosAi.Core.WorldModel;
using NosAi.Core.WorldModel.Combat;
using NosAi.Core.WorldModel.Exploration;
using NosAi.Core.WorldModel.Reconstruction;
using NosAi.Core.WorldModel.Strategy;
using NosAi.LiveIntegration;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate2;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Navigation;
using NosAi.Runtime.Orchestration;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Testing;
using NosAi.Runtime.WorldModel.Fusion;
using NosAi.Storage;

namespace NosAi.Runtime.Tactical;

/// <summary>
/// The first operator command that <b>chooses</b> which pre-built,
/// pre-audited act to attempt, cycle by cycle: <c>--autoplay
/// [--cycles &lt;n&gt;] [--recover-slot &lt;slot&gt;]</c> reads the live
/// player vitals/position/map and the session's exploration footprint,
/// asks <see cref="StrategyPlanner.SelectStrategicPlan"/> which
/// <see cref="StrategicGoalKind"/> is most urgent this cycle, and
/// dispatches <see cref="StrategicGoalKind.Survival"/> to
/// <see cref="RecoverCommand.ExecuteOneRound"/> or
/// <see cref="StrategicGoalKind.Exploration"/> to
/// <see cref="ScoutCommand.ExecuteOneRound"/>. Everything else -- the
/// key presses, the per-cell walks, the Guard/Safety gates -- is exactly
/// the machinery those two commands already run unchanged; this command
/// only decides <i>which</i> of them to invoke.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope, stated as hard limits (AP-08/A2A4 spec).</b> Only
/// <see cref="StrategicGoalKind.Survival"/> and
/// <see cref="StrategicGoalKind.Exploration"/> are dispatched.
/// <see cref="StrategicGoalKind.QuestUrgency"/> is deliberately <b>not</b>:
/// <c>WorldModelSnapshot.Quests</c> is empty in every live composition
/// today (no network/OCR channel populates real quests yet), so
/// <see cref="StrategyPlanner.AssessQuestUrgency"/> returns
/// <see langword="null"/> on every real cycle and
/// <see cref="StrategyPlanner.SelectStrategicPlan"/> never selects it --
/// dispatch code for a signal that cannot fire honestly today would be dead
/// code. <see cref="StrategicGoalKind.Recovery"/> now has a real assessor
/// (<see cref="StrategyPlanner.AssessRecoveryUrgency"/>, gated on a real
/// "currently in combat" fact) but this command does not yet call it --
/// wiring it into this cycle (deriving that fact, adding the signal, and
/// dispatching it) is a separate, not-yet-delivered increment
/// (AP-08/A2A4, `AP-08_A2A4_DEEPSEEK_recovery_signal.md`).
/// <see cref="StrategicGoalKind.Progression"/>/<see cref="StrategicGoalKind.Farming"/>/
/// <see cref="StrategicGoalKind.Optimization"/> have no assessor at all and
/// are out of scope for the same reason as ever. A selected plan whose kind
/// is not dispatchable is reported as
/// <see cref="AutoplayDispatch.NotDispatchable"/> by name, never silently
/// ignored and never substituted with a different act.
/// </para>
/// <para>
/// <b>What this command is not.</b> It never constructs an
/// <see cref="ActuationScope"/>, never presses a key and never moves the
/// mouse itself. It does not generate candidates or add planning logic. It
/// does not arm input, and when both underlying commands' own gates refuse
/// (as <c>--engage</c>/<c>--recover</c> currently do on the armed
/// production gate), the refusal is reported faithfully -- never worked
/// around.
/// </para>
/// </remarks>
public static class AutoplayCommand
{
    /// <summary>The flag, and the name recorded as the commanded authority.</summary>
    public const string Flag = "--autoplay";

    /// <summary>The hard ceiling on cycles one invocation may run.</summary>
    public const int MaxCycles = 20;

    /// <summary>Printed when a requested cycle count exceeds <see cref="MaxCycles"/> or is below one.</summary>
    public const string CyclesExceedsMaxReason = "autoplay_cycles_exceeds_max";

    /// <summary>Printed when Survival is urgent but no recovery slot was configured.</summary>
    public const string SurvivalNoSlotWarning = "survival urgent but no --recover-slot configured, skipping this cycle";

    /// <summary>How long the after-read waits for the consumable's effect to land.</summary>
    /// <remarks>
    /// The same live value RecoverCommand.RunWindows injects (its own constant
    /// is private there; this command may not modify that file, so the shared
    /// delay is stated here).
    /// </remarks>
    private const int VerificationDelayMs = 350;

    /// <summary>Reported off Windows, where there is no client to attach to.</summary>
    public const string NotWindowsReason = "autoplay_requires_windows";

    /// <summary>Reported when the composed backend is not the gated one.</summary>
    public const string UngatedBackendReason = "autoplay_input_backend_not_gated";

    /// <summary>What one <see cref="ExecuteOneCycle"/> decided to do.</summary>
    public enum AutoplayDispatch
    {
        /// <summary><see cref="StrategicPlan.SelectedKind"/> was null: nothing was urgent this cycle.</summary>
        Idle = 0,

        /// <summary>Dispatched to <see cref="ScoutCommand.ExecuteOneRound"/>.</summary>
        Explored = 1,

        /// <summary>Dispatched to <see cref="RecoverCommand.ExecuteOneRound"/>.</summary>
        Recovered = 2,

        /// <summary>Survival was selected, but no recovery slot was configured.</summary>
        SurvivalSkippedNoSlot = 3,

        /// <summary>Any other selected kind (QuestUrgency etc.) -- named, not silently ignored.</summary>
        NotDispatchable = 4
    }

    /// <summary>What happened on one autoplay cycle.</summary>
    /// <param name="Dispatch">What was decided.</param>
    /// <param name="Plan">The plan this cycle selected (already computed by the caller).</param>
    /// <param name="ScoutRun">The scout round's walk result, when <see cref="AutoplayDispatch.Explored"/>.</param>
    /// <param name="RecoverEvidence">The recover round's evidence, when <see cref="AutoplayDispatch.Recovered"/>.</param>
    /// <param name="UpdatedFootprint">
    /// The footprint to carry into the next cycle. Equal to the caller's own
    /// <c>footprint</c> parameter unless <see cref="Dispatch"/> is
    /// <see cref="AutoplayDispatch.Explored"/>, in which case it is
    /// <see cref="ScoutCommand.ExecuteOneRound"/>'s own updated footprint
    /// (folding this cycle's player position in) -- without carrying this
    /// forward, a caller that reused the original footprint every cycle would
    /// never learn the player had already visited wherever a previous cycle
    /// walked to.
    /// </param>
    public sealed record AutoplayCycleResult(
        AutoplayDispatch Dispatch,
        StrategicPlan Plan,
        WalkRun? ScoutRun,
        CombatExecutionEvidence? RecoverEvidence,
        ExplorationFootprint UpdatedFootprint);

    /// <summary>
    /// Dispatches one already-selected <see cref="StrategicPlan"/> to at most
    /// one of the two supported commands. Pure and testable: every
    /// I/O-shaped dependency is a parameter, no clock reads, no
    /// <c>Console.Write</c> -- the same discipline the two
    /// <c>ExecuteOneRound</c> methods this calls already follow.
    /// </summary>
    /// <param name="plan">The plan this cycle runs (already computed by the caller from live facts).</param>
    /// <param name="recoverSlot">The operator-named consumable slot, or <see langword="null"/> when none was configured.</param>
    /// <param name="keybinds">The operator's keybind map, for the recover branch's <c>consumable.{slot}</c> lookup.</param>
    /// <param name="input">The gated input backend both dispatched rounds press through.</param>
    /// <param name="readVitals">Reads the player's current vitals, for the recover branch.</param>
    /// <param name="verificationDelay">The recover branch's between-press-and-after-read delay.</param>
    /// <param name="map">The reconstructed map, for the scout branch.</param>
    /// <param name="footprint">The session footprint, for the scout branch.</param>
    /// <param name="playerPosition">The player's live world position, for the scout branch.</param>
    /// <param name="mobs">This cycle's known mobs, for the scout branch.</param>
    /// <param name="origin">The map cell the scout walk starts from.</param>
    /// <param name="grid">The static geometry grid, for the scout branch's guard chain/planner.</param>
    /// <param name="view">The occupancy view, for the scout branch's walk.</param>
    /// <param name="controller">The walk controller, for the scout branch.</param>
    /// <param name="chain">The guard ladder, for the scout branch.</param>
    /// <param name="executor">The step executor, for the scout branch.</param>
    /// <param name="readPosition">Re-reads the observed grid position, for the scout branch's verifier.</param>
    /// <param name="onEvidence">Called per emitted step, for the scout branch.</param>
    /// <param name="authority">
    /// The authority of every dispatched round:
    /// <see cref="ActuationAuthority.Commanded"/>(<see cref="Flag"/>) -- never
    /// <c>"--scout"</c>/<c>"--recover"</c>: the audit must show these acts
    /// were chosen by <c>--autoplay</c>, not typed directly.
    /// </param>
    /// <param name="nowUtc">The instant this cycle runs at.</param>
    public static AutoplayCycleResult ExecuteOneCycle(
        StrategicPlan plan,
        int? recoverSlot,
        KeybindMap keybinds,
        IInputBackend input,
        Func<PlayerVitalsReading?> readVitals,
        Action verificationDelay,
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
        Func<PositionReading?> readPosition,
        Action<MovementExecutionEvidence>? onEvidence,
        in ActuationAuthority authority,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.SelectedKind is not { } kind)
            return new AutoplayCycleResult(AutoplayDispatch.Idle, plan, null, null, footprint);

        switch (kind)
        {
            case StrategicGoalKind.Exploration:
            {
                WalkRun? run = ScoutCommand.ExecuteOneRound(
                    map,
                    footprint,
                    playerPosition,
                    mobs,
                    origin,
                    in grid,
                    view,
                    controller,
                    chain,
                    executor,
                    in authority,
                    readPosition,
                    onEvidence,
                    out ExplorationFootprint updatedFootprint,
                    out _,
                    nowUtc);
                // ScoutCommand.ExecuteOneRound folds this cycle's player position
                // into updatedFootprint before it ever checks reachability, so it
                // is valid even when run is null (nothing reachable) -- carrying
                // it forward is what lets the next cycle know this position was
                // already visited, exactly like ScoutCommand's own --watch loop.
                return new AutoplayCycleResult(AutoplayDispatch.Explored, plan, run, null, updatedFootprint);
            }

            case StrategicGoalKind.Survival:
            {
                if (recoverSlot is not { } slot)
                    return new AutoplayCycleResult(AutoplayDispatch.SurvivalSkippedNoSlot, plan, null, null, footprint);

                // The candidate construction mirrors RecoverCommand.RunWindows:
                // the constructor requires an Item and the slot number doubles as
                // the id (no catalogue vnum is known or needed).
                string slotAsId = slot.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var candidate = new CombatActionCandidate(
                    CombatActionKind.UseConsumable,
                    item: new ItemId(slotAsId));

                CombatExecutionEvidence evidence = RecoverCommand.ExecuteOneRound(
                    candidate,
                    slot,
                    keybinds,
                    input,
                    readVitals,
                    verificationDelay,
                    in authority,
                    nowUtc);
                return new AutoplayCycleResult(AutoplayDispatch.Recovered, plan, null, evidence, footprint);
            }

            default:
                // QuestUrgency/Recovery/Progression/Farming/Optimization: named,
                // never silently ignored, never substituted.
                return new AutoplayCycleResult(AutoplayDispatch.NotDispatchable, plan, null, null, footprint);
        }
    }

    /// <summary>
    /// Console entry for <c>--autoplay [--cycles &lt;n&gt;]
    /// [--recover-slot &lt;slot&gt;]</c>.
    /// </summary>
    public static int Run(int cycles = 1, int? recoverSlot = null)
    {
        // A requested cycle count above MaxCycles is refused cleanly, never
        // silently clamped -- the operator must ask again with a smaller
        // number, not be surprised by a quietly shortened run. Below one is
        // the same [REFUSED]-not-throw discipline every other command's
        // argument validation applies (AP-05_A5_AUDIT.md/AP-06_A5_AUDIT.md).
        if (cycles > MaxCycles || cycles < 1)
        {
            Console.WriteLine($"[REFUSED] {CyclesExceedsMaxReason}:{MaxCycles}");
            return WalkCommand.ExitAbandoned;
        }

        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine($"[REFUSED] {NotWindowsReason}");
            return WalkCommand.ExitAbandoned;
        }

        return RunWindows(cycles, recoverSlot);
    }

    /// <summary>
    /// The live composition, mirroring <see cref="ScoutCommand"/>'s and
    /// <see cref="RecoverCommand"/>'s own <c>RunWindows</c>: one shared
    /// composition for the whole invocation -- attach the client once, build
    /// the gated backend/guard chain/map reconstruction once, load the
    /// keybinds once -- then loop cycles, reading live facts and dispatching.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static int RunWindows(int cycles, int? recoverSlot)
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
            // cycles: it caches per map id (the same shape ScoutCommand.RunWindows
            // uses). Disposed on the way out.
            using var mapReconstruction = new MapReconstructionSource(logger: null);

            // Session-local memory of visited tiles, keyed to the current map id.
            // ScoutCommand.ExecuteOneRound folds each cycle's player position into
            // the footprint it is given and returns the updated one through its out
            // parameter, so the footprint advances across cycles on the same map.
            ExplorationFootprint footprint = ExplorationFootprint.Empty(
                new MapId("unknown-map"), "autoplay_command_session_start");

            // Tracks the previous cycle's (map, position) reading so a map
            // change between two consecutive cycles can be recognised as a
            // portal crossing (PortalCrossingDetector) -- null before the
            // first cycle runs. Independent of `footprint`: this is about
            // recording a fact for the map just left, not the one just
            // entered.
            MapPositionReading? previousReading = null;

            string keybindsPath = KeybindsCheck.ResolvePath();
            if (!KeybindMap.TryLoad(keybindsPath, out KeybindMap keybinds, out string? loadFailure))
            {
                Console.WriteLine($"[REFUSED] recover requires keybinds:{loadFailure} ({keybindsPath})");
                return WalkCommand.ExitAbandoned;
            }

            // Every dispatched round runs under --autoplay's own commanded
            // authority, never --scout/--recover: the audit must show these acts
            // were chosen by this command, not typed directly by an operator.
            ActuationAuthority authority = ActuationAuthority.Commanded(Flag);

            // Ledger persistence is opportunistic history, never a gate: a
            // missing NOSAI-SSD volume warns and records nothing -- it must
            // never become a reason this command refuses.
            using ActionOutcomeLedgerStore? ledgerStore =
                ActionOutcomeLedgerStore.TryOpenFromVolume(new SqliteJournalOptions(), out string? ledgerFailure);
            if (ledgerStore is null)
                Console.WriteLine($"[WARN] action_outcome_ledger_unavailable:{ledgerFailure}");

            for (int cycle = 1; cycle <= cycles; cycle++)
            {
                Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"=== autoplay cycle {cycle} of {cycles} ==="));

                if (!attached.TryReadPlayer(out PlayerObjectReading player, out string? readFailure))
                {
                    Console.WriteLine($"[REFUSED] {readFailure}");
                    return WalkCommand.ExitAbandoned;
                }

                if (!attached.TryReadPlayerVitals(out PlayerVitalsReading vitals, out string? vitalsFailure))
                {
                    Console.WriteLine($"[REFUSED] {vitalsFailure}");
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
                    footprint = ExplorationFootprint.Empty(currentMapId, "autoplay_command_session_start", now);
                }

                // The minimal snapshot MapReconstructionSource.Resolve needs: it only
                // reads the map id off the snapshot's own Map to decide what to
                // reconstruct; everything else stays honestly unknown.
                WorldModelSnapshot snapshotForResolve =
                    WorldModelSnapshot.Unknown("autoplay_command_local_read", now) with
                    {
                        Map = MapModel.Unknown(currentMapId, "autoplay_command_local_read", now)
                    };
                MapModel map = mapReconstruction.Resolve(snapshotForResolve, now);

                // A minimal Player carrying only what AssessSurvivalUrgency reads:
                // Status.Resources with one Health resource from the live vitals
                // reading. Every other field stays Unknown/empty -- the same
                // "minimal object with only the one real fact" pattern
                // ScoutCommand.RunWindows applies to the map/snapshot.
                var healthResource = new Resource(
                    ResourceKind.Health,
                    WorldFact<double>.Live(vitals.Hp, confidence: 1d, now),
                    WorldFact<double>.Live(vitals.MaxHp, confidence: 1d, now));
                Player playerFacts = new Player(
                    new EntityId("unknown-player"),
                    WorldFact<WorldPosition>.Unknown("not_read_this_cycle", now),
                    WorldFact<float>.Unknown("not_read_this_cycle", now),
                    WorldFact<bool>.Unknown("not_read_this_cycle", now),
                    WorldFact<MapId>.Unknown("not_read_this_cycle", now),
                    new CombatantStatus(
                        EquatableArray<Resource>.From(new[] { healthResource }),
                        EquatableArray<StatusEffect>.Empty),
                    EquatableArray<Skill>.Empty,
                    EquatableArray<Cooldown>.Empty,
                    EquatableArray<InventoryItem>.Empty,
                    EquatableArray<EquipmentItem>.Empty);

                StrategicSignal? survival = StrategyPlanner.AssessSurvivalUrgency(playerFacts);
                StrategicSignal? exploration = StrategyPlanner.AssessExplorationUrgency(footprint);

                var signals = new List<StrategicSignal>(2);
                if (survival is not null) signals.Add(survival);
                if (exploration is not null) signals.Add(exploration);

                StrategicPlan plan = StrategyPlanner.SelectStrategicPlan(signals, now);
                string selection = plan.SelectedKind?.ToString() ?? "none";
                Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"plan: {selection} signals={signals.Count} reason={plan.HasSelection.Reason}"));

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
                var controller = new PathWalkController();

                AutoplayCycleResult result = ExecuteOneCycle(
                    plan,
                    recoverSlot,
                    keybinds,
                    gated,
                    () => attached.TryReadPlayerVitals(out PlayerVitalsReading reading, out _)
                        ? reading
                        : null,
                    verificationDelay: () => Thread.Sleep(VerificationDelayMs),
                    map,
                    footprint,
                    new WorldPosition(player.X, player.Y),
                    EquatableArray<Mob>.Empty,
                    origin,
                    in grid,
                    view,
                    controller,
                    chain,
                    executor,
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
                            context: $"scout:{map.Id.Value}",
                            recordedAtUtc: now);
                    },
                    in authority,
                    now);

                // Carry this cycle's footprint forward regardless of what was
                // dispatched -- only the Exploration branch actually changes it,
                // but reassigning unconditionally keeps this the single place
                // that advances footprint across cycles (ExecuteOneCycle is pure
                // and never mutates the caller's local).
                footprint = result.UpdatedFootprint;

                switch (result.Dispatch)
                {
                    case AutoplayDispatch.Idle:
                        // Nothing urgent: stop now rather than looping the remaining
                        // cycles uselessly.
                        Console.WriteLine("nothing urgent, stopping");
                        return ExitNothingUrgent;

                    case AutoplayDispatch.SurvivalSkippedNoSlot:
                        Console.WriteLine($"[WARN] {SurvivalNoSlotWarning}");
                        break;

                    case AutoplayDispatch.NotDispatchable:
                        Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                            $"not-dispatchable: {result.Plan.SelectedKind}"));
                        break;

                    case AutoplayDispatch.Explored:
                    {
                        WalkRun? run = result.ScoutRun;
                        if (run is null)
                        {
                            Console.WriteLine("nothing-to-scout: no reachable frontier");
                            return ExitNothingUrgent;
                        }

                        Console.Write(run.Value.Text);
                        if (run.Value.ExitCode != WalkCommand.ExitArrived)
                            return run.Value.ExitCode;
                        break;
                    }

                    case AutoplayDispatch.Recovered:
                    {
                        CombatExecutionEvidence evidence = result.RecoverEvidence!;
                        RecoverCommand.PrintEvidence(evidence);

                        // evidence.Candidate is the exact CombatActionCandidate
                        // ExecuteOneCycle's own Survival branch built from
                        // recoverSlot -- reused here rather than rebuilt, so the
                        // recorded action is provably the one that was actually
                        // pressed, not a second, independently-constructed guess.
                        ActionOutcomeRecorder.RecordCombat(
                            ledgerStore,
                            new ActionId(Guid.NewGuid().ToString("N")),
                            evidence.Candidate,
                            issuedAtUtc: now,
                            evidence,
                            MemoryType.Combat,
                            context: $"consumable-slot:{recoverSlot!.Value}",
                            recordedAtUtc: now);

                        // A confirmed recovery is the purpose of the invocation; a
                        // deterministic abort (missing keybind, refused press) is not
                        // going to change by looping. No-gain-but-ran is transient,
                        // which is what the remaining cycles are for.
                        if (evidence.Result == CombatExecutionResult.ResourceGainConfirmed)
                            return WalkCommand.ExitArrived;

                        if (evidence.Result == CombatExecutionResult.Aborted)
                            return WalkCommand.ExitAbandoned;
                        break;
                    }
                }

                if (cycle < cycles)
                    Console.WriteLine();
            }

            // Every requested cycle ran and none ended the loop early.
            return WalkCommand.ExitArrived;
        }
    }

    /// <summary>The loop ended early because a cycle had nothing urgent to do.</summary>
    public const int ExitNothingUrgent = 7;

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
