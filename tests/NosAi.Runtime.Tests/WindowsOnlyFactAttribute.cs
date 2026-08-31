using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// A fact that runs only on Windows and is skipped elsewhere.
/// </summary>
/// <remarks>
/// <para>
/// Some of the runtime's behaviour is genuinely Windows-only — the runtime
/// identity is wrapped with DPAPI (ADR-0010) and the client's TCP state is read
/// from <c>iphlpapi</c> — and the tests that exercise it call platform APIs that
/// throw or return nothing on Linux. CI runs the .NET tests on a Linux runner, so
/// those tests must skip there rather than fail: a red build from a test that
/// could never pass on the runner tells the reader nothing true.
/// </para>
/// <para>
/// This skips, it does not weaken. The behaviour is still covered on every
/// Windows developer machine and on a Windows runner; only the platform where the
/// API does not exist is excused, and the skip reason says why.
/// </para>
/// </remarks>
public sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Windows-only: exercises a platform API (DPAPI / iphlpapi) not present on this OS.";
    }
}
