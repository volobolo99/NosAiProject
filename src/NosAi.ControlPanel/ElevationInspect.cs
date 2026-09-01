using System.Security.Principal;

namespace NosAi.ControlPanel;

/// <summary>Whether this process can open WinDivert. A missing elevation is not a capture.</summary>
internal static class ElevationInspect
{
    public static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static DisplayField Field(bool elevated) =>
        new("Processo elevato", elevated ? "sì" : "no", "LIVE");
}
