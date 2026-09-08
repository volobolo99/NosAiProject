using System.Globalization;
using System.Runtime.Versioning;
using NosAi.Core.Memory;
using NosAi.LiveIntegration;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.GameData;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Orchestration;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Perception.Network;
using NosAi.Runtime.Testing;

namespace NosAi.Runtime.Tactical;

/// <summary>
/// The operator command that clicks one established target and asks the wire
/// whether the click selected it (AP-05 "click the target", execute → verify).
/// </summary>
/// <remarks>
/// <para>
/// <c>--click-target &lt;entityId&gt;</c> clicks that exact entity, or
/// <c>--click-target --vnum &lt;vnum&gt;</c> clicks the nearest established
/// entity with that vnum. Both go through
/// <see cref="ClickTargetExecutor"/>, which assesses, projects, confines the
/// pixel to the window, opens one actuation scope, clicks, and then verifies
/// on the wire: the decoder publishes <see cref="PlayerTargetSelection"/> from
/// <c>ct</c> with the target id, so the command can report — from the server,
/// in milliseconds — whether the click selected the right entity, a different
/// one, or nothing.
/// </para>
/// <para>
/// <b>One click per invocation.</b> No loop, no retry: this command establishes
/// that a click works and is verifiable. A loop is built later, on top of
/// something proven, never below it.
/// </para>
/// <para>
/// <b>Fail closed.</b> Without <c>--arm-input</c>, without a calibration, or
/// with the window out of focus, the click does not leave and the refusal names
/// the real cause — the same direction every actuating command here takes. This
/// command never arms input itself.
/// </para>
/// </remarks>
public static class ClickTargetCommand
{
    /// <summary>The flag, and the name recorded as the commanded authority.</summary>
    public const string Flag = "--click-target";

    /// <summary>Where the audit events are attributed.</summary>
    public const string SourceModule = "Tactical";

    /// <summary>Session id of a one-shot operator command, not a Gate 2 cycle.</summary>
    public const string OperatorSessionId = "operator-click-target";

    /// <summary>Selects by vnum instead of by entity id; its value is the vnum.</summary>
    public const string VnumOption = "--vnum";

    /// <summary>Arms live input. Without it the command refuses before attaching to anything.</summary>
    public const string ArmInputOption = "--arm-input";

    /// <summary>An argument starting with <c>--</c> that is not a known option.</summary>
    public const string UnknownOptionReason = "unknown_option";

    /// <summary>Neither an entity id nor <c>--vnum</c> was given.</summary>
    public const string MissingTargetReason = "click_target_requires_entity_id_or_vnum";

    /// <summary>The positional argument was not a positive entity id.</summary>
    public const string InvalidEntityIdReason = "invalid_entity_id";

    /// <summary><c>--vnum</c> carried no value, or a value that is not a positive integer.</summary>
    public const string InvalidVnumReason = "invalid_vnum";

    /// <summary>More than one target was named (two positionals, or an id and a vnum).</summary>
    public const string AmbiguousTargetReason = "click_target_ambiguous_target";

    /// <summary>Live input was not armed with <c>--arm-input</c>.</summary>
    public const string InputNotArmedReason = "click_target_input_not_armed";

    /// <summary>Reported off Windows, where there is no client to attach to.</summary>
    public const string NotWindowsReason = "click_target_requires_windows";

    /// <summary>Reported when the composed backend is not the gated one.</summary>
    public const string UngatedBackendReason = "click_target_input_backend_not_gated";

    /// <summary>
    /// Reported when the packet capture could not open. Unlike <c>--scout</c> it is
    /// a refusal, not a warning: both the target choice and the wire verdict depend
    /// on the feed, so a click made without it would be unverifiable.
    /// </summary>
    public const string EntityFeedUnavailableReason = "click_target_entity_feed_unavailable";

    /// <summary>Reported when the operator's entity id names nothing observed.</summary>
    public const string EntityNotFoundReason = "click_target_entity_not_found";

    /// <summary>Reported when no observed entity carries the requested vnum.</summary>
    public const string VnumNotFoundReason = "click_target_vnum_not_observed";

    /// <summary>Reported when the player's position could not be read for the nearest-by-vnum choice.</summary>
    public const string PlayerPositionUnreadableReason = "click_target_player_position_unreadable";

    /// <summary>Exit code: the wire confirmed the click selected the requested id.</summary>
    public const int ExitConfirmed = 0;

    /// <summary>Exit code: the wire named a different id after the click.</summary>
    public const int ExitDifferentTarget = 1;

    /// <summary>Exit code: no <c>ct</c> arrived within the verification window.</summary>
    public const int ExitNotConfirmed = 2;

    /// <summary>Exit code: refused before or at the click, with a named reason.</summary>
    public const int ExitRefused = 3;

