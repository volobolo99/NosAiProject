using NosAi.LiveIntegration;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// A fact that may call <see cref="LiveObservationScope.TryOpen"/> against the
/// test process itself, and therefore runs only on a host where that call
/// provably stops before the capture backend.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="LiveObservationScope.TryOpen"/> reads the OS connection table,
/// and refuses with <c>no_single_game_connection</c> unless the process has
/// exactly one non-loopback TCP connection. When it has exactly one, the very
/// next statement is <c>WinDivertPacketSource.TryOpen</c>: a unit test would
/// open a real packet capture on this machine's traffic, and only the absence
/// of an elevated WinDivert would stop it.
/// </para>
/// <para>
/// Whether the test host has one remote connection is an accident of what else
/// the machine is doing, not a property of the code under test. A test that
/// passes for that reason is passing by luck, so the condition is checked here
/// and the test is skipped -- visibly, with the reason attached -- rather than
/// run and hoped over.
/// </para>
/// </remarks>
public sealed class NoSingleRemoteConnectionFactAttribute : FactAttribute
{
    public NoSingleRemoteConnectionFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Windows only: the OS connection table this guard reads is a Windows API.";
            return;
        }

        using System.Diagnostics.Process self = System.Diagnostics.Process.GetCurrentProcess();
        ClientNetworkObservation observation = ClientNetworkObserver.Observe(self.Id);
        if (observation.Observed && observation.Primary is not null)
        {
            Skip = "The test host itself has exactly one remote TCP connection, "
                   + "so TryOpen would resolve a primary endpoint and reach WinDivert.";
        }
    }
}
