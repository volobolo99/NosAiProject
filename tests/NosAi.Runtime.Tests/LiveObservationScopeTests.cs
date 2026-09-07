using NosAi.LiveIntegration;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The shared live capture scope, extracted from the two private copies
/// <c>CollectCommand</c> and <c>LoadoutReportCommand</c> had grown -- identical
/// in composition, disposal order and failure strings, though not line for
/// line: <c>CollectCommand</c>'s carried 26 lines of doc comment the other did
/// not. It is now also used by <c>ScoutCommand</c>/<c>AutoplayCommand</c> for
/// their entity feed, and by <c>CombatReportCommand</c>/<c>EngageCommand</c>
/// through <c>LiveCombatObserver</c>.
/// </summary>
/// <remarks>
/// Only the refusal contract is pinned here, and deliberately: opening a real
/// scope needs a running client, its game connection in the OS table, and the
/// WinDivert backend (Administrator). What every caller depends on and what
/// therefore must hold on any host is narrower -- a failed open <b>names</b>
/// why and never throws. Of the callers, four carry on without an entity feed;
/// <c>CombatReportCommand</c> and <c>EngageCommand</c> refuse instead, because
/// a combat report with no entities would say nothing and an engagement with
/// no entities could not verify its target.
/// </remarks>
public sealed class LiveObservationScopeTests
{
    [Fact]
    public void AnImpossibleProcessId_IsRefusedWithANamedReason_NotAnException()
    {
        LiveObservationScope? scope = LiveObservationScope.TryOpen(0, out string? failureReason);

        Assert.Null(scope);
        Assert.Equal("connection_table:invalid_process_id", failureReason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void EveryRefusal_CarriesANonEmptyReason(int processId)
    {
        LiveObservationScope? scope = LiveObservationScope.TryOpen(processId, out string? failureReason);

        Assert.Null(scope);
        Assert.False(string.IsNullOrWhiteSpace(failureReason));
    }

    /// <summary>
    /// A process id that exists but is not a game client resolves through the
    /// real connection table and refuses by name rather than throwing -- the
    /// path the commands actually hit when the operator runs them against a
    /// machine with no client attached.
    /// </summary>
    /// <remarks>
    /// <see cref="NoSingleRemoteConnectionFactAttribute"/> rather than
    /// <see cref="FactAttribute"/>, and that is not defensive padding:
    /// <c>TryOpen</c> refuses this process only because the test host does not
    /// have exactly one non-loopback TCP connection. If it did,
    /// <c>Primary</c> would resolve and the next statement inside
    /// <c>TryOpen</c> is <c>WinDivertPacketSource.TryOpen</c> -- this test
    /// would open a real packet capture on the machine's traffic, and only the
    /// absence of an elevated WinDivert would stop it.
    /// </remarks>
    [NoSingleRemoteConnectionFact]
    public void ARealButUnrelatedProcess_IsRefusedWithANamedReason()
    {
        using System.Diagnostics.Process self = System.Diagnostics.Process.GetCurrentProcess();

        LiveObservationScope? scope = LiveObservationScope.TryOpen(self.Id, out string? failureReason);

        using (scope)
        {
            Assert.Null(scope);
            Assert.False(string.IsNullOrWhiteSpace(failureReason));
        }
    }
}