    /// <summary>Exit code: the arguments did not name a target, or named it ambiguously.</summary>
    public const int ExitUsage = 4;

    /// <summary>
    /// Validates the argument vector. Returns the named refusal reason, or null
    /// when well-formed (with the parsed entity id or vnum and the arming state).
    /// </summary>
    public static string? TryParse(string[] args, out long? entityId, out int? vnum, out bool armInput)
    {
        entityId = null;
        vnum = null;
        armInput = false;

        int flagIndex = Array.FindIndex(args, a => string.Equals(a, Flag, StringComparison.OrdinalIgnoreCase));
        if (flagIndex < 0)
            return MissingTargetReason;

        for (int i = flagIndex + 1; i < args.Length; i++)
        {
            string arg = args[i];

            if (string.Equals(arg, VnumOption, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length
                    || !int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedVnum)
                    || parsedVnum <= 0)
                    return InvalidVnumReason;
                vnum = parsedVnum;
                i++;
                continue;
            }

            if (string.Equals(arg, ArmInputOption, StringComparison.OrdinalIgnoreCase))
            {
                armInput = true;
                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
                return $"{UnknownOptionReason}:{arg}";

            if (entityId is not null)
                return AmbiguousTargetReason;

            if (!long.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsedId) || parsedId <= 0)
                return InvalidEntityIdReason;
            entityId = parsedId;
        }

        if (entityId is null && vnum is null)
            return MissingTargetReason;

        if (entityId is not null && vnum is not null)
            return AmbiguousTargetReason;

