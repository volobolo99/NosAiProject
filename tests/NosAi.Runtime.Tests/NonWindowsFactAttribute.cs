using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// A fact that runs only off Windows and is skipped there.
/// </summary>
/// <remarks>
/// <para>
/// The mirror image of <see cref="WindowsOnlyFactAttribute"/>, and needed for the
/// same reason. A few tests assert the fail-closed branch the runtime takes when
/// the Windows-only API is absent — the client window lookup, DXGI desktop
/// duplication. That branch is only reachable where
/// <c>OperatingSystem.IsWindows()</c> is false, so on a Windows runner the test
/// cannot pass: it fails on its own guard and the build goes red for a platform
/// difference, not for a defect.
/// </para>
/// <para>
/// This skips, it does not weaken. CI runs the .NET tests on a Linux runner
/// (<c>.github/workflows/ci.yml</c>), where these still execute and still assert
/// the whole fail-closed path; only the Windows runner
/// (<c>.github/workflows/dotnet-windows.yml</c>), where the branch does not
/// exist, is excused, and the skip reason says why.
/// </para>
/// </remarks>
public sealed class NonWindowsFactAttribute : FactAttribute
{
    public NonWindowsFactAttribute()
    {
        if (OperatingSystem.IsWindows())
            Skip = "Non-Windows only: asserts the fail-closed branch taken when the Windows-only API is absent.";
    }
}
