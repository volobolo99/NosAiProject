// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// LowLevel — OS input backend: keyboard and mouse behind an authorised boundary
// ============================================================================
//
// The one primitive that really touches the desktop lives here. Authorisation is
// NOT the caller's responsibility: GatedInputBackend wraps this backend and
// refuses every injection until the policy allows it, so a consumer that skips
// the adapter does not skip the Safety Gate (ADR-0003).

using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;

namespace NosAi.Runtime.LowLevel;

/// <summary>Mouse buttons the backend can actuate.</summary>
public enum MouseButton : byte
{
    Left = 0,
    Right = 1,
    Middle = 2,
}

/// <summary>
/// Releasing what was already pressed, as a capability separate from pressing.
/// </summary>
/// <remarks>
/// <para>
/// Additive rather than folded into <see cref="IInputBackend"/>, and the split is the
/// safety property. A backend that can only press cannot be asked to undo; a caller
/// holding this can <b>only</b> release, so handing it to the abort path opens no way
/// to actuate anything new. That is what lets the release bypass the policy gate
/// without the bypass being a hole: the worst a compromised release path can do is
/// let go of a key.
/// </para>
/// <para>
/// The press primitives balance themselves — <see cref="IInputBackend.Click"/> and
/// <see cref="IInputBackend.KeyPress"/> both send their own release — but only when
/// they run to completion. A release that fails is reported and then forgotten, and a
/// key that stays down survives the process that pressed it.
/// </para>
/// </remarks>
public interface IInputReleaseBackend
{
    /// <summary>Sends a button-up. Harmless when the button was not down.</summary>
    bool ReleaseMouseButton(MouseButton button);

    /// <summary>Sends a key-up. Harmless when the key was not down.</summary>
    bool ReleaseKey(ushort virtualKey);
}

/// <summary>
/// OS-level input backend. Implementations are primitives: they perform the
/// injection and report success, and never decide whether it was allowed.
/// </summary>
public interface IInputBackend
{
    /// <summary>True when this backend can actually reach the OS input queue.</summary>
    bool IsLive { get; }

    /// <summary>Reads the real cursor position; false when it cannot be determined.</summary>
    bool TryGetCursorPosition(out int x, out int y);

    /// <summary>Moves the cursor by a relative delta.</summary>
    bool MoveRelative(int dx, int dy);

    /// <summary>Moves the cursor to an absolute virtual-desktop pixel.</summary>
    bool MoveAbsolute(int x, int y);

    bool Click(MouseButton button, int delayBetweenDownUpMs = 45);

    /// <summary>Presses a virtual key, optionally with modifiers held around it.</summary>
    bool KeyPress(ushort virtualKey, int pressDurationMs = 80, ReadOnlySpan<ushort> modifiers = default);

    bool ScrollWheel(int detents);
}

/// <summary>Real Windows input injection through <c>SendInput</c>.</summary>
public sealed class Win32InputBackend : IInputBackend, IInputReleaseBackend
{
    /// <summary>Reported when a point does not exist on the virtual desktop.</summary>
    public const string PointOffVirtualDesktopReason = "point_outside_virtual_desktop";

    /// <summary>Reported when the desktop metrics could not be read.</summary>
    public const string DesktopMetricsUnreadableReason = "virtual_desktop_metrics_unreadable";

