using NosAi.Core;
using NosAi.Host;
using NosAi.Storage;
using Xunit;

namespace NosAi.Core.Tests;

[Trait("Category", "Gate1")]
public sealed class NosAiHostTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"nosai-gate1-host-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    [Fact]
    public async Task RunAsyncJournalsTheAttachOutcomeAndPublishesTelemetry()
    {
        var options = new HostOptions(
            ProcessName: $"nosai-gate1-test-{Guid.NewGuid():N}",
            ExpectedModule: "whatever.dll",
            ModuleSha256: "00",
            AttachTimeoutMs: 50,
            JournalOptions: new SqliteJournalOptions(),
            SessionId: "host-test-session",
            VerifyChainOnStart: true);

        await using NosAiHost host = NosAiHost.ComposeWithJournalPath(options, _databasePath);

        TelemetryFrame? published = null;
        host.Dashboard.FramePublished += frame => published = frame;

        HostBootstrapResult result = await host.RunAsync(CancellationToken.None);

        Assert.False(result.Attached);
        Assert.Equal(FaultCode.AttachFailed, result.AttachFault);
        Assert.Equal(0, result.JournaledSequence);
        Assert.Equal(true, result.ChainIntact);
        Assert.Equal(-1, result.ChainFirstBrokenSequence);

        Assert.NotNull(published);
        Assert.Equal("attach-failed", published!.Value.Status);
        Assert.Equal(FaultCode.AttachFailed, published.Value.Fault);
    }

    [Fact]
    public async Task SequentialRunsOnTheSameHostAppendSuccessiveJournalSequences()
    {
        var options = new HostOptions(
            ProcessName: $"nosai-gate1-test-{Guid.NewGuid():N}",
            ExpectedModule: "whatever.dll",
            ModuleSha256: "00",
            AttachTimeoutMs: 50,
            JournalOptions: new SqliteJournalOptions(),
            SessionId: "host-test-session-2",
            VerifyChainOnStart: false);

        await using NosAiHost host = NosAiHost.ComposeWithJournalPath(options, _databasePath);

        HostBootstrapResult first = await host.RunAsync(CancellationToken.None);
        HostBootstrapResult second = await host.RunAsync(CancellationToken.None);

        Assert.Equal(first.JournaledSequence + 1, second.JournaledSequence);
    }
}
