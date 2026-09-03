using System.Net;
using NosAi.LiveIntegration.Capture;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The recorder that gives the memory probes a contemporaneous second source.
/// </summary>
/// <remarks>
/// No driver and no game here: <see cref="InMemoryPacketSource"/> is a first-class
/// source, so the outcome rules and the file round trip are exercised the same way
/// the rest of the capture engine is. Only opening WinDivert needs a real device,
/// and that is the one thing these tests do not touch.
/// </remarks>
public sealed class WireRecorderTests : IDisposable
{
    private static readonly IPAddress Server = IPAddress.Parse("10.20.30.40");
    private const int ServerPort = 4002;

    private readonly List<string> _tempFiles = new();

    private string TempPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nosai_record_{Guid.NewGuid():N}.noscap");
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (string path in _tempFiles)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException)
            {
                // A leftover temp file is not worth failing a test over.
            }
        }
    }

    // ---------- the endpoint

    [Fact]
    public void AnIpv4EndpointParses()
    {
        Assert.True(WireRecorder.TryParseEndpoint("10.20.30.40:4002", out IPAddress address, out int port, out string? why));
        Assert.Equal(Server, address);
        Assert.Equal(ServerPort, port);
        Assert.Null(why);
    }

    [Fact]
    public void AHostNameIsRefusedBecauseTheFilterIsAddressBased()
    {
        // A name can resolve to several addresses; the WinDivert filter names one.
        // Accepting the name would capture a subset and look like a quiet session.
        Assert.False(WireRecorder.TryParseEndpoint("login.nostale.com:4002", out _, out _, out string? why));
        Assert.StartsWith(WireRecorder.HostNotAnIpPrefix, why, StringComparison.Ordinal);
        Assert.Contains("login.nostale.com", why, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("-1")]
    [InlineData("porta")]
    public void APortOutsideTheRangeIsRefusedAndTheValueIsNamed(string portText)
    {
        Assert.False(WireRecorder.TryParseEndpoint($"10.20.30.40:{portText}", out _, out _, out string? why));
        Assert.StartsWith(WireRecorder.PortImplausiblePrefix, why, StringComparison.Ordinal);
        Assert.Contains(portText, why, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("10.20.30.40")]
    [InlineData("10.20.30.40:")]
    [InlineData(":4002")]
    public void AnEndpointMissingASideIsRefused(string text)
    {
        Assert.False(WireRecorder.TryParseEndpoint(text, out _, out _, out string? why));
        Assert.False(string.IsNullOrEmpty(why));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AMissingEndpointIsNamedRatherThanDefaulted(string? text)
    {
        // There is no sensible default server. Guessing one would capture nothing
        // and report success at having captured nothing.
        Assert.False(WireRecorder.TryParseEndpoint(text, out _, out _, out string? why));
        Assert.Equal(WireRecorder.EndpointMissingReason, why);
    }

    // ---------- where it writes

    [Fact]
    public void TheDefaultPathLandsWhereTheReplayCommandsLook()
    {
        var when = new DateTime(2026, 9, 3, 1, 2, 3, DateTimeKind.Utc);
        string path = WireRecorder.DefaultPath(when);

        Assert.Equal(WireRecorder.DefaultDirectory, Path.GetDirectoryName(path));
        Assert.EndsWith(".noscap", path, StringComparison.Ordinal);
        // The timestamp is in the name so two recordings in one session cannot
        // silently overwrite each other.
        Assert.Contains("20260903_010203", path, StringComparison.Ordinal);
    }

    // ---------- the outcome

    [Fact]
    public void ARecordingRoundTripsAndKeepsTheEndpointItWasTakenFrom()
    {
        string path = TempPath();
        var when = new DateTime(2026, 9, 3, 0, 55, 0, DateTimeKind.Utc);
        byte[] first = { 0x45, 0x00, 0x01 };
        byte[] second = { 0x45, 0x00, 0x02, 0x03 };

        var source = new InMemoryPacketSource(Server, ServerPort, new[]
        {
            new CapturedPacket(when, first),
            new CapturedPacket(when.AddMilliseconds(40), second),
        });

        RecordingOutcome outcome = WireRecorder.RecordFrom(source, path);

        Assert.True(outcome.Ok);
        Assert.Null(outcome.FailureReason);
        Assert.Equal(2, outcome.Packets);

        using CaptureFileSource replay = CaptureFile.Open(path);
        Assert.Equal(Server, replay.ServerAddress);
        Assert.Equal(ServerPort, replay.ServerPort);
        Assert.True(replay.TryRead(TimeSpan.Zero, out CapturedPacket restored));
        Assert.Equal(when, restored.TimestampUtc);
        Assert.Equal(first, restored.Raw.ToArray());
    }

    [Fact]
    public void ARunThatCapturedNothingIsRefusedRatherThanCalledASuccess()
    {
        string path = TempPath();
        var silent = new InMemoryPacketSource(Server, ServerPort, Array.Empty<CapturedPacket>());

        RecordingOutcome outcome = WireRecorder.RecordFrom(silent, path);

        Assert.False(outcome.Ok);
        Assert.Equal(WireRecorder.NoPacketsReason, outcome.FailureReason);
        Assert.Equal(0, outcome.Packets);
    }

    [Fact]
    public void AnEmptyRecordingIsRemovedRatherThanLeftWhereTheReadersLook()
    {
        // The header is written before the first packet, so a silent run would
        // leave a file every reader accepts and no reading can be checked
        // against — in the very directory the replay commands scan. Refusing
        // protects this call's exit code; deleting protects the next command.
        string path = TempPath();
        var silent = new InMemoryPacketSource(Server, ServerPort, Array.Empty<CapturedPacket>());

        RecordingOutcome outcome = WireRecorder.RecordFrom(silent, path);

        Assert.False(outcome.Ok);
        Assert.Equal(WireRecorder.NoPacketsReason, outcome.FailureReason);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void ACancelledRunThatCapturedNothingLeavesNothingBehind()
    {
        string path = TempPath();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var source = new InMemoryPacketSource(Server, ServerPort, new[]
        {
            new CapturedPacket(DateTime.UtcNow, new byte[] { 0x45 }),
        });

        RecordingOutcome outcome = WireRecorder.RecordFrom(source, path, cancelled.Token);

        Assert.Equal(0, outcome.Packets);
        Assert.Equal(WireRecorder.NoPacketsReason, outcome.FailureReason);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void CancellingDisposesTheSourceBecauseAWinDivertReadCannotBeInterruptedAnyOtherWay()
    {
        // WinDivertPacketSource.TryRead ignores its timeout and WinDivertRecv
        // blocks, so a quiet endpoint never returns to the loop's token check.
        // Closing the handle is the only thing that unblocks it, which is why
        // cancellation must reach the source and not just the loop. Without this,
        // --record-wire and --calibrate-vitals hang and Ctrl+C cannot recover.
        string path = TempPath();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var source = new DisposalWatchingSource(Server, ServerPort);

        WireRecorder.RecordFrom(source, path, cancelled.Token);

        Assert.True(source.Disposed);
    }

    /// <summary>A source that records whether cancellation reached it.</summary>
    private sealed class DisposalWatchingSource : IPacketSource
    {
        public DisposalWatchingSource(IPAddress serverAddress, int serverPort)
        {
            ServerAddress = serverAddress;
            ServerPort = serverPort;
        }

        public IPAddress ServerAddress { get; }
        public int ServerPort { get; }
        public bool Disposed { get; private set; }

        public bool TryRead(TimeSpan timeout, out CapturedPacket packet)
        {
            packet = default;
            return false;
        }

        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void TheRuntimeWiresTheRecorderFlag()
    {
        // The flag has to be reachable from Program, or the recorder exists and
        // the operator still has no way to run it.
        string source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "NosAi.Runtime", "Program.cs"));
        Assert.Contains("WireRecorder.Flag", source, StringComparison.Ordinal);
        Assert.Contains("WireRecorder.Run", source, StringComparison.Ordinal);
        Assert.Equal("--record-wire", WireRecorder.Flag);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CLAUDE.md")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
