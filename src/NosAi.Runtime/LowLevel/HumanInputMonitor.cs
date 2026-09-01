using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace NosAi.Runtime.LowLevel;

/// <summary>When the operator last touched the mouse or the keyboard.</summary>
/// <remarks>
/// A separate contract from the monitor so the commit point can be tested against a
/// stated answer instead of a real desktop, and so a runtime with no monitor running
/// is a thing that can be expressed rather than a null nobody checked.
/// </remarks>
public interface IHumanInputMonitor
{
    /// <summary>True while the hooks are installed and reporting.</summary>
    bool IsWatching { get; }

    /// <summary>
    /// How long since the last event that came from a person, or null when that is
    /// not known.
    /// </summary>
    /// <remarks>
    /// Null is the honest answer before the first human event of a session and
    /// whenever the monitor is not running. It is <b>not</b> "a long time": a caller
    /// that read it as "nobody is here" would hand the runtime the mouse on exactly
    /// the evidence it does not have.
    /// </remarks>
    TimeSpan? SinceLastHumanInput { get; }

    /// <summary>Human events seen. Diagnostic.</summary>
    long HumanEventCount { get; }

    /// <summary>Injected events seen and discarded. Diagnostic.</summary>
    long InjectedEventCount { get; }
}

/// <summary>
/// Low-level mouse and keyboard hooks that keep the time of the last event that came
/// from a person, discarding the ones the OS marks as injected.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not <c>GetLastInputInfo</c>.</b> It counts synthetic input as input. Every
/// <c>SendInput</c> this runtime issues moves the value forward, so a runtime that
/// asked it "has a person touched anything recently?" would be told yes, by its own
/// hand, every time it acted — and the busier it got the more certain the wrong
/// answer would look. It cannot separate the operator's hand from ours because it was
/// never built to: it answers "was there input", and the question here is "was there a
/// <i>person</i>". One low-level hook parameter — the injected flag — is the whole
/// difference, and only the hook has it.
/// </para>
/// <para>
/// <b>The hooks need a message loop.</b> <c>WH_MOUSE_LL</c> and <c>WH_KEYBOARD_LL</c>
/// are dispatched to the thread that installed them, and a thread that does not pump
/// messages is silently dropped from the chain after <c>LowLevelHooksTimeout</c>. So
/// this owns a dedicated thread whose only job is the loop; installing on a worker or
/// on the main runtime thread would work in a test and go quiet under load, which is
/// the worst shape a safety input can have.
/// </para>
/// <para>
/// <b>The callback does almost nothing, on purpose.</b> It runs on every mouse move
/// on the desktop, and a slow one gets the hook removed by the OS. It writes one
/// timestamp and two counters and returns; there is no allocation, no lock and no
/// logging on that path.
/// </para>
/// <para>
/// <b>What it deliberately does not do.</b> It never swallows an event. Every hook
/// call chains on to <c>CallNextHookEx</c>: this watches the operator, it does not
/// take input away from them, and a monitor that could drop a keystroke would be a
/// worse hazard than the one it guards against.
/// </para>
/// <para>
/// <b>Known limit, stated rather than papered over.</b> The injected flag says "some
/// process injected this", not "this was not a person". Remote-desktop sessions, some
/// touchpad and tablet drivers, and virtual-machine guest tools deliver genuine human
/// action with the flag set, and this would read those as ours and see an idle
/// operator. Where that is the environment, the courtesy window is not protection and
/// the operator has to be told so — it is not something this class can detect about
/// itself.
/// </para>
/// </remarks>
public sealed class HumanInputMonitor : IHumanInputMonitor, IDisposable
{
    private readonly object _lifecycle = new();

    // Kept as fields so the GC cannot collect the delegates the OS holds pointers to.
    // A collected hook callback is a process-wide crash on the next mouse move.
    private HookProc? _mouseProc;
    private HookProc? _keyboardProc;

    private IntPtr _mouseHook;
    private IntPtr _keyboardHook;
    private Thread? _pump;
    private uint _pumpThreadId;
    private volatile bool _watching;
    private volatile bool _disposed;

    private long _lastHumanTimestamp;
    private long _humanEvents;
    private long _injectedEvents;

    /// <inheritdoc />
    public bool IsWatching => _watching;

    /// <inheritdoc />
    public TimeSpan? SinceLastHumanInput
    {
        get
        {
            long stamp = Interlocked.Read(ref _lastHumanTimestamp);
            if (stamp == 0)
                return null;

            return Stopwatch.GetElapsedTime(stamp);
        }
    }

    /// <inheritdoc />
    public long HumanEventCount => Interlocked.Read(ref _humanEvents);

    /// <inheritdoc />
    public long InjectedEventCount => Interlocked.Read(ref _injectedEvents);

