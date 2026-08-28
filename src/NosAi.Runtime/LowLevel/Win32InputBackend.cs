using System.Runtime.InteropServices;

namespace NosAi.Runtime.LowLevel;

/// <summary>
/// OS-level input backend. This implementation is intentionally exposed as a
/// primitive backend only; authorization belongs to the higher-level Safety Gate.
/// </summary>
public interface IInputBackend
{
    bool MoveRelative(int dx, int dy);
    bool ClickLeft(int delayBetweenDownUpMs = 45);
    bool KeyPress(ushort virtualKey, int pressDurationMs = 80);
}

public sealed class Win32InputBackend : IInputBackend
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] inputs, int cbSize);

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
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint KeyUp = 0x0002;

    public bool MoveRelative(int dx, int dy)
        => Send(new INPUT { type = InputMouse, u = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = MouseMove } } });

    public bool ClickLeft(int delayBetweenDownUpMs = 45)
    {
        if (delayBetweenDownUpMs < 0) throw new ArgumentOutOfRangeException(nameof(delayBetweenDownUpMs));
        var down = Send(new INPUT { type = InputMouse, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = MouseLeftDown } } });
        Thread.Sleep(delayBetweenDownUpMs);
        var up = Send(new INPUT { type = InputMouse, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = MouseLeftUp } } });
        return down && up;
    }

    public bool KeyPress(ushort virtualKey, int pressDurationMs = 80)
    {
        if (pressDurationMs < 0) throw new ArgumentOutOfRangeException(nameof(pressDurationMs));
        var down = Send(new INPUT { type = InputKeyboard, u = new InputUnion { ki = new KEYBDINPUT { wVk = virtualKey } } });
        Thread.Sleep(pressDurationMs);
        var up = Send(new INPUT { type = InputKeyboard, u = new InputUnion { ki = new KEYBDINPUT { wVk = virtualKey, dwFlags = KeyUp } } });
        return down && up;
    }

    private static bool Send(INPUT input)
        => SendInput(1, [input], Marshal.SizeOf<INPUT>()) == 1;
}
