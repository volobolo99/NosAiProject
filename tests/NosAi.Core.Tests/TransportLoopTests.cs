using System.Net.Sockets;
using System.Text;
using NosAi.Core;
using NosAi.Host;
using NosAi.Security;
using NosAi.Storage;
using Xunit;

namespace NosAi.Core.Tests;

[Trait("Category", "Gate1")]
public sealed class TransportLoopTests : IDisposable
{
    private static readonly byte[] RootKey = Encoding.UTF8.GetBytes("gate1-transport-root-key-32bytes!");
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"nosai-gate1-transport-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    [Fact]
    public async Task HandshakeCapabilityHeartbeatsAndDisconnectAreJournaledOnRealTcp()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using NosAiHost host = StartHost();
        Task<HostBootstrapResult> run = host.RunAsync(cts.Token).AsTask();
        await host.WhenListening.WaitAsync(cts.Token);

        await using (Gate1LoopbackPeer peer = await Gate1LoopbackPeer.ConnectAsync(host.BoundPort, cts.Token))
        {
            await peer.HandshakeAsync(cts.Token);
            CapabilityVerdict verdict = await peer.PresentCapabilityAsync(IssueObserveToken(), cts.Token);
            Assert.True(verdict.Granted);

            await peer.SendHeartbeatAsync(cts.Token);
            await peer.SendHeartbeatAsync(cts.Token);
            await peer.SendDisconnectAsync(cts.Token);
        }

        await WaitForAsync(() => host.Dashboard.CompletedSessionCount >= 1, cts.Token);
        cts.Cancel();
        await run;

        Assert.True(host.Dashboard.AcceptedFrameCount >= 3);
        Assert.Contains(host.Dashboard.Snapshot(), f => f.Status == "transport");
        Assert.Contains(host.Dashboard.Snapshot(), f => f.Status == "disconnected");

        var payloads = new List<string>();
        await foreach (JournalRecord record in host.Journal.ReplayAsync(0, CancellationToken.None))
            payloads.Add(Encoding.UTF8.GetString(record.Payload.Span));

