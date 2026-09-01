using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace NosAi.Runtime.Perception;

/// <summary>Where a process's client area sits on screen, and which window that is.</summary>
/// <param name="Handle">The window that actually renders.</param>
/// <param name="ClassName">Its window class, for the operator's report.</param>
/// <param name="ClientArea">Client area in screen pixels, ready to pass to <see cref="RoiSegmenter"/>.</param>
public sealed record ClientWindow(IntPtr Handle, string ClassName, PixelRect ClientArea);

/// <summary>
/// Finds the window a game process actually draws in.
/// </summary>
/// <remarks>
/// <para>
/// Not <c>Process.MainWindowHandle</c>, which is what the client connector used
/// and what T-03 caught being wrong. On the real NosTale client that property
/// returns the Delphi <c>TApplication</c> window: a hidden 159x27 stub parked at
/// -25600,-25600 with a client area of 0x0. It is a perfectly valid window handle
/// and it renders nothing. The window that draws is a sibling of class
/// <c>TNosTaleMainF</c>, and nothing about the process object points at it.
/// </para>
/// <para>
/// So the search is by property rather than by name: among the process's
/// top-level windows, take a visible one with a non-empty client area that is on
/// screen. A window cannot be the one being looked at if it is invisible, has no
/// client area, or sits off the desktop -- and those three checks are what
/// separate the stub from the game.
/// </para>
/// </remarks>
public static class ClientWindowLocator
{
    /// <summary>Smallest client area worth treating as a rendering surface.</summary>
    private const int MinimumSide = 100;

    /// <summary>
    /// The largest visible client area belonging to <paramref name="processId"/>,
    /// or null with a reason.
    /// </summary>
    /// <remarks>
    /// Largest, because a game can own tooltip and overlay windows that satisfy
    /// every other test; the one it draws the world in is the big one.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    public static ClientWindow? TryFind(int processId, out string? failureReason)
    {
        failureReason = null;
        ClientWindow? best = null;
        long bestArea = 0;

        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out uint owner);
            if (owner != (uint)processId || !IsWindowVisible(handle) || IsIconic(handle))
                return true;

            if (!GetClientRect(handle, out Rect client))
                return true;

            int width = client.Right - client.Left;
            int height = client.Bottom - client.Top;
            if (width < MinimumSide || height < MinimumSide)
                return true;

            var origin = new Point { X = 0, Y = 0 };
            if (!ClientToScreen(handle, ref origin))
                return true;

            // A window parked off the desktop is the shape the Delphi stub takes;
            // it is not somewhere a person is looking.
            if (origin.X < -10_000 || origin.Y < -10_000)
                return true;

            long area = (long)width * height;
            if (area <= bestArea)
                return true;

            var name = new StringBuilder(256);
            GetClassNameW(handle, name, name.Capacity);
            bestArea = area;
            best = new ClientWindow(handle, name.ToString(), new PixelRect(origin.X, origin.Y, width, height));
            return true;
        }, IntPtr.Zero);

        if (best is null)
            failureReason = "no_visible_client_window";

        return best;
    }

    private delegate bool EnumProc(IntPtr handle, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumProc callback, IntPtr parameter);

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr handle);

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr handle, out Rect rect);

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr handle, ref Point point);

    [SupportedOSPlatform("windows")]
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr handle, StringBuilder name, int capacity);
}