    /// <summary>
    /// Installs the hooks on a dedicated pumping thread and waits for them to be up.
    /// </summary>
    /// <returns>False with a reason when the hooks could not be installed.</returns>
    /// <remarks>
    /// Synchronous on purpose: a caller that started this and immediately asked
    /// whether a person was present would otherwise be told "no events yet" by a
    /// monitor that had not begun watching, which is the same wrong answer as an
    /// idle operator.
    /// </remarks>
    public bool TryStart(out string? failureReason)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!OperatingSystem.IsWindows())
        {
            failureReason = "human_input_monitor_requires_windows";
            return false;
        }

        lock (_lifecycle)
        {
            if (_watching)
            {
                failureReason = null;
                return true;
            }

            using var ready = new ManualResetEventSlim(false);
            string? installFailure = null;

            _pump = new Thread(() => PumpWindows(ready, ref installFailure))
            {
                IsBackground = true,
                Name = "nosai-human-input-monitor",
            };

            _pump.Start();

            if (!ready.Wait(TimeSpan.FromSeconds(5)))
            {
                failureReason = "human_input_monitor_start_timed_out";
                return false;
            }

            if (installFailure is not null)
            {
                failureReason = installFailure;
                return false;
            }

            _watching = true;
            failureReason = null;
            return true;
        }
    }

    [SupportedOSPlatform("windows")]
    private void PumpWindows(ManualResetEventSlim ready, ref string? installFailure)
    {
        try
        {
            _pumpThreadId = GetCurrentThreadId();

            _mouseProc = OnMouse;
            _keyboardProc = OnKeyboard;

            _mouseHook = SetWindowsHookExW(WhMouseLowLevel, _mouseProc, IntPtr.Zero, 0);
            _keyboardHook = SetWindowsHookExW(WhKeyboardLowLevel, _keyboardProc, IntPtr.Zero, 0);

            if (_mouseHook == IntPtr.Zero || _keyboardHook == IntPtr.Zero)
            {
                installFailure = $"human_input_hooks_not_installed:{Marshal.GetLastWin32Error()}";
                return;
            }
        }
        finally
        {
            ready.Set();
        }

        // The loop is the point: without it the OS drops these hooks from the chain.
        while (GetMessageW(out Msg message, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref message);
            DispatchMessageW(ref message);
        }
    }

    [SupportedOSPlatform("windows")]
    private IntPtr OnMouse(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var data = Marshal.PtrToStructure<MsllHookStruct>(lParam);
            Record((data.flags & LlmhfInjected) != 0);
        }

        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    [SupportedOSPlatform("windows")]
    private IntPtr OnKeyboard(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var data = Marshal.PtrToStructure<KbdllHookStruct>(lParam);
            Record((data.flags & LlkhfInjected) != 0);
        }

        return CallNextHookEx(IntPtr.Zero, code, wParam, lParam);
    }

    private void Record(bool injected)
    {
        if (injected)
        {
            Interlocked.Increment(ref _injectedEvents);
            return;
        }

        Interlocked.Increment(ref _humanEvents);
        Interlocked.Exchange(ref _lastHumanTimestamp, Stopwatch.GetTimestamp());
    }

    public void Dispose()
    {
        lock (_lifecycle)
        {
            if (_disposed)
                return;

            _disposed = true;
            _watching = false;

            if (OperatingSystem.IsWindows())
                StopWindows();

            _mouseProc = null;
            _keyboardProc = null;
        }
    }

    [SupportedOSPlatform("windows")]
    private void StopWindows()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        if (_pumpThreadId != 0)
        {
            PostThreadMessageW(_pumpThreadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
            _pump?.Join(TimeSpan.FromSeconds(2));
            _pumpThreadId = 0;
        }

        _pump = null;
    }

    private const int WhKeyboardLowLevel = 13;
    private const int WhMouseLowLevel = 14;
    private const uint WmQuit = 0x0012;

    /// <summary>The bit the OS sets on an event some process injected.</summary>
    private const uint LlmhfInjected = 0x00000001;
    private const uint LlkhfInjected = 0x00000010;

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MsllHookStruct
    {
        public int x, y;
        public uint mouseData, flags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdllHookStruct
    {
        public uint vkCode, scanCode, flags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam, lParam;
        public uint time;
        public int ptX, ptY;
    }

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookExW(int idHook, HookProc callback, IntPtr module, uint threadId);

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    private static extern int GetMessageW(out Msg message, IntPtr window, uint filterMin, uint filterMax);

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg message);

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref Msg message);

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    private static extern bool PostThreadMessageW(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}

/// <summary>A monitor that is not running and says so.</summary>
/// <remarks>
/// The stand-in for a runtime with no hooks installed. It reports null rather than a
/// long idle time, so the commit point refuses instead of being told nobody is there
/// by something that is not looking.
/// </remarks>
public sealed class NotWatchingHumanInput : IHumanInputMonitor
{
    public static NotWatchingHumanInput Instance { get; } = new();

    public bool IsWatching => false;

    public TimeSpan? SinceLastHumanInput => null;

    public long HumanEventCount => 0;

    public long InjectedEventCount => 0;
}