        Assert.Contains(payloads, p => p.Contains("handshake;state=Transport", StringComparison.Ordinal));
        Assert.Contains(payloads, p => p.Contains("capability;granted=True", StringComparison.Ordinal));
        Assert.Contains(payloads, p => p.Contains("frame;op=heartbeat", StringComparison.Ordinal));
        Assert.Contains(payloads, p => p.StartsWith("disconnect;", StringComparison.Ordinal));
        Assert.True(host.Journal.VerifyChain(0, out long broken));
        Assert.Equal(-1, broken);
    }

    [Fact]
    public async Task AbruptTcpCloseIsJournaledAsNetworkFaultWithoutBreakingTheChain()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using NosAiHost host = StartHost();
        Task<HostBootstrapResult> run = host.RunAsync(cts.Token).AsTask();
        await host.WhenListening.WaitAsync(cts.Token);

        await using Gate1LoopbackPeer peer = await Gate1LoopbackPeer.ConnectAsync(host.BoundPort, cts.Token);
        await peer.HandshakeAsync(cts.Token);
        CapabilityVerdict verdict = await peer.PresentCapabilityAsync(IssueObserveToken(), cts.Token);
        Assert.True(verdict.Granted);
        await peer.SendHeartbeatAsync(cts.Token);
        peer.Abort();

        await WaitForAsync(() => host.Dashboard.CompletedSessionCount >= 1, cts.Token);
        cts.Cancel();
        await run;

        Assert.Contains(host.Dashboard.Snapshot(), f => f.Status == "disconnected" && f.Fault == FaultCode.Network);
        Assert.True(host.Journal.VerifyChain(0, out long broken));
        Assert.Equal(-1, broken);
    }

    [Fact]
    public async Task ReplayedApplicationSequenceIsRejectedAndJournaled()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using NosAiHost host = StartHost();
        Task<HostBootstrapResult> run = host.RunAsync(cts.Token).AsTask();
        await host.WhenListening.WaitAsync(cts.Token);

        await using (Gate1LoopbackPeer peer = await Gate1LoopbackPeer.ConnectAsync(host.BoundPort, cts.Token))
        {
            await peer.HandshakeAsync(cts.Token);
            Assert.True((await peer.PresentCapabilityAsync(IssueObserveToken(), cts.Token)).Granted);
            uint sequence = await peer.SendHeartbeatAsync(cts.Token);
            await peer.SendHeartbeatAtAsync(sequence, cts.Token);
            await peer.SendDisconnectAsync(cts.Token);
        }

        await WaitForAsync(() => host.Dashboard.CompletedSessionCount >= 1, cts.Token);
        cts.Cancel();
        await run;

        Assert.Contains(host.Dashboard.Snapshot(), f => f.Status == "replay" && f.Fault == FaultCode.Replay);
    }

    [Fact]
    public async Task ExpiredCapabilityTokenIsDeniedAndTheSessionEnds()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using NosAiHost host = StartHost();
        Task<HostBootstrapResult> run = host.RunAsync(cts.Token).AsTask();
        await host.WhenListening.WaitAsync(cts.Token);

        await using (Gate1LoopbackPeer peer = await Gate1LoopbackPeer.ConnectAsync(host.BoundPort, cts.Token))
        {
            await peer.HandshakeAsync(cts.Token);
            CapabilityToken expired = CapabilityToken.Issue(1, CapabilityScope.Observe, 1, 2, RootKey);
            CapabilityVerdict verdict = await peer.PresentCapabilityAsync(expired, cts.Token);
            Assert.False(verdict.Granted);
            Assert.Equal(FaultCode.Timeout, verdict.Fault);
        }

        await WaitForAsync(() => host.Dashboard.CompletedSessionCount >= 1, cts.Token);
        cts.Cancel();
        await run;

        Assert.Contains(host.Dashboard.Snapshot(), f => f.Status == "capability-denied");
    }

    [Fact]
    public async Task OneHundredLoopbackHandshakesStayUnderTheTwentyFiveMillisecondBudget()
    {
        // Local TCP bound only. docs/TEST_RIMANDATI.md T-06 is the same
        // measurement through a real phone and is not closed by this test.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await using NosAiHost host = StartHost();
        Task<HostBootstrapResult> run = host.RunAsync(cts.Token).AsTask();
        await host.WhenListening.WaitAsync(cts.Token);

        var samples = new List<long>(100);
        for (int i = 0; i < 100; i++)
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            await using Gate1LoopbackPeer peer = await Gate1LoopbackPeer.ConnectAsync(host.BoundPort, cts.Token);
            await peer.HandshakeAsync(cts.Token);
            long elapsedTicks = System.Diagnostics.Stopwatch.GetTimestamp() - start;
            samples.Add(elapsedTicks);
        }

        await WaitForAsync(() => host.Dashboard.CompletedSessionCount >= 100, cts.Token);
        cts.Cancel();
        await run;

        samples.Sort();
        double p99Ms = samples[98] * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        Assert.True(p99Ms < 25.0, $"Loopback handshake p99 was {p99Ms:F3} ms (T-06 still requires a real phone).");
    }

    private NosAiHost StartHost()
    {
        var options = new HostOptions(
            ProcessName: $"nosai-gate1-transport-{Guid.NewGuid():N}",
            ExpectedModule: "whatever.dll",
            ModuleSha256: "00",
            AttachTimeoutMs: 50,
            JournalOptions: new SqliteJournalOptions(),
            SessionId: "host-transport-session",
            VerifyChainOnStart: true,
            ListenPort: 0,
            ListenAddress: "127.0.0.1",
            StaticPrivateKey: NoiseXxSession.GenerateStaticPrivateKey(),
            CapabilityRootKey: RootKey);

        return NosAiHost.ComposeWithJournalPath(options, _databasePath);
    }

    private static CapabilityToken IssueObserveToken()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return CapabilityToken.Issue(1, CapabilityScope.Observe, now - 60_000, now + 3_600_000, RootKey);
    }

    private static async Task WaitForAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(15, ct);
        }
    }
}

