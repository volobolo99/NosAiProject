// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Versione: 1.0 Beta
// LowLevel — Backend di input OS: tastiera e mouse dietro un confine autorizzato
// ============================================================================
//
// Qui vive l'unica primitiva che tocca davvero il desktop. L'autorizzazione NON
// è responsabilità del chiamante: GatedInputBackend avvolge questo backend e
// rifiuta ogni iniezione finché la policy non la abilita, così un consumatore
// che salta l'adapter non aggira il Safety Gate (ADR-0003).

using System;
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
public sealed class Win32InputBackend : IInputBackend
{
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
        if (width <= 0 || height <= 0) return false;

        int normalisedX = (int)Math.Round((x - originX) * 65535.0 / (width - 1));
        int normalisedY = (int)Math.Round((y - originY) * 65535.0 / (height - 1));
        return Send(Mouse(Math.Clamp(normalisedX, 0, 65535), Math.Clamp(normalisedY, 0, 65535),
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
