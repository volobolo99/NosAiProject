using System.Globalization;
using System.Runtime.Versioning;
using NosAi.LiveIntegration;
using NosAi.Runtime.Orchestration;
using NosAi.Runtime.Perception;
using NosAi.Runtime.Safety;
using NosAi.Runtime.Security;

namespace NosAi.Runtime.LowLevel;

/// <summary>
/// One reading of the session actuation verdict, as the operator's command prints it.
/// </summary>
/// <param name="Window">
/// PID and handle of the client window, or the named reason there is none.
/// </param>
/// <param name="RuntimeIntegrity"><see cref="IntegrityLevel.Name"/> of this process.</param>
/// <param name="ClientIntegrity"><see cref="IntegrityLevel.Name"/> of the client process.</param>
/// <param name="IsActuating">Whether the decision level may be offered actuation.</param>
/// <param name="RefusalReason">
/// <see cref="SessionAuthorityVerdict.RefusalReason"/> when not actuating; null when it is.
/// </param>
/// <param name="IsTerminal">Whether asking again cannot change the answer.</param>
/// <param name="PointerErrorPixels">
/// How far the probe pointer landed from where it was sent; -1 when it did not run.
/// </param>
/// <param name="Age">How long the standing verdict has been up, or <c>never</c>.</param>
public readonly record struct InputAuthorityReading(
    string Window,
    string RuntimeIntegrity,
    string ClientIntegrity,
    bool IsActuating,
    string? RefusalReason,
    bool IsTerminal,
    int PointerErrorPixels,
    string Age);

/// <summary>
/// Prints the session actuation verdict against the live client.
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/CONTROLLO_PERSONAGGIO_ROADMAP.md</c> X-P3. A verification command: it
/// takes a verdict, including the harmless pointer probe when the session allows
/// it, and the process exits non-zero when the session is not actuating.
/// </para>
/// <para>
/// There is no foreground observer in this runtime. <see cref="SessionActuationAuthority.NoteForegroundRestored"/>
/// is therefore not hooked to any event: the only trigger that retakes a verdict
/// after the operator brings the client forward is <c>--input-authority --watch</c>,
/// which calls <see cref="SessionActuationAuthority.EnsureVerified"/> on each
/// repeat. A timer that called <see cref="SessionActuationAuthority.Verify"/> on
/// its own would move the operator's pointer without anyone asking.
/// </para>
/// <para>
/// Thresholds and refusal criteria stay where they were set. This only reports,
/// and the watch loop only asks.
/// </para>
/// </remarks>
public static class InputAuthorityProbe
{
    /// <summary>Reported off Windows, where there is no session window to bind.</summary>
    public const string NotWindowsReason = "input_authority_requires_windows";

    /// <summary>Reported when no client window could be located.</summary>
    public const string WindowNotLocatedReason = "client_window_not_located";

