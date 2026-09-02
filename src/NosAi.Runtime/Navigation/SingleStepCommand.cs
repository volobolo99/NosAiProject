using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using NosAi.LiveIntegration;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Gate2;
using NosAi.Runtime.Gate3;
using NosAi.Runtime.LowLevel;
using NosAi.Runtime.Orchestration;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Safety;
using NosAi.Runtime.Testing;

namespace NosAi.Runtime.Navigation;

/// <summary>The printed report, the exit code, and the audit events of one <c>--step</c>.</summary>
/// <param name="Text">What the operator sees, in the order the session requires.</param>
/// <param name="ExitCode">Zero only for <see cref="MovementOutcome.Succeeded"/>.</param>
/// <param name="Events">
/// Authorization always; emission and verification only when the irreversible
/// step actually left. Every payload names the act's authority.
/// </param>
/// <param name="Step">The executor's record, for a caller that already has it.</param>
public readonly record struct SingleStepRun(
    string Text,
    int ExitCode,
    IReadOnlyList<RuntimeEvent> Events,
    StepReport Step);

/// <summary>
/// The operator command for one adjacent-cell step (S4 / C2-4).
/// </summary>
/// <remarks>
/// <para>
/// Prints and audits. It does not compose the guards, does not choose their order,
/// and has no route to the input backend that does not pass through
/// <see cref="SingleStepExecutor"/>. The authority of the act is
/// <see cref="ActuationAuthority.Commanded"/> named <c>--step</c>: no scope opens
/// without it (ADR-0020).
/// </para>
/// <para>
/// It does not arm live input and it does not take a session-authority probe. Both
/// would be acts this command did not print as its own. The chain already refuses
/// an unarmed policy and an unverified session, and those refusals are the report.
/// </para>
/// </remarks>
public static class SingleStepCommand
{
    /// <summary>The flag, and the name recorded as the commanded authority.</summary>
    public const string Flag = "--step";

    /// <summary>Where the audit events are attributed.</summary>
    public const string SourceModule = "Navigation";

    /// <summary>Session id of a one-shot operator command, not a Gate 2 cycle.</summary>
    public const string OperatorSessionId = "operator-step";

    /// <summary>Written for every step that was asked, emitted or not.</summary>
    public const string AuthorizationEventType = "step.authorization";

    /// <summary>Written only when the irreversible step actually left.</summary>
    public const string EmissionEventType = "step.emission";

    /// <summary>Written only after an emission, with the verifier's measurement.</summary>
    public const string VerificationEventType = "step.verification";

    /// <summary>JSON field every audit event carries. Never omitted, never empty.</summary>
    public const string AuthorityField = "authority";

    /// <summary>The step arrived on the cell that was asked for.</summary>
    public const int ExitSucceeded = 0;

    /// <summary>A guard refused. The irreversible step was not attempted.</summary>
    public const int ExitGuardRefused = 1;

    /// <summary>The flag was present without two integer offsets.</summary>
    public const int ExitUsage = 2;

    /// <summary>The guards passed and nothing was emitted.</summary>
    public const int ExitNotEmitted = 3;

    /// <summary>Emitted; the character was watched and did not leave the origin.</summary>
    public const int ExitStalled = 4;

    /// <summary>Emitted; the character moved somewhere other than the destination.</summary>
    public const int ExitDisplaced = 5;

    /// <summary>Emitted; no reading postdating the act arrived in time.</summary>
    public const int ExitUnobserved = 6;

    /// <summary>Reported off Windows, where there is no session window to bind.</summary>
    public const string NotWindowsReason = "step_requires_windows";

    /// <summary>Reported when the composed backend is not the gated one.</summary>
    public const string UngatedBackendReason = "step_input_backend_not_gated";

