using System.Globalization;
using System.Runtime.Versioning;
using NosAi.LiveIntegration;
using NosAi.Runtime.Perception;

namespace NosAi.Runtime.LowLevel;

/// <summary>
/// One reading of the five commit-point conditions, plus the validator's verdict.
/// </summary>
/// <remarks>
/// The lines are facts about the desktop; the verdict is what
/// <see cref="CommitPointValidator"/> would do with them. They are kept apart so
/// a short-circuit in the validator cannot hide a later condition from the
/// operator, and so a test can pin a named refusal without going through a
/// window.
/// </remarks>
public readonly record struct InputGuardReading(
    string Geometry,
    string Foreground,
    string Point,
    string Cloak,
    string Human,
    string Scale,
    bool Authorised,
    string? RefusalReason,
    TimeSpan ValidationDuration);

/// <summary>
/// Prints the five commit-point conditions against the live client window.
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/CONTROLLO_PERSONAGGIO_ROADMAP.md</c> X-P2. Observation only: nothing
/// is emitted. A refused verdict is a printed fact, not a command failure — the
/// operator is inspecting the desktop, not asking this to act.
/// </para>
/// <para>
/// A single snapshot stamps the geometry now and validates against that stamp,
/// so the geometry condition passes unless the window is unreadable. The three
/// real-client proofs need an interval: <c>--input-guards --watch &lt;seconds&gt;</c>
/// keeps the stamp taken at the start and revalidates, so moving the window,
/// covering the click point, or touching the mouse is a named refusal.
/// </para>
/// <para>
/// Thresholds stay where Claude set them. This only reports.
/// </para>
/// </remarks>
public static class InputGuardsProbe
{
    public const string NotWindowsReason = "input_guards_requires_windows";
    public const string WindowNotLocatedReason = "client_window_not_located";