/// <summary>
/// Real TCP initiator for Gate 1 tests: a <see cref="TcpClient"/> plus a real
/// <see cref="NoiseXxSession"/>. Not a test double of the host — it is the
/// other end of the wire the host is specified to speak.
/// </summary>
internal sealed class Gate1LoopbackPeer : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly NoiseXxSession _session;
    private readonly byte[] _noise = new byte[LengthPrefixedRecord.MaxLength];
    private readonly byte[] _plain = new byte[LengthPrefixedRecord.MaxLength];
    private FrameTagCalculator? _tags;
    private SequenceGuard _incoming = new();
    private uint _outbound;
    private bool _aborted;

    private Gate1LoopbackPeer(TcpClient client, NetworkStream stream, NoiseXxSession session)
    {
        _client = client;
        _stream = stream;
        _session = session;
    }

    public static async Task<Gate1LoopbackPeer> ConnectAsync(int port, CancellationToken ct)
    {
        var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync(System.Net.IPAddress.Loopback, port, ct);
        return new Gate1LoopbackPeer(client, client.GetStream(), new NoiseXxSession(initiator: true, NoiseXxSession.GenerateStaticPrivateKey()));
    }

    public async Task HandshakeAsync(CancellationToken ct)
    {
        int first = _session.WriteMessage(ReadOnlySpan<byte>.Empty, _noise);
        await LengthPrefixedRecord.WriteAsync(_stream, _noise.AsMemory(0, first), ct);

        int second = await LengthPrefixedRecord.ReadAsync(_stream, _noise, ct);
        Assert.True(second > 0);
        _session.ReadMessage(_noise.AsSpan(0, second), _plain);

        int third = _session.WriteMessage(ReadOnlySpan<byte>.Empty, _noise);
        await LengthPrefixedRecord.WriteAsync(_stream, _noise.AsMemory(0, third), ct);

        Assert.Equal(NoiseHandshakeState.Transport, _session.State);
        _tags = new FrameTagCalculator(_session.DeriveFrameSessionKey());
    }

    public async Task<CapabilityVerdict> PresentCapabilityAsync(CapabilityToken token, CancellationToken ct)
    {
        byte[] payload = new byte[CapabilityToken.WireLength];
        token.WriteTo(payload);
        await SendFrameAsync(FrameOpCode.PresentCapability, _outbound++, payload, ct);
        return await ReadDecisionAsync(ct);
    }

    public Task<uint> SendHeartbeatAsync(CancellationToken ct) => SendHeartbeatAtAsync(_outbound++, ct);

    public async Task<uint> SendHeartbeatAtAsync(uint sequence, CancellationToken ct)
    {
        await SendFrameAsync(FrameOpCode.Heartbeat, sequence, [], ct);
        return sequence;
    }

    public Task SendDisconnectAsync(CancellationToken ct) =>
        SendFrameAsync(FrameOpCode.Disconnect, _outbound++, [], ct);

    public void Abort()
    {
        _aborted = true;
        _client.Close();
    }

    private async Task SendFrameAsync(FrameOpCode op, uint sequence, byte[] payload, CancellationToken ct)
    {
        Assert.NotNull(_tags);
        int frameLength = FrameCodec.Encode((byte)op, sequence, payload, _tags!, _plain);
        int cipherLength = _session.WriteMessage(_plain.AsSpan(0, frameLength), _noise);
        await LengthPrefixedRecord.WriteAsync(_stream, _noise.AsMemory(0, cipherLength), ct);
    }

    private async Task<CapabilityVerdict> ReadDecisionAsync(CancellationToken ct)
    {
        Assert.NotNull(_tags);
        int cipherLength = await LengthPrefixedRecord.ReadAsync(_stream, _noise, ct);
        Assert.True(cipherLength > 0);
        int plainLength = _session.ReadMessage(_noise.AsSpan(0, cipherLength), _plain);
        Assert.True(TryReadDecision(_plain, plainLength, _tags!, out CapabilityVerdict verdict, out FaultCode fault, out byte op, out uint seq));
        Assert.Equal(FaultCode.None, fault);
        Assert.True(_incoming.TryAccept(seq));
        Assert.Equal((byte)FrameOpCode.CapabilityDecision, op);
        return verdict;
    }

    private static bool TryReadDecision(
        byte[] plain,
        int plainLength,
        FrameTagCalculator tags,
        out CapabilityVerdict verdict,
        out FaultCode fault,
        out byte op,
        out uint seq)
    {
        verdict = default;
        op = 0;
        seq = 0;
        if (!FrameCodec.TryDecode(plain.AsSpan(0, plainLength), tags, out NosFrameHeader header, out ReadOnlySpan<byte> payload, out fault))
            return false;

        op = header.OpCode;
        seq = header.Sequence;
        return CapabilityVerdict.TryRead(payload, out verdict);
    }

    public async ValueTask DisposeAsync()
    {
        _tags?.Dispose();
        _session.Dispose();
        if (!_aborted)
        {
            await _stream.DisposeAsync();
            _client.Dispose();
        }
    }
}
