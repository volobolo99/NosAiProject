using System.Buffers.Binary;
using System.Net;
using NosAi.LiveIntegration;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Demonstrates that the pre-login capture session trims its .noscap recording to the chosen
/// endpoint, closes by itself once the character load has been observed (stat, in, ivn), and
/// reports precisely why it could not close when the evidence is incomplete.
/// </summary>
/// <remarks>
/// The packets are synthetic, but the chain under test is the real one: the IPv4/TCP parser,
/// the TCP reassembler, the world-channel framer and the opcode decoder exercised here are the
/// same components used by the live capture path. The only part that is not present is the
/// driver that feeds the prelude from the network.
/// </remarks>
public sealed class PreLoginCaptureSessionTests : IDisposable
{
    private static readonly IPAddress Server = IPAddress.Parse("79.110.84.175");
    private const int ServerPort = 4006;
    private static readonly IPAddress Client = IPAddress.Parse("192.168.0.4");
    private const int ClientPort = 56027;
    private static readonly IPAddress OtherServer = IPAddress.Parse("203.0.113.9");
    private const int OtherPort = 9999;
    private static readonly DateTime At = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

    private readonly string _root;

    public PreLoginCaptureSessionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nosai-prelogin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
        }
    }

    /// <summary>
    /// Encodes a world-channel payload. The decoder reads one header byte: 0xFF ends the frame,
    /// otherwise header &amp; 0x7F is the field length and each payload byte decodes as byte ^ 0xFF.
    /// The inverse encoding is: length byte, XOR-complemented text bytes, 0xFF terminator.
    /// </summary>
    private static byte[] WorldPacket(string text)
    {
        if (text.Length == 0 || text.Length > 0x7F)
        {
            throw new ArgumentException("World packet text must be between 1 and 127 characters.", nameof(text));
        }

        var bytes = new byte[text.Length + 2];
        bytes[0] = (byte)text.Length;
        for (int i = 0; i < text.Length; i++)
        {
            bytes[i + 1] = (byte)(text[i] ^ 0xFF);
        }

        bytes[^1] = 0xFF;
        return bytes;
    }

    /// <summary>
    /// Builds a minimal IPv4/TCP packet: 0x45 version/IHL byte, total length, protocol 6, source and
    /// destination addresses, then a TCP header with the given ports, sequence number, a data offset
    /// of five 32-bit words and an optional SYN flag.
    /// </summary>
    private static byte[] TcpPacket(
        IPAddress source,
        int sourcePort,
        IPAddress destination,
        int destinationPort,
        uint sequence,
        ReadOnlySpan<byte> body,
        bool syn = false)
    {
        var packet = new byte[20 + 20 + body.Length];
        packet[0] = 0x45;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), (ushort)packet.Length);
        packet[9] = 6;
        source.GetAddressBytes().CopyTo(packet.AsSpan(12, 4));
        destination.GetAddressBytes().CopyTo(packet.AsSpan(16, 4));

        var tcp = packet.AsSpan(20);
        BinaryPrimitives.WriteUInt16BigEndian(tcp[..2], (ushort)sourcePort);
        BinaryPrimitives.WriteUInt16BigEndian(tcp.Slice(2, 2), (ushort)destinationPort);
        BinaryPrimitives.WriteUInt32BigEndian(tcp.Slice(4, 4), sequence);
        tcp[12] = 5 << 4;
        if (syn)
        {
            tcp[13] = 0x02;
        }

        body.CopyTo(tcp[20..]);
        return packet;
    }

    /// <summary>
    /// A watcher whose lookups report the client process 4242 and an established connection from the
    /// client to the chosen server, so the arrival is identified without any live system state.
    /// </summary>
    private static ClientArrivalWatcher IdentifiedWatcher()
    {
        var connection = new ClientTcpConnection(
            new IPEndPoint(Client, ClientPort),
            new IPEndPoint(Server, ServerPort),
            ClientTcpState.Established);

        return new ClientArrivalWatcher(
            processLookup: () => new[] { 4242 },
            networkLookup: _ => new ClientNetworkObservation(new[] { connection }, connection, null),
            pollInterval: TimeSpan.FromMilliseconds(10));
    }

    /// <summary>
    /// A prelude whose input is fully fed and closed. The clock is pinned to <see cref="At"/>, the same
    /// instant used by the test packets: with the default clock the packets dated 2026-09-01 would look
    /// older than the buffer window and would be evicted before the session could read them.
    /// </summary>
    private static BroadWirePrelude Armed(params CapturedPacket[] packets)
    {
        var prelude = new BroadWirePrelude(clock: () => At);
        foreach (CapturedPacket packet in packets)
        {
            prelude.Accept(packet);
        }

        prelude.CompleteInput();
        return prelude;
    }

    /// <summary>
    /// Wraps a world payload in an inbound TCP packet from the chosen server. Sequence numbers must stay
    /// contiguous per direction or the reassembler would hold bytes waiting for the missing segment.
    /// </summary>
    private static CapturedPacket Inbound(string text, ref uint sequence, int millisecond)
    {
        byte[] body = WorldPacket(text);
        byte[] raw = TcpPacket(Server, ServerPort, Client, ClientPort, sequence, body);
        sequence += (uint)body.Length;
        return new CapturedPacket(At.AddMilliseconds(millisecond), raw);
    }

    /// <summary>
    /// The outbound SYN that opens the connection to the chosen server.
    /// </summary>
    private static CapturedPacket OutboundSyn()
    {
        return new CapturedPacket(At, TcpPacket(Client, ClientPort, Server, ServerPort, 5000, ReadOnlySpan<byte>.Empty, syn: true));
    }

    [Fact]
    public void Only_the_chosen_endpoint_reaches_the_capture_file()
    {
        uint sequence = 1000;

        CapturedPacket syn = OutboundSyn();
        CapturedPacket stat = Inbound("stat 7305 7305 1420 1420 0 1184", ref sequence, 1);
        var otherOne = new CapturedPacket(
            At.AddMilliseconds(2),
            TcpPacket(OtherServer, OtherPort, Client, 40000, 7000, WorldPacket("stat 1 1 1 1")));
        CapturedPacket inventory = Inbound("in 1 2 3 4 5", ref sequence, 3);
        var otherTwo = new CapturedPacket(
            At.AddMilliseconds(4),
            TcpPacket(OtherServer, OtherPort, Client, 40000, 7000, WorldPacket("mov 1 2 3 4")));

        string capturePath = Path.Combine(_root, "endpoint.noscap");
        using var prelude = Armed(syn, stat, otherOne, inventory, otherTwo);
        ClientArrivalWatcher watcher = IdentifiedWatcher();
        using var session = new PreLoginCaptureSession(
            prelude,
            watcher,
            capturePath,
            streamProvenance: DataSourceKind.Live,
            timeout: TimeSpan.FromSeconds(10));

        PreLoginCaptureOutcome outcome = session.Run();

        Assert.NotNull(outcome.CapturePath);
        Assert.True(File.Exists(outcome.CapturePath));

        using (CaptureFileSource source = CaptureFile.Open(outcome.CapturePath!))
        {
            Assert.Equal(Server, source.ServerAddress);
            Assert.Equal(ServerPort, source.ServerPort);

            var written = new List<byte[]>();
            while (source.TryRead(TimeSpan.FromMilliseconds(50), out CapturedPacket packet))
            {
                written.Add(packet.Raw.ToArray());
            }

            Assert.Equal(3, written.Count);
            Assert.Equal(syn.Raw.ToArray(), written[0]);
            Assert.Equal(stat.Raw.ToArray(), written[1]);
            Assert.Equal(inventory.Raw.ToArray(), written[2]);

            byte[] otherOneRaw = otherOne.Raw.ToArray();
            byte[] otherTwoRaw = otherTwo.Raw.ToArray();
            foreach (byte[] raw in written)
            {
                Assert.NotEqual(otherOneRaw, raw);
                Assert.NotEqual(otherTwoRaw, raw);
            }
        }

        Assert.Equal(3L, outcome.PacketsBeforeDiscovery);
        Assert.Equal(3L, outcome.PacketsWritten);
    }

    [Fact]
    public void A_session_that_sees_stat_in_and_ivn_closes_complete()
    {
        uint sequence = 1000;

        CapturedPacket syn = OutboundSyn();
        CapturedPacket stat = Inbound("stat 7305 7305 1420 1420 0 1184", ref sequence, 1);
        CapturedPacket inventory = Inbound("in 1 2 3 4 5", ref sequence, 3);
        CapturedPacket ivn = Inbound("ivn 0 1 2 3 4", ref sequence, 5);

        string capturePath = Path.Combine(_root, "complete.noscap");
        using var prelude = Armed(syn, stat, inventory, ivn);
        ClientArrivalWatcher watcher = IdentifiedWatcher();
        using var session = new PreLoginCaptureSession(prelude, watcher, capturePath, timeout: TimeSpan.FromSeconds(10));

        PreLoginCaptureOutcome outcome = session.Run();

        Assert.Equal(PreLoginOutcomeKind.Complete, outcome.Kind);
        Assert.Null(outcome.Reason);
        Assert.Empty(outcome.OpcodesMissing);
        Assert.Contains("stat", outcome.OpcodesObserved);
        Assert.Contains("in", outcome.OpcodesObserved);
        Assert.Contains("ivn", outcome.OpcodesObserved);
        Assert.False(outcome.HandshakeMayBeMissing);
        Assert.True(outcome.VitalsReadings >= 1);
        Assert.Equal(DataSourceKind.Live, outcome.VitalsProvenance);
        Assert.True(outcome.VitalsSatisfied);
        Assert.Equal(0L, outcome.PacketsAfterDiscovery);
    }

    [Fact]
    public void A_session_missing_an_opcode_closes_partial_and_names_what_was_missing()
    {
        uint sequence = 1000;

        CapturedPacket syn = OutboundSyn();
        CapturedPacket stat = Inbound("stat 7305 7305 1420 1420 0 1184", ref sequence, 1);
        CapturedPacket inventory = Inbound("in 1 2 3 4 5", ref sequence, 3);

        string capturePath = Path.Combine(_root, "missing-ivn.noscap");
        using var prelude = Armed(syn, stat, inventory);
        ClientArrivalWatcher watcher = IdentifiedWatcher();
        using var session = new PreLoginCaptureSession(prelude, watcher, capturePath, timeout: TimeSpan.FromSeconds(10));

        PreLoginCaptureOutcome outcome = session.Run();

        Assert.Equal(PreLoginOutcomeKind.Partial, outcome.Kind);
        string missing = Assert.Single(outcome.OpcodesMissing);
        Assert.Equal("ivn|equip", missing);
        Assert.NotNull(outcome.Reason);
        Assert.StartsWith("source_ended_before_criterion", outcome.Reason!);
        Assert.Contains("ivn|equip", outcome.Reason!);
        Assert.NotNull(outcome.CapturePath);
        Assert.True(File.Exists(outcome.CapturePath));
    }

    [Fact]
    public void Vitals_that_are_not_live_do_not_close_the_vitals_probe()
    {
        uint sequence = 1000;

        CapturedPacket syn = OutboundSyn();
        CapturedPacket stat = Inbound("stat 7305 7305 1420 1420 0 1184", ref sequence, 1);
        CapturedPacket inventory = Inbound("in 1 2 3 4 5", ref sequence, 3);
        CapturedPacket ivn = Inbound("ivn 0 1 2 3 4", ref sequence, 5);

        string capturePath = Path.Combine(_root, "cached.noscap");
        using var prelude = Armed(syn, stat, inventory, ivn);
        ClientArrivalWatcher watcher = IdentifiedWatcher();
        using var session = new PreLoginCaptureSession(
            prelude,
            watcher,
            capturePath,
            streamProvenance: DataSourceKind.Cached,
            timeout: TimeSpan.FromSeconds(10));

        PreLoginCaptureOutcome outcome = session.Run();

        Assert.True(outcome.VitalsReadings >= 1);
        Assert.Equal(DataSourceKind.Cached, outcome.VitalsProvenance);
        Assert.False(outcome.VitalsSatisfied);
        Assert.NotNull(outcome.VitalsReason);
        Assert.StartsWith("vitals_not_live", outcome.VitalsReason!);
        Assert.Equal(PreLoginOutcomeKind.Partial, outcome.Kind);
        Assert.NotNull(outcome.Reason);
        Assert.StartsWith("stream_not_live", outcome.Reason!);
    }

    [Fact]
    public void A_recording_without_the_handshake_does_not_close_complete()
    {
        uint sequence = 1000;

        CapturedPacket stat = Inbound("stat 7305 7305 1420 1420 0 1184", ref sequence, 1);
        CapturedPacket inventory = Inbound("in 1 2 3 4 5", ref sequence, 3);
        CapturedPacket ivn = Inbound("ivn 0 1 2 3 4", ref sequence, 5);

        string capturePath = Path.Combine(_root, "no-handshake.noscap");
        using var prelude = Armed(stat, inventory, ivn);
        ClientArrivalWatcher watcher = IdentifiedWatcher();
        using var session = new PreLoginCaptureSession(prelude, watcher, capturePath, timeout: TimeSpan.FromSeconds(10));

        PreLoginCaptureOutcome outcome = session.Run();

        Assert.True(outcome.HandshakeMayBeMissing);
        Assert.Empty(outcome.OpcodesMissing);
        Assert.Equal(PreLoginOutcomeKind.Partial, outcome.Kind);
        Assert.Equal("handshake_not_in_recording:no_syn_before_discovery", outcome.Reason!);
    }

    /// <summary>
    /// A session whose client never arrives produces no recording at all and says in which words it
    /// failed to produce one.
    /// </summary>
    [Fact]
    public void A_session_whose_client_never_arrives_fails_naming_the_missing_endpoint()
    {
        string capturePath = Path.Combine(_root, "never-arrives.noscap");
        using var prelude = Armed();
        var watcher = new ClientArrivalWatcher(
            processLookup: () => Array.Empty<int>(),
            pollInterval: TimeSpan.FromMilliseconds(5));
        using var session = new PreLoginCaptureSession(prelude, watcher, capturePath, timeout: TimeSpan.FromMilliseconds(50));

        PreLoginCaptureOutcome outcome = session.Run();

        Assert.Equal(PreLoginOutcomeKind.Failed, outcome.Kind);
        Assert.NotNull(outcome.Reason);
        Assert.StartsWith("no_client_endpoint:", outcome.Reason!);
        Assert.Null(outcome.CapturePath);
        Assert.Null(outcome.Endpoint);
        Assert.False(File.Exists(capturePath));
        Assert.Equal(0L, outcome.PacketsWritten);
    }

    /// <summary>
    /// When the capture file cannot be written the session fails saying so, no exception escapes from
    /// Run, and above all the wide buffer never spills anywhere onto disk.
    /// </summary>
    [Fact]
    public void A_capture_file_that_cannot_be_written_fails_without_spilling_the_wide_buffer()
    {
        string blocker = Path.Combine(_root, "blocker");
        File.WriteAllText(blocker, "not a directory");
        string capturePath = Path.Combine(blocker, "denied", "writer-failed.noscap");

        uint sequence = 1000;
        CapturedPacket syn = OutboundSyn();
        CapturedPacket stat = Inbound("stat 7305 7305 1420 1420 0 1184", ref sequence, 1);

        using var prelude = Armed(syn, stat);
        ClientArrivalWatcher watcher = IdentifiedWatcher();
        using var session = new PreLoginCaptureSession(prelude, watcher, capturePath, timeout: TimeSpan.FromSeconds(10));

        PreLoginCaptureOutcome outcome = session.Run();

        Assert.Equal(PreLoginOutcomeKind.Failed, outcome.Kind);
        Assert.NotNull(outcome.Reason);
        Assert.StartsWith("capture_write_failed:", outcome.Reason!);
        Assert.Equal(0L, outcome.PacketsWritten);
        Assert.False(File.Exists(capturePath));
        Assert.False(Directory.Exists(Path.Combine(blocker, "denied")));
        Assert.Empty(Directory.GetFiles(_root, "*.noscap", SearchOption.AllDirectories));
        Assert.Equal("not a directory", File.ReadAllText(blocker));
    }

    /// <summary>
    /// If the conversation stays alive but incomplete, it is the deadline that closes the session, and
    /// the session declares which opcode it never saw.
    /// </summary>
    [Fact]
    public void A_session_that_runs_out_of_time_closes_partial_naming_the_missing_opcodes()
    {
        uint sequence = 1000;
        CapturedPacket syn = OutboundSyn();
        CapturedPacket stat = Inbound("stat 7305 7305 1420 1420 0 1184", ref sequence, 1);
        CapturedPacket inventory = Inbound("in 1 2 3 4 5", ref sequence, 3);

        string capturePath = Path.Combine(_root, "out-of-time.noscap");

        // Deliberately not CompleteInput(): the source stays live, so only the deadline can end the loop.
        using var prelude = new BroadWirePrelude(clock: () => At);
        prelude.Accept(syn);
        prelude.Accept(stat);
        prelude.Accept(inventory);

        ClientArrivalWatcher watcher = IdentifiedWatcher();
        using var session = new PreLoginCaptureSession(
            prelude,
            watcher,
            capturePath,
            timeout: TimeSpan.FromMilliseconds(300),
            pollInterval: TimeSpan.FromMilliseconds(20));

        PreLoginCaptureOutcome outcome = session.Run();

        Assert.Equal(PreLoginOutcomeKind.Partial, outcome.Kind);
        Assert.NotNull(outcome.Reason);
        Assert.StartsWith("timeout_before_criterion:", outcome.Reason!);
        Assert.Contains("ivn|equip", outcome.Reason!);
        Assert.Contains("ivn|equip", outcome.OpcodesMissing);
        Assert.NotNull(outcome.CapturePath);
        Assert.True(File.Exists(outcome.CapturePath));
    }
}