    /// <summary>Console entry for <c>--input-guards</c>.</summary>
    public static int Run(int watchSeconds = 0)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine($"[REFUSED] {NotWindowsReason}");
            return 2;
        }

        return RunWindows(watchSeconds);
    }

    /// <summary>
    /// Evaluates the five conditions against a stated desktop, so the report is
    /// testable without a window.
    /// </summary>
    public static InputGuardReading Observe(
        ICommitEnvironment environment,
        IHumanInputMonitor human,
        in CommitRequest request,
        TimeSpan? courtesyWindow = null)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(human);

        var validator = new CommitPointValidator(environment, human, courtesyWindow);
        CommitDecision decision = validator.Validate(in request);

        IntPtr session = request.Stamp.Epoch.Window;
        GeometryEpoch live = environment.ReadEpoch(session);
        bool geometryUnchanged = GeometryEpoch.Unchanged(request.Stamp.Epoch, live, out string? geometryChanged);

        IntPtr foreground = environment.ForegroundWindow();
        IntPtr atPoint = environment.RootWindowFromPoint(request.ScreenX, request.ScreenY);
        bool? cloaked = environment.IsCloaked(session);
        TimeSpan? idle = human.SinceLastHumanInput;
        TimeSpan courtesy = validator.CourtesyWindow;

        return new InputGuardReading(
            Geometry: geometryUnchanged
                ? FormatKnown("unchanged", live)
                : $"changed:{geometryChanged}",
            Foreground: foreground == session
                ? FormatHandle("ours", foreground)
                : FormatHandle("other", foreground),
            Point: atPoint == session
                ? FormatHandle($"ours @{request.ScreenX},{request.ScreenY}", atPoint)
                : FormatHandle($"other @{request.ScreenX},{request.ScreenY}", atPoint),
            Cloak: cloaked switch
            {
                false => "visible",
                true => "cloaked",
                null => "unknown",
            },
            Human: FormatHuman(human, idle, courtesy),
            Scale: request.Scale.IsKnown && live.IsKnown && request.Scale.Dpi == live.Dpi
                ? string.Create(CultureInfo.InvariantCulture, $"known dpi={request.Scale.Dpi}")
                : !request.Scale.IsKnown
                    ? "unknown"
                    : string.Create(CultureInfo.InvariantCulture,
                        $"changed:{request.Scale.Dpi}_to_{live.Dpi}"),
            Authorised: decision.IsAuthorised,
            RefusalReason: decision.RefusalReason,
            ValidationDuration: decision.ValidationDuration);
    }

    /// <summary>The operator-facing block. Stable enough to assert against.</summary>
    public static string Format(in InputGuardReading reading)
    {
        string verdict = reading.Authorised
            ? string.Create(CultureInfo.InvariantCulture,
                $"authorised  validation={reading.ValidationDuration.TotalMilliseconds:F2}ms")
            : string.Create(CultureInfo.InvariantCulture,
                $"refused  {reading.RefusalReason}  validation={reading.ValidationDuration.TotalMilliseconds:F2}ms");

        return
            $"geometry:   {reading.Geometry}{Environment.NewLine}"
            + $"foreground: {reading.Foreground}{Environment.NewLine}"
            + $"point:      {reading.Point}{Environment.NewLine}"
            + $"cloak:      {reading.Cloak}{Environment.NewLine}"
            + $"human:      {reading.Human}{Environment.NewLine}"
            + $"scale:      {reading.Scale}{Environment.NewLine}"
            + $"verdict:    {verdict}";
    }

    [SupportedOSPlatform("windows")]
    private static int RunWindows(int watchSeconds)
    {
        if (!TryFindWindow(out ClientWindow window, out string? failure))
        {
            Console.WriteLine($"[REFUSED] {failure}");
            return 1;
        }

        using var monitor = new HumanInputMonitor();
        if (!monitor.TryStart(out string? watchFailure))
            Console.WriteLine($"[WARN] human monitor: {watchFailure}");

        var environment = new Win32CommitEnvironment();
        GeometryStamp stamp = GeometryStamp.Take(window.Handle, TimeProvider.System);
        int pointX = stamp.Epoch.ClientArea.X + stamp.Epoch.ClientArea.Width / 2;
        int pointY = stamp.Epoch.ClientArea.Y + stamp.Epoch.ClientArea.Height / 2;
        var request = new CommitRequest(stamp, pointX, pointY, stamp.Epoch.Shape);

        Console.WriteLine("=== input guards (commit point, observation only) ===");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"window: 0x{window.Handle.ToInt64():X} class={window.ClassName}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"stamp:  {stamp.Epoch.ClientArea.X},{stamp.Epoch.ClientArea.Y} {stamp.Epoch.ClientArea.Width}x{stamp.Epoch.ClientArea.Height} dpi={stamp.Epoch.Dpi} monitor=0x{stamp.Epoch.Monitor.ToInt64():X}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"point:  {pointX},{pointY} (client centre at stamp)"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"courtesy={CommitPointValidator.DefaultCourtesyWindow.TotalMilliseconds:F0}ms  max-latency={CommitPointValidator.DefaultMaxEmissionLatency.TotalMilliseconds:F0}ms"));
        Console.WriteLine();

        InputGuardReading first = Observe(environment, monitor, request);
        Console.WriteLine(Format(first));

        if (watchSeconds <= 0)
            return 0;

        Console.WriteLine();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"watching {watchSeconds}s against this stamp. Move the window, cover the point, or touch the mouse — each is a named refusal."));

        string? lastVerdict = first.RefusalReason ?? "authorised";
        DateTimeOffset until = DateTimeOffset.UtcNow.AddSeconds(watchSeconds);
        while (DateTimeOffset.UtcNow < until)
        {
            Thread.Sleep(100);
            InputGuardReading next = Observe(environment, monitor, request);
            string verdict = next.RefusalReason ?? "authorised";
            if (string.Equals(verdict, lastVerdict, StringComparison.Ordinal))
                continue;

            lastVerdict = verdict;
            Console.WriteLine();
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"-- changed at {DateTimeOffset.Now:HH:mm:ss.fff} --"));
            Console.WriteLine(Format(next));
        }

        return 0;
    }

    private static string FormatKnown(string state, in GeometryEpoch epoch) =>
        epoch.IsKnown
            ? string.Create(CultureInfo.InvariantCulture,
                $"{state} {epoch.ClientArea.X},{epoch.ClientArea.Y} {epoch.ClientArea.Width}x{epoch.ClientArea.Height} dpi={epoch.Dpi}")
            : $"{state}:unknown";

    private static string FormatHandle(string label, IntPtr handle) =>
        handle == IntPtr.Zero
            ? $"{label} none"
            : string.Create(CultureInfo.InvariantCulture, $"{label} 0x{handle.ToInt64():X}");

    private static string FormatHuman(IHumanInputMonitor human, TimeSpan? idle, TimeSpan courtesy)
    {
        if (!human.IsWatching)
            return "not-watching";

        if (idle is null)
            return "watching never-seen";

        string idleMs = idle.Value.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture);
        string courtesyMs = courtesy.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture);
        return idle.Value < courtesy
            ? $"recent {idleMs}ms_of_{courtesyMs}ms"
            : $"idle {idleMs}ms (courtesy {courtesyMs}ms)";
    }

    [SupportedOSPlatform("windows")]
    private static bool TryFindWindow(out ClientWindow window, out string? failureReason)
    {
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
                        failureReason = null;
                        return true;
                    }

                    failureReason = why;
                }
            }
        }

        window = null!;
        failureReason = $"{WindowNotLocatedReason}:{string.Join('/', RealClientConnector.DefaultProcessNames)}";
        return false;
    }
}