    /// <summary>
    /// Why the last call returned false, or null. Diagnostic only.
    /// </summary>
    /// <remarks>
    /// <see cref="IInputBackend"/> returns a bare bool, and widening it would touch
    /// every implementer for the sake of one refusal. The reason is recorded here
    /// instead so a refusal is still nameable in a report.
    /// </remarks>
    public string? LastFailureReason { get; private set; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] inputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion u; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT
    {
        public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT
    {
        public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo;
    }

    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;

    private const uint MouseMove = 0x0001;
    private const uint MouseAbsolute = 0x8000;
    private const uint MouseVirtualDesk = 0x4000;
    private const uint MouseLeftDown = 0x0002, MouseLeftUp = 0x0004;
    private const uint MouseRightDown = 0x0008, MouseRightUp = 0x0010;
    private const uint MouseMiddleDown = 0x0020, MouseMiddleUp = 0x0040;
    private const uint MouseWheel = 0x0800;
    private const int WheelDelta = 120;

    private const uint KeyUp = 0x0002;

    // Virtual-desktop metrics, needed to normalise absolute coordinates.
    private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;

    public bool IsLive => true;

    public bool TryGetCursorPosition(out int x, out int y)
    {
        if (GetCursorPos(out POINT point))
        {
            x = point.X;
            y = point.Y;
            return true;
        }
        x = 0;
        y = 0;
        return false;
    }

    public bool MoveRelative(int dx, int dy)
        => Send(Mouse(dx, dy, MouseMove));

    public bool MoveAbsolute(int x, int y)
    {
        // SendInput takes absolute coordinates normalised to 0..65535 across the
        // whole virtual desktop, not raw pixels: a multi-monitor setup lands in
        // the wrong place without this mapping.
        int originX = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int originY = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int height = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        if (width <= 1 || height <= 1)
        {
            LastFailureReason = DesktopMetricsUnreadableReason;
            return false;
        }

        // A point off the virtual desktop is refused, not clamped.
        //
        // Clamping was the last line of this method and it was the one place on the
        // whole path where a coordinate error became an act instead of a refusal: a
        // point that does not exist was carried to the nearest edge and clicked there,
        // which is a real click at a place nobody chose. It is the same mistake the
        // project forbids everywhere else — unknown does not become a plausible value,
        // and a source that fails says so. The guards upstream refuse points outside
        // the client area, so this does not bite today; a last line of defence that
        // silently corrects is worse than none, because it removes the evidence that
        // the earlier guards were wrong.
        if (x < originX || y < originY || x >= originX + width || y >= originY + height)
        {
            LastFailureReason = string.Create(CultureInfo.InvariantCulture,
                $"{PointOffVirtualDesktopReason}:{x},{y}_outside_{originX},{originY}_{width}x{height}");
            return false;
        }

        int normalisedX = (int)Math.Round((x - originX) * 65535.0 / (width - 1));
        int normalisedY = (int)Math.Round((y - originY) * 65535.0 / (height - 1));

        LastFailureReason = null;
        return Send(Mouse(normalisedX, normalisedY,
            MouseMove | MouseAbsolute | MouseVirtualDesk));
    }

    public bool Click(MouseButton button, int delayBetweenDownUpMs = 45)
    {
        if (delayBetweenDownUpMs < 0) throw new ArgumentOutOfRangeException(nameof(delayBetweenDownUpMs));
        var (downFlag, upFlag) = button switch
        {
            MouseButton.Left => (MouseLeftDown, MouseLeftUp),
            MouseButton.Right => (MouseRightDown, MouseRightUp),
            MouseButton.Middle => (MouseMiddleDown, MouseMiddleUp),
            _ => throw new ArgumentOutOfRangeException(nameof(button)),
        };

        bool down = Send(Mouse(0, 0, downFlag));
        Thread.Sleep(delayBetweenDownUpMs);
        bool up = Send(Mouse(0, 0, upFlag));
        // The release is always attempted, even if the press failed: leaving a
        // button logically held down is worse than a failed click.
        return down && up;
    }

    public bool ScrollWheel(int detents)
        => Send(new INPUT
        {
            type = InputMouse,
            u = new InputUnion { mi = new MOUSEINPUT { mouseData = unchecked((uint)(detents * WheelDelta)), dwFlags = MouseWheel } },
        });

    public bool KeyPress(ushort virtualKey, int pressDurationMs = 80, ReadOnlySpan<ushort> modifiers = default)
    {
        if (pressDurationMs < 0) throw new ArgumentOutOfRangeException(nameof(pressDurationMs));

        bool ok = true;
        int held = 0;
        // Modifiers go down first and come up in reverse order, so a failure
        // part-way through still releases exactly what was pressed.
        foreach (ushort modifier in modifiers)
        {
            if (!Send(Key(modifier, 0))) { ok = false; break; }
            held++;
        }

        if (ok)
        {
            ok &= Send(Key(virtualKey, 0));
            Thread.Sleep(pressDurationMs);
            ok &= Send(Key(virtualKey, KeyUp));
        }

        for (int i = held - 1; i >= 0; i--) ok &= Send(Key(modifiers[i], KeyUp));
        return ok;
    }

    /// <inheritdoc />
    public bool ReleaseMouseButton(MouseButton button)
    {
        uint flag = button switch
        {
            MouseButton.Left => MouseLeftUp,
            MouseButton.Right => MouseRightUp,
            MouseButton.Middle => MouseMiddleUp,
            _ => throw new ArgumentOutOfRangeException(nameof(button)),
        };

        return Send(Mouse(0, 0, flag));
    }

    /// <inheritdoc />
    public bool ReleaseKey(ushort virtualKey) => Send(Key(virtualKey, KeyUp));

    private static INPUT Mouse(int dx, int dy, uint flags) => new()
    {
        type = InputMouse,
        u = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = flags } },
    };

    private static INPUT Key(ushort virtualKey, uint flags) => new()
    {
        type = InputKeyboard,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = virtualKey, dwFlags = flags } },
    };

    private static bool Send(INPUT input)
        => SendInput(1, [input], Marshal.SizeOf<INPUT>()) == 1;
}
