// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Versione: 1.0 Beta
// LowLevel — Validazione d'ambiente reale del layer di input
// ============================================================================
//
// La suite --input-test certifica il CONTRATTO su un backend di registrazione:
// non prova che SendInput arrivi davvero alla coda di input del sistema. Questa
// sonda lo prova sull'ambiente reale, come --dxgi-probe fa per la cattura.
//
// Sicurezza della prova:
//  - la tastiera è validata con un hook low-level che OSSERVA il tasto iniettato
//    e lo INGHIOTTE, quindi nessuna applicazione lo riceve;
//  - si usa VK_F24, che nessuna applicazione normale interpreta;
//  - il mouse viene riportato esattamente dove stava;
//  - nessun client di gioco viene toccato: la sonda valida l'OS, non NosTale.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using NosAi.Runtime.Safety;

namespace NosAi.Runtime.LowLevel;

/// <summary>Outcome of one real-environment input validation.</summary>
public sealed record InputProbeResult(
    bool CursorReadWorks,
    bool MouseInjectionWorks,
    bool KeyboardInjectionWorks,
    int MousePointsVerified,
    int MouseMaxErrorPixels,
    string? Failure)
{
    public bool Success => CursorReadWorks && MouseInjectionWorks && KeyboardInjectionWorks && Failure is null;
}

/// <summary>
/// Validates that the input backend really reaches the OS input queue.
/// </summary>
/// <remarks>
/// This is real-environment work and cannot run headlessly: it needs an
/// interactive desktop session, exactly like Desktop Duplication. A failure here
/// means the runtime would silently do nothing when it believes it is acting.
/// </remarks>
public static class InputEnvironmentProbe
{
    /// <summary>F24: present on the virtual-key map, produced by no ordinary keyboard.</summary>
    public const ushort ProbeVirtualKey = 0x87;