        return null;
    }

    /// <summary>Console entry for <c>--click-target</c>.</summary>
    public static int Run(string[] args)
    {
        string? refusal = TryParse(args, out long? entityId, out int? vnum, out bool armInput);
        if (refusal is not null)
        {
            Console.WriteLine($"[REFUSED] {refusal}");
            Console.WriteLine($"Usage: {Flag} <entityId> --arm-input | {Flag} --vnum <vnum> --arm-input");
            return ExitUsage;
        }

        if (!armInput)
        {
            Console.WriteLine($"[REFUSED] {InputNotArmedReason}");
            Console.WriteLine($"  {Flag} clicks the character's target; pass --arm-input to arm live input.");
            return ExitRefused;
        }

        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine($"[REFUSED] {NotWindowsReason}");
            return ExitRefused;
        }

        return RunWindows(entityId, vnum);
    }

    /// <summary>
    /// The live composition. Mirror of <c>ScoutCommand.RunWindows</c> (same
    /// <see cref="RuntimeComposition.CreateSafe"/>, window lookup,
    /// <see cref="ClientMemorySession.TryAttach"/>, projection construction) plus
    /// the one thing <c>--scout</c> treats as opportunistic and this command
    /// treats as required: the entity feed, which both the target choice and the
    /// wire verdict read.
    /// </summary>
    /// <remarks>
    /// Untested by design, exactly like <c>ScoutCommand.RunWindows</c>: only the
    /// pure <see cref="TryParse"/> and <see cref="ClickTargetExecutor"/> are unit
    /// tested. A unit test must not be one elevation and one attached client away
    /// from actuating.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    private static int RunWindows(long? entityId, int? vnum)
    {
        RuntimeComponents components = RuntimeComposition.CreateSafe();
        if (components.InputBackend is not GatedInputBackend gated)
        {
            Console.WriteLine($"[REFUSED] {UngatedBackendReason}");
            return ExitRefused;
        }

        if (!TryFindWindow(out ClientWindow window, out int processId, out string? windowFailure))
        {
            Console.WriteLine($"[REFUSED] {windowFailure}");
            return ExitRefused;
        }

        if (!ClientMemorySession.TryAttach(out ClientMemorySession? session, out string? attachFailure, processId))
        {
            Console.WriteLine($"[REFUSED] {attachFailure}");
            return ExitRefused;
        }

        using (session)
        {
            components.SessionAuthority?.BeginSession(window.Handle, processId);

            if (components.HumanInput is HumanInputMonitor monitor
                && !monitor.TryStart(out string? watchFailure))
            {
                Console.WriteLine($"[WARN] human monitor: {watchFailure}");
            }

            // Required, not opportunistic: the wire is what verifies the click.
            using LiveObservationScope? feed = LiveObservationScope.TryOpen(processId, out string? feedFailure);
            if (feed is null)
            {
                Console.WriteLine($"[REFUSED] {EntityFeedUnavailableReason}:{feedFailure}");
                return ExitRefused;
            }

            // A missing catalogue establishes no vnum as a monster; the executor's
            // Assess answers for that honestly rather than this command guessing.
            GameReferenceDatabase? catalogue = null;
            if (!GameReferenceLocator.TryOpen(out catalogue, out string? catalogueFailure))
                Console.WriteLine($"[WARN] entity_catalogue_unavailable:{catalogueFailure} -- nessun vnum verra' stabilito come mostro");
            using GameReferenceDatabase? catalogueLifetime = catalogue;

            if (!session!.TryReadPlayer(out PlayerObjectReading player, out string? readFailure))
            {
                Console.WriteLine($"[REFUSED] {readFailure}");
                return ExitRefused;
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

            GameplayObservation observation = feed.Gateway.Capture().Gameplay;
            IReadOnlyList<SelectableEntity> entities = observation.Entities.HasValue
                ? observation.Entities.Value
                : Array.Empty<SelectableEntity>();

            SelectableEntity target;
            string rationale;
            if (entityId is long id)
            {
                SelectableEntity? found = null;
                foreach (SelectableEntity entity in entities)
                {
                    if (entity.EntityId == id)
                    {
                        found = entity;
                        break;
                    }
                }

                if (found is not { } named)
                {
                    Console.WriteLine($"[REFUSED] {EntityNotFoundReason}:{id}");
                    return ExitRefused;
                }

                target = named;
                rationale = DescribeById(named, id);
            }
            else
            {
                var matching = new List<SelectableEntity>();
                foreach (SelectableEntity entity in entities)
                {
                    if (entity.Vnum == vnum)
                        matching.Add(entity);
                }

                if (matching.Count == 0)
                {
                    Console.WriteLine($"[REFUSED] {VnumNotFoundReason}:{vnum}");
                    return ExitRefused;
                }

                var playerPosition = ClassifiedValue<MapPoint>.Live(new MapPoint(player.X, player.Y));
                if (!TargetSelector.TrySelect(
                        matching,
                        playerPosition,
                        TimeProvider.System.GetUtcNow().UtcDateTime,
                        TargetSelectionPolicy.Default,
                        out TargetChoice? choice,
                        out string? selectFailure,
                        isAttackable: e => TargetEstablishment.Assess(e, observation.HitBy, observation.SelectedTarget, catalogue).IsEstablished))
                {
                    Console.WriteLine($"[REFUSED] {PlayerPositionUnreadableReason}:{selectFailure}");
                    return ExitRefused;
                }

                target = choice!.Entity;
                rationale = choice.Rationale;
            }

            var executor = new ClickTargetExecutor(gated, projection, () => window.Handle);
            ActuationAuthority authority = ActuationAuthority.Commanded(Flag);
            var request = new ClickTargetRequest(target, observation.HitBy, observation.SelectedTarget, catalogue);

            Console.WriteLine($"target: {rationale}");

            ClickTargetReport report = executor.Click(
                in request,
                in authority,
                readLatestSelection: () =>
                {
                    ClassifiedValue<TargetedEntity> selected = feed.Gateway.Capture().Gameplay.SelectedTarget;
                    return selected.HasValue
                        ? new PlayerTargetSelection(selected.Value, selected.ObservedAtUtc, selected.Source)
                        : null;
                });

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"pixel: {report.ScreenX},{report.ScreenY}"));

            if (!report.Emitted)
            {
                Console.WriteLine($"not-emitted: {report.RefusalReason}");
                return ExitRefused;
            }

            Console.WriteLine($"scope: emitted");
            Console.WriteLine($"verification: {DescribeVerification(report.Verification)}");

            return report.Verification.Outcome switch
            {
                TargetSelectionOutcome.Confirmed => ExitConfirmed,
                TargetSelectionOutcome.DifferentTarget => ExitDifferentTarget,
                _ => ExitNotConfirmed
            };
        }
    }

    private static string DescribeById(SelectableEntity entity, long id)
    {
        string kind = entity.Kind ?? "specie non osservata";
        string vnum = entity.Vnum is { } v ? $" vnum={v}" : " vnum non osservato";
        string health = entity.HpRatio is { } ratio
            ? $" {ratio * 100:F0}% vita"
            : " vita non nota";
        return string.Create(CultureInfo.InvariantCulture,
            $"entita' {id} ({kind},{vnum},{health})");
    }

    private static string DescribeVerification(ClickTargetVerification verification) => verification.Outcome switch
    {
        TargetSelectionOutcome.Confirmed => string.Create(CultureInfo.InvariantCulture,
            $"confirmed target {verification.TargetEntityId} in {verification.Waited.TotalMilliseconds:F0}ms"),
        TargetSelectionOutcome.DifferentTarget => string.Create(CultureInfo.InvariantCulture,
            $"wrong-target: ct named {verification.ObservedEntityId} instead of {verification.TargetEntityId}"),
        TargetSelectionOutcome.NotConfirmed => string.Create(CultureInfo.InvariantCulture,
            $"not-confirmed: no ct within {verification.Waited.TotalMilliseconds:F0}ms"),
        _ => "not-attempted"
    };

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
