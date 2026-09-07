using NosAi.LiveIntegration;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The shared live capture scope, extracted from the byte-identical private
/// copies <c>CollectCommand</c> and <c>LoadoutReportCommand</c> had grown and
/// now also used by <c>ScoutCommand</c>/<c>AutoplayCommand</c> for their entity
/// feed.
/// </summary>
/// <remarks>
/// Only the refusal contract is pinned here, and deliberately: opening a real
/// scope needs a running client, its game connection in the OS table, and the
/// WinDivert backend (Administrator). What every caller depends on and what
/// therefore must hold on any host is narrower -- a failed open <b>names</b>
/// why and never throws, because four commands treat it as a non-fatal
/// condition and carry on without an entity feed.
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
    /// real connection table and still refuses by name rather than throwing --
    /// the path the commands actually hit when the operator runs them against
    /// a machine with no client attached.
    /// </summary>
    [Fact]
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