    private const int WH_KEYBOARD_LL = 13;
    private const int HC_ACTION = 0;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_QUIT = 0x0012;
    private const uint LLKHF_INJECTED = 0x00000010;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookExW(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessageW(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

    /// <summary>
    /// Runs the validation against the real OS input queue.
    /// </summary>
    /// <remarks>
    /// The caller must pass a backend authorised for live input: the probe does
    /// not grant itself permission, it validates the path the runtime would use.
    /// </remarks>
    public static InputProbeResult Run(IInputBackend backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        if (!OperatingSystem.IsWindows())
            return new InputProbeResult(false, false, false, 0, 0, "probe_requires_windows");

        if (!backend.TryGetCursorPosition(out int originalX, out int originalY))
            return new InputProbeResult(false, false, false, 0, 0, "cursor_read_failed");

        bool mouseOk;
        int verified = 0, maxError = 0;
        try
        {
            mouseOk = ProbeMouse(backend, out verified, out maxError, out string? mouseFailure);
            if (!mouseOk)
                return new InputProbeResult(true, false, false, verified, maxError, mouseFailure ?? "mouse_injection_failed");
        }
        finally
        {
            // Always put the cursor back where the operator left it.
            backend.MoveAbsolute(originalX, originalY);
        }

        bool keyboardOk = ProbeKeyboard(backend, out string? keyboardFailure);
        return new InputProbeResult(true, true, keyboardOk, verified, maxError,
            keyboardOk ? null : keyboardFailure ?? "keyboard_injection_failed");
    }

    private static bool ProbeMouse(IInputBackend backend, out int verified, out int maxError, out string? failure)
    {
        verified = 0;
        maxError = 0;
        failure = null;

        int originX = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int originY = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        if (width <= 0 || height <= 0)
        {
            failure = "virtual_desktop_metrics_unavailable";
            return false;
        }

        // Points well inside the desktop, away from edges where the OS clamps.
        (int X, int Y)[] targets =
        {
            (originX + width / 4, originY + height / 4),
            (originX + width / 2, originY + height / 2),
            (originX + (3 * width) / 4, originY + (3 * height) / 4),
        };

        foreach ((int x, int y) in targets)
        {
            if (!backend.MoveAbsolute(x, y))
            {
                failure = "move_absolute_rejected";
                return false;
            }

            // The compositor applies the move asynchronously; poll briefly rather
            // than assume it landed instantly.
            int observedX = 0, observedY = 0, error = int.MaxValue;
            for (int attempt = 0; attempt < 50 && error > 2; attempt++)
            {
                Thread.Sleep(4);
                if (!backend.TryGetCursorPosition(out observedX, out observedY)) continue;
                error = Math.Max(Math.Abs(observedX - x), Math.Abs(observedY - y));
            }

            if (error > 2)
            {
                failure = $"cursor_did_not_reach_target:wanted={x},{y} got={observedX},{observedY}";
                return false;
            }
            maxError = Math.Max(maxError, error);
            verified++;
        }
        return true;
    }

    private static bool ProbeKeyboard(IInputBackend backend, out string? failure)
    {
        failure = null;
        bool observed = false;
        bool wasInjected = false;
        IntPtr hook = IntPtr.Zero;
        uint hookThreadId = 0;
        using var hookReady = new ManualResetEventSlim(false);
        using var keySeen = new ManualResetEventSlim(false);
        Exception? hookFailure = null;

        // The hook must live on a thread with a message pump: a low-level keyboard
        // hook is only called while its owning thread pumps messages.
        HookProc proc = (nCode, wParam, lParam) =>
        {
            if (nCode == HC_ACTION && (int)wParam == WM_KEYDOWN)
            {
                var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                if (info.vkCode == ProbeVirtualKey)
                {
                    observed = true;
                    wasInjected = (info.flags & LLKHF_INJECTED) != 0;
                    keySeen.Set();
                    // Swallow it: the proof is that the hook saw it, and no
                    // application has any business receiving the probe key.
                    return 1;
                }
            }
            return CallNextHookEx(hook, nCode, wParam, lParam);
        };

        var hookThread = new Thread(() =>
        {
            try
            {
                hookThreadId = GetCurrentThreadId();
                hook = SetWindowsHookExW(WH_KEYBOARD_LL, proc, IntPtr.Zero, 0);
                if (hook == IntPtr.Zero)
                {
                    hookFailure = new InvalidOperationException(
                        $"SetWindowsHookEx failed (win32={Marshal.GetLastWin32Error()})");
                    hookReady.Set();
                    return;
                }
                hookReady.Set();

                while (GetMessageW(out MSG message, IntPtr.Zero, 0, 0) > 0)
                {
                    if (message.message == WM_QUIT) break;
                }
            }
            catch (Exception ex) { hookFailure = ex; hookReady.Set(); }
            finally { if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook); }
        })
        { IsBackground = true, Name = "NosAi.InputProbe.Hook" };

        hookThread.SetApartmentState(ApartmentState.STA);
        hookThread.Start();
        hookReady.Wait(TimeSpan.FromSeconds(5));

        if (hookFailure is not null || hook == IntPtr.Zero)
        {
            failure = $"keyboard_hook_unavailable:{hookFailure?.Message ?? "no_handle"}";
            return false;
        }

        try
        {
            if (!backend.KeyPress(ProbeVirtualKey, pressDurationMs: 10))
            {
                failure = "key_press_rejected_by_backend";
                return false;
            }
            keySeen.Wait(TimeSpan.FromSeconds(3));
        }
        finally
        {
            PostThreadMessageW(hookThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            hookThread.Join(TimeSpan.FromSeconds(3));
        }

        if (!observed)
        {
            failure = "injected_key_never_reached_the_input_queue";
            return false;
        }
        if (!wasInjected)
        {
            // The hook saw the key but the OS did not flag it as injected: that
            // would mean a real keyboard produced it, not us.
            failure = "observed_key_was_not_flagged_as_injected";
            return false;
        }
        return true;
    }

    /// <summary>
    /// Console entry point for the operator command. Builds a backend explicitly
    /// authorised for live input, because validating the path requires using it.
    /// </summary>
    public static int RunConsoleProbe()
    {
        Console.WriteLine("=== Input layer real-environment probe ===");
        Console.WriteLine("Injects into this desktop only. The probe key (F24) is swallowed by a");
        Console.WriteLine("low-level hook, so no application receives it, and the cursor is restored.");

        // The runtime's SafeDefault keeps live input off. Validating the path
        // requires an explicit, local, temporary authorisation: it is scoped to
        // this probe object and never handed to the game adapter.
        var policy = RuntimeSafetyPolicy.SafeDefault with { LiveInputEnabled = true };
        var backend = new GatedInputBackend(new Win32InputBackend(), policy);

        var stopwatch = Stopwatch.StartNew();
        InputProbeResult result = Run(backend);
        stopwatch.Stop();

        Console.WriteLine($"[{(result.CursorReadWorks ? "OK" : "FAIL")}] cursor read");
        Console.WriteLine($"[{(result.MouseInjectionWorks ? "OK" : "FAIL")}] mouse absolute positioning " +
                          $"({result.MousePointsVerified} points, max error {result.MouseMaxErrorPixels}px)");
        Console.WriteLine($"[{(result.KeyboardInjectionWorks ? "OK" : "FAIL")}] keyboard injection reached the OS input queue");
        if (result.Failure is not null) Console.WriteLine($"      reason: {result.Failure}");

        Console.WriteLine(result.Success
            ? $"=== Input probe passed in {stopwatch.ElapsedMilliseconds} ms: SendInput really reaches this desktop. ==="
            : "=== Input probe FAILED: the runtime would believe it is acting while doing nothing. ===");
        return result.Success ? 0 : 1;
    }
}