    /// <summary>Console entry for <c>--input-authority</c>.</summary>
    /// <param name="watchRepeats">
    /// How many times to take the verdict. Zero or one is a single shot; more
    /// than one waits one second between calls to
    /// <see cref="SessionActuationAuthority.EnsureVerified"/>.
    /// </param>
    public static int Run(int watchRepeats = 0)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine($"[REFUSED] {NotWindowsReason}");
            return 2;
        }

        return RunWindows(watchRepeats);
    }

    /// <summary>
    /// Reads the standing verdict, so the report is testable without a window.
    /// </summary>
    public static InputAuthorityReading Observe(
        SessionActuationAuthority authority,
        string? windowDescription = null,
        TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(authority);

        TimeProvider time = clock ?? TimeProvider.System;
        SessionAuthorityVerdict verdict = authority.Current;
        string? refusal = authority.CurrentRefusal();
        bool actuating = refusal is null;

        string window = windowDescription
            ?? FormatWindow(verdict.ClientProcessId, verdict.Window);

        string age = !verdict.WasProbed
            ? "never"
            : string.Create(CultureInfo.InvariantCulture,
                $"{Math.Max(0, (time.GetUtcNow() - verdict.TakenAtUtc).TotalMilliseconds):F0}ms");

        return new InputAuthorityReading(
            Window: window,
            RuntimeIntegrity: verdict.Runtime.Name,
            ClientIntegrity: verdict.Client.Name,
            IsActuating: actuating,
            RefusalReason: actuating ? null : refusal,
            IsTerminal: verdict.IsTerminal,
            PointerErrorPixels: verdict.PointerErrorPixels,
            Age: age);
    }

    /// <summary>The operator-facing block. Stable enough to assert against.</summary>
    public static string Format(in InputAuthorityReading reading)
    {
        string session = reading.IsActuating
            ? string.Create(CultureInfo.InvariantCulture,
                $"actuating  terminal=false  pointer-error={reading.PointerErrorPixels}px  age={reading.Age}")
            : string.Create(CultureInfo.InvariantCulture,
                $"not-actuating  {reading.RefusalReason}  terminal={(reading.IsTerminal ? "true" : "false")}  pointer-error={reading.PointerErrorPixels}px  age={reading.Age}");

        return
            $"window:    {reading.Window}{Environment.NewLine}"
            + $"runtime:   {reading.RuntimeIntegrity}{Environment.NewLine}"
            + $"client:    {reading.ClientIntegrity}{Environment.NewLine}"
            + $"session:   {session}";
    }

    /// <summary>PID and handle of a located window, or the no-session reason.</summary>
    public static string FormatWindow(int processId, IntPtr handle) =>
        handle == IntPtr.Zero
            ? SessionActuationAuthority.NoSessionReason
            : string.Create(CultureInfo.InvariantCulture,
                $"pid={processId} handle=0x{handle.ToInt64():X}");

    [SupportedOSPlatform("windows")]
    private static int RunWindows(int watchRepeats)
    {
        RuntimeComponents components = RuntimeComposition.CreateSafe();
        if (components.SessionAuthority is not { } authority)
        {
            Console.WriteLine($"[REFUSED] {SessionActuationAuthority.NoSessionReason}");
            return 1;
        }

        if (components.HumanInput is HumanInputMonitor monitor
            && !monitor.TryStart(out string? watchFailure))
        {
            Console.WriteLine($"[WARN] human monitor: {watchFailure}");
        }

        // The probe is verification: it has to be allowed to emit the harmless
        // act, or the only verdict it could ever take would be
        // authority_live_input_not_armed. The switch is this process's, named,
        // and dies with it.
        AuthorizationDecision armed = components.Safety.Set(
            SecurityPrincipal.Operator,
            SafetySwitch.LiveInput,
            true,
            "input_authority_probe");
        if (!armed.Allowed)
            Console.WriteLine($"[WARN] live input not armed: {armed.Reason}");

        Console.WriteLine("=== input authority (session actuation verdict) ===");

        string windowDescription;
        if (!TryFindWindow(out ClientWindow window, out int pid, out string? failure))
        {
            windowDescription = failure ?? WindowNotLocatedReason;
            Console.WriteLine(windowDescription);
            InputAuthorityReading missing = Observe(authority, windowDescription);
            Console.WriteLine(Format(missing));
            return 1;
        }

        windowDescription = FormatWindow(pid, window.Handle);
        authority.BeginSession(window.Handle, pid);

        int repeats = watchRepeats <= 0 ? 1 : watchRepeats;
        InputAuthorityReading last = default;
        for (int i = 0; i < repeats; i++)
        {
            if (i > 0)
                Thread.Sleep(1000);

            authority.EnsureVerified();
            last = Observe(authority, windowDescription);
            if (i > 0)
            {
                Console.WriteLine();
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"-- repeat {i + 1}/{repeats} at {DateTimeOffset.Now:HH:mm:ss.fff} --"));
            }

            Console.WriteLine(Format(last));
        }

        return last.IsActuating ? 0 : 1;
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
        failureReason = $"{WindowNotLocatedReason}:{string.Join('/', RealClientConnector.DefaultProcessNames)}";
        return false;
    }
}
