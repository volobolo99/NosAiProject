using System.Runtime.InteropServices;

namespace NosAi.Runtime.LowLevel;

/// <summary>
/// Windows input abstraction kept behind an explicit safety boundary.
/// Live input injection is intentionally disabled until the runtime Safety Gate
/// and bring-up validation explicitly authorize it.
/// </summary>
public static class ProcessInputEmulation
{
    public static bool MoveMouse(int dx, int dy)
        => throw new NotSupportedException("Live input injection is disabled in the 1.0 Beta runtime.");

    public static bool ClickMouseLeft(int delayBetweenDownUpMs = 45)
        => throw new NotSupportedException("Live input injection is disabled in the 1.0 Beta runtime.");

    public static bool SendKeyPress(ushort virtualKey, int pressDurationMs = 80)
        => throw new NotSupportedException("Live input injection is disabled in the 1.0 Beta runtime.");
}