    /// <summary>Console entry for <c>--step &lt;dx&gt; &lt;dy&gt;</c>.</summary>
    public static int Run(int dx, int dy)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine($"[REFUSED] {NotWindowsReason}");
            return ExitGuardRefused;
        }

        return RunWindows(dx, dy);
    }

    /// <summary>
    /// Authorises, possibly emits, formats and audits one step against a stated
    /// executor. The live command builds that executor from
    /// <see cref="RuntimeComposition"/>; tests pass a recording backend.
    /// </summary>
    /// <param name="authority">
    /// Required. The live command always passes
    /// <see cref="ActuationAuthority.Commanded"/>(<see cref="Flag"/>). A default
    /// value is <see cref="ActuationAuthorityKind.None"/> and the gate refuses it
    /// by name — that is the missing-authority case, not a way to skip the
    /// parameter.
    /// </param>
    public static SingleStepRun Execute(
        in StepRequest request,
        SingleStepExecutor executor,
        in ActuationAuthority authority,
        Func<PositionReading?> readPosition,
        string sessionId = OperatorSessionId,
        DateTime? timestampUtc = null)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(readPosition);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        StepReport step = executor.Step(in request, in authority, readPosition);
        DateTime at = timestampUtc ?? TimeProvider.System.GetUtcNow().UtcDateTime;
        IReadOnlyList<RuntimeEvent> events = Audit(in request, step, in authority, sessionId, at);
        return new SingleStepRun(Format(in request, step), ExitCode(step), events, step);
    }

    /// <summary>The operator-facing block. Stable enough to assert against.</summary>
    public static string Format(in StepRequest request, StepReport report)
    {
        var text = new StringBuilder();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"request: map={request.Grid.MapId} from={request.From.X},{request.From.Y} to={request.To.X},{request.To.Y}"));

        foreach (StepGuardOutcome outcome in report.Authorization.Outcomes)
        {
            string state = outcome.State.ToString();
            text.Append((outcome.Guard.ToString() + ":").PadRight(13));
            if (outcome.State == StepGuardState.Refused && outcome.RefusalReason is { } reason)
                text.AppendLine($"{state}  {reason}");
            else
                text.AppendLine(state);
        }

        if (report.Authorization.IsAuthorized)
        {
            GeometryShape scale = report.Authorization.Scale;
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"pixel: {report.Authorization.ScreenX},{report.Authorization.ScreenY} scale={scale.Width}x{scale.Height} dpi={scale.Dpi}"));
        }

        if (report.Emitted)
        {
            string elapsed = report.Verification.Elapsed.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture);
            string detail = report.Verification.Detail is { } named ? $"  {named}" : string.Empty;
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"verifier: {report.Verification.Outcome} {elapsed}ms{detail}"));
        }
        else
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"not-emitted: {report.EmissionRefusal}"));
        }

        return text.ToString();
    }

    /// <summary>
    /// Audit events for the chain, in the order they happened. An emission event
    /// without an emission is the lie this audit exists to prevent.
    /// </summary>
    public static IReadOnlyList<RuntimeEvent> Audit(
        in StepRequest request,
        StepReport report,
        in ActuationAuthority authority,
        string sessionId = OperatorSessionId,
        DateTime? timestampUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        DateTime at = timestampUtc ?? TimeProvider.System.GetUtcNow().UtcDateTime;
        string named = NamedAuthority(in authority);
        var events = new List<RuntimeEvent>(3)
        {
            Event(sessionId, AuthorizationEventType, at, AuthorizationPayload(in request, report, named))
        };

        if (report.Emitted)
        {
            events.Add(Event(sessionId, EmissionEventType, at, EmissionPayload(report, named)));
            events.Add(Event(sessionId, VerificationEventType, at, VerificationPayload(report, named)));
        }

        return events;
    }

    /// <summary>Zero only for a verified arrival on the asked-for cell.</summary>
    public static int ExitCode(StepReport report)
    {
        if (!report.Authorization.IsAuthorized)
            return ExitGuardRefused;

        if (!report.Emitted)
            return ExitNotEmitted;

        return report.Verification.Outcome switch
        {
            MovementOutcome.Succeeded => ExitSucceeded,
            MovementOutcome.Stalled => ExitStalled,
            MovementOutcome.Displaced => ExitDisplaced,
            MovementOutcome.Unobserved => ExitUnobserved,
            _ => ExitNotEmitted
        };
    }

    /// <summary>
    /// Writes the events to the durable store so <see cref="EventLogReader"/> can
    /// read them back in the same order. Disposal of the batch logger is what
    /// commits them; a process that skipped it would print an audit it never kept.
    /// </summary>
    public static void Persist(IReadOnlyList<RuntimeEvent> events, string? databasePath = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (events.Count == 0)
            return;

        string path = string.IsNullOrWhiteSpace(databasePath)
            ? EventLogDiagnostics.DefaultDatabasePath
            : databasePath;

        var logger = new NosAiSqliteBatchLogger(new SqliteStoragePolicy(path));
        try
        {
            foreach (RuntimeEvent runtimeEvent in events)
                logger.EnqueueEvent(runtimeEvent);
        }
        finally
        {
            logger.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [SupportedOSPlatform("windows")]
    private static int RunWindows(int dx, int dy)
    {
        RuntimeComponents components = RuntimeComposition.CreateSafe();
        if (components.InputBackend is not GatedInputBackend gated)
        {
            Console.WriteLine($"[REFUSED] {UngatedBackendReason}");
            return ExitGuardRefused;
        }

        if (!TryFindWindow(out ClientWindow window, out int processId, out string? windowFailure))
        {
            Console.WriteLine($"[REFUSED] {windowFailure}");
            return ExitGuardRefused;
        }

        if (!ClientMemorySession.TryAttach(out ClientMemorySession? session, out string? attachFailure, processId))
        {
            Console.WriteLine($"[REFUSED] {attachFailure}");
            return ExitGuardRefused;
        }

        using (session)
        {
            if (!session!.TryReadPlayer(out PlayerObjectReading player, out string? readFailure))
            {
                Console.WriteLine($"[REFUSED] {readFailure}");
                return ExitGuardRefused;
            }

            if (!session.TryReadMapId(out int mapId, out string? mapFailure))
            {
                Console.WriteLine($"[REFUSED] {mapFailure}");
                return ExitGuardRefused;
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
            var from = new MapPoint(player.X, player.Y);
            var to = new MapPoint(from.X + dx, from.Y + dy);
            DateTime now = TimeProvider.System.GetUtcNow().UtcDateTime;
            // Nothing has looked through the occupancy feed in this process. An empty
            // list would claim it had looked and seen nothing; null is that it has
            // not looked, which OccupancyFreshness already refuses by name.
            var request = new StepRequest(from, to, grid, new OccupancyView(null, now), now);
            ActuationAuthority authority = ActuationAuthority.Commanded(Flag);

            SingleStepRun run = Execute(
                in request,
                executor,
                in authority,
                () => attached.TryReadPlayer(out PlayerObjectReading current, out _)
                    ? new PositionReading(
                        new MapPoint(current.X, current.Y),
                        TimeProvider.System.GetUtcNow().UtcDateTime,
                        DataSourceKind.Live)
                    : null);

            Console.WriteLine($"=== step dx={dx.ToString(CultureInfo.InvariantCulture)} dy={dy.ToString(CultureInfo.InvariantCulture)} ===");
            Console.Write(run.Text);

            try
            {
                Persist(run.Events);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] audit_not_persisted:{ex.GetType().Name}");
            }

            return run.ExitCode;
        }
    }

    private static string NamedAuthority(in ActuationAuthority authority)
    {
        string named = authority.Describe();
        return string.IsNullOrEmpty(named) ? "none" : named;
    }

    private static RuntimeEvent Event(string sessionId, string type, DateTime at, string payload) =>
        new(Guid.NewGuid(), sessionId, 0, at, SourceModule, type, EventPriority.NormalAudit, payload);

    private static string AuthorizationPayload(in StepRequest request, StepReport report, string authority)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AuthorityField] = authority,
            ["mapId"] = request.Grid.MapId.ToString(CultureInfo.InvariantCulture),
            ["fromX"] = request.From.X.ToString(CultureInfo.InvariantCulture),
            ["fromY"] = request.From.Y.ToString(CultureInfo.InvariantCulture),
            ["toX"] = request.To.X.ToString(CultureInfo.InvariantCulture),
            ["toY"] = request.To.Y.ToString(CultureInfo.InvariantCulture),
            ["authorized"] = report.Authorization.IsAuthorized ? "true" : "false",
            ["refusedAt"] = report.Authorization.RefusedAt?.ToString() ?? string.Empty,
            ["refusalReason"] = report.Authorization.RefusalReason ?? string.Empty,
            ["emitted"] = report.Emitted ? "true" : "false",
            ["emissionRefusal"] = report.EmissionRefusal ?? string.Empty
        };

        foreach (StepGuardOutcome outcome in report.Authorization.Outcomes)
        {
            string key = "guard." + outcome.Guard;
            fields[key] = outcome.State.ToString();
            if (outcome.State == StepGuardState.Refused && outcome.RefusalReason is { } reason)
                fields[key + ".reason"] = reason;
        }

        return JsonSerializer.Serialize(fields);
    }

    private static string EmissionPayload(StepReport report, string authority)
    {
        GeometryShape scale = report.Authorization.Scale;
        string emittedAt = report.EmittedAtUtc is { } at
            ? at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
            : string.Empty;

        return JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AuthorityField] = authority,
            ["screenX"] = report.Authorization.ScreenX.ToString(CultureInfo.InvariantCulture),
            ["screenY"] = report.Authorization.ScreenY.ToString(CultureInfo.InvariantCulture),
            ["scaleWidth"] = scale.Width.ToString(CultureInfo.InvariantCulture),
            ["scaleHeight"] = scale.Height.ToString(CultureInfo.InvariantCulture),
            ["scaleDpi"] = scale.Dpi.ToString(CultureInfo.InvariantCulture),
            ["emittedAtUtc"] = emittedAt
        });
    }

    private static string VerificationPayload(StepReport report, string authority) =>
        JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [AuthorityField] = authority,
            ["outcome"] = report.Verification.Outcome.ToString(),
            ["elapsedMs"] = report.Verification.Elapsed.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture),
            ["readingsAccepted"] = report.Verification.ReadingsAccepted.ToString(CultureInfo.InvariantCulture),
            ["detail"] = report.Verification.Detail ?? string.Empty
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
