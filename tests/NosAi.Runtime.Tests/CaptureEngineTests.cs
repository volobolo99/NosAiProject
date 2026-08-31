using System.Buffers.Binary;
using System.Net;
using System.Text;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The capture engine composed end to end, and the recording that lets a decoder
/// be written offline.
/// </summary>
/// <remarks>
/// A synthetic session stands in for a driver: scripted packets — out of order,
/// retransmitted — go in, and the engine's counts and frames come out. Because
/// the source is an interface, none of this needs WinDivert; the only untested
/// edge is the driver handle itself.
/// </remarks>
public sealed class CaptureEngineTests : IDisposable
{
    private static readonly IPAddress Server = IPAddress.Parse("79.110.84.175");
    private const int ServerPort = 4006;
    private const string Client = "192.168.0.4";
    private const int ClientPort = 56027;

    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try { File.Delete(file); } catch (IOException) { }
        }
    }

    private static byte[] Packet(bool fromServer, uint seq, string payload, bool syn = false)
    {
        string src = fromServer ? Server.ToString() : Client;
        int srcPort = fromServer ? ServerPort : ClientPort;
        string dst = fromServer ? Client : Server.ToString();
        int dstPort = fromServer ? ClientPort : ServerPort;

        var body = Encoding.ASCII.GetBytes(payload);
        var packet = new byte[20 + 20 + body.Length];
        packet[0] = 0x45;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), (ushort)packet.Length);
        packet[9] = 6;
        IPAddress.Parse(src).GetAddressBytes().CopyTo(packet, 12);
        IPAddress.Parse(dst).GetAddressBytes().CopyTo(packet, 16);
        var tcp = packet.AsSpan(20);
        BinaryPrimitives.WriteUInt16BigEndian(tcp.Slice(0, 2), (ushort)srcPort);
        BinaryPrimitives.WriteUInt16BigEndian(tcp.Slice(2, 2), (ushort)dstPort);
        BinaryPrimitives.WriteUInt32BigEndian(tcp.Slice(4, 4), seq);
        tcp[12] = 5 << 4;
        tcp[13] = (byte)(syn ? 0x02 : 0);
        body.CopyTo(tcp[20..]);
        return packet;
    }

    private static CapturedPacket Cap(bool fromServer, uint seq, string payload, bool syn = false) =>
        new(DateTime.UtcNow, Packet(fromServer, seq, payload, syn));

    private InMemoryPacketSource Source(params CapturedPacket[] packets) =>
        new(Server, ServerPort, packets);

    private string TempPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nosai_capture_{Guid.NewGuid():N}.noscap");
        _tempFiles.Add(path);
        return path;
    }

    // ------------------------------------------------------------- the engine

    [Fact]
    public void AScriptedSessionRunsThroughToFrames()
    {
        var engine = new GameTrafficCaptureEngine(Source(
            Cap(true, 99, "", syn: true),      // server SYN anchors inbound
            Cap(true, 100, "hello"),
            Cap(false, 200, "req")));           // one outbound packet, its own stream

        var frames = new List<CaptureFrame>();
        engine.FrameProduced += frames.Add;
        var summary = engine.Run();

        Assert.Equal(3, summary.PacketsRead);
        Assert.Equal(3, summary.PacketsParsed);
        Assert.Equal(0, summary.PacketsRejected);
        Assert.Equal(5, summary.InboundBytes);
        Assert.Equal(3, summary.OutboundBytes);
        Assert.Contains(frames, f => f.Direction == StreamDirection.Inbound);
        Assert.Contains(frames, f => f.Direction == StreamDirection.Outbound);
    }

    [Fact]
    public void OutOfOrderAndRetransmittedPacketsStillProduceTheStream()
    {
        // The point of running through the engine rather than the reassembler
        // alone: the composition handles the mess a live capture delivers.
        var engine = new GameTrafficCaptureEngine(Source(
            Cap(true, 99, "", syn: true),  // captured from the SYN, so the stream is anchored
            Cap(true, 105, "world"),        // later half first, held past the gap
            Cap(true, 100, "hello"),        // fills the gap, releases both
            Cap(true, 100, "hello")));       // pure retransmission, ignored

        var inbound = new List<byte>();
        engine.FrameProduced += f =>
        {
            if (f.Direction == StreamDirection.Inbound)
                inbound.AddRange(f.Frame.Body.ToArray());
        };
        var summary = engine.Run();

        Assert.Equal("helloworld", Encoding.ASCII.GetString(inbound.ToArray()));
        Assert.Equal(10, summary.InboundBytes);
    }

    [Fact]
    public void EveryFrameIsUnknownUntilADecoderExists()
    {
        // ADR-0014: the traffic is read, not yet interpreted. The engine must not
        // dress reassembled bytes as decoded messages.
        var engine = new GameTrafficCaptureEngine(Source(Cap(true, 0, "some bytes")));
        var summary = engine.Run();

        Assert.True(summary.FramesProduced > 0);
        Assert.Equal(summary.FramesProduced, summary.UnknownFrames);
        Assert.False(summary.AnyDecoded);
    }

    [Fact]
    public void APacketForAnotherConversationIsRejectedNotMixedIn()
    {
        // A packet to a different server must not corrupt this stream; it is
        // counted as rejected so an operator sees the filter is off.
        var stranger = new byte[40];
        stranger[0] = 0x45;
        BinaryPrimitives.WriteUInt16BigEndian(stranger.AsSpan(2, 2), 40);
        stranger[9] = 6;
        IPAddress.Parse("10.0.0.1").GetAddressBytes().CopyTo(stranger, 12);
        IPAddress.Parse("93.184.216.34").GetAddressBytes().CopyTo(stranger, 16);
        BinaryPrimitives.WriteUInt16BigEndian(stranger.AsSpan(22, 2), 443);
        stranger[32] = 5 << 4;

        var engine = new GameTrafficCaptureEngine(Source(
            new CapturedPacket(DateTime.UtcNow, stranger),
            Cap(true, 0, "real")));
        var summary = engine.Run();

        Assert.Equal(1, summary.PacketsRejected);
        Assert.Equal(4, summary.InboundBytes);
    }

    [Fact]
    public void ADecoderCanBeSubstitutedWithoutTouchingTheEngine()
    {
        // The seam ADR-0014 leaves for a real NosTale decoder: swap the framer,
        // nothing else changes. A length-prefixed toy framer stands in for one.
        var engine = new GameTrafficCaptureEngine(
            Source(Cap(true, 0, "abcde")),
            direction => new LengthPrefixedFramer(direction));

        var decoded = new List<string>();
        engine.FrameProduced += f =>
        {
            if (f.Frame.Source == DataSourceKind.Live)
                decoded.Add(Encoding.ASCII.GetString(f.Frame.Body.ToArray()));
        };
        var summary = engine.Run();

        Assert.Equal(new[] { "abc", "de" }, decoded);
        Assert.True(summary.AnyDecoded);
    }

    // ---------------------------------------------------------- record + replay

    [Fact]
    public void ARecordingReplaysToTheSameFrames()
    {
        // The bridge that decouples decoding from the driver: capture once, replay
        // offline as often as the decoder needs.
        var path = TempPath();
        var live = Source(
            Cap(true, 99, "", syn: true),
            Cap(true, 100, "hello"),
            Cap(true, 105, " world"));

        long written = CaptureFile.Record(live, path);
        Assert.Equal(3, written);

        using var replay = CaptureFile.Open(path);
        Assert.Equal(Server, replay.ServerAddress);
        Assert.Equal(ServerPort, replay.ServerPort);

        var engine = new GameTrafficCaptureEngine(replay);
        var inbound = new List<byte>();
        engine.FrameProduced += f => inbound.AddRange(f.Frame.Body.ToArray());
        var summary = engine.Run();

        Assert.Equal("hello world", Encoding.ASCII.GetString(inbound.ToArray()));
        Assert.Equal(3, summary.PacketsRead);
    }

    [Fact]
    public void ARecordingPreservesTimestampsAndBytesExactly()
    {
        var path = TempPath();
        var when = new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);
        var raw = Packet(true, 42, "exact");
        var single = new InMemoryPacketSource(Server, ServerPort, new[] { new CapturedPacket(when, raw) });

        CaptureFile.Record(single, path);

        using var replay = CaptureFile.Open(path);
        Assert.True(replay.TryRead(TimeSpan.Zero, out var restored));
        Assert.Equal(when, restored.TimestampUtc);
        Assert.Equal(raw, restored.Raw.ToArray());
        Assert.False(replay.TryRead(TimeSpan.Zero, out _));
        Assert.True(replay.Ended);
    }

    [Fact]
    public void AFileThatIsNotACaptureIsRefused()
    {
        var path = TempPath();
        File.WriteAllText(path, "this is not a capture");

        Assert.Throws<InvalidDataException>(() => CaptureFile.Open(path));
    }

    [Fact]
    public void AnEmptyRecordingReplaysAsNothingRatherThanFailing()
    {
        var path = TempPath();
        CaptureFile.Record(Source(), path); // no packets, header only

        using var replay = CaptureFile.Open(path);
        Assert.False(replay.TryRead(TimeSpan.Zero, out _));
        Assert.True(replay.Ended);
    }

    /// <summary>A trivial length-prefixed framer, only to prove the seam works.</summary>
    private sealed class LengthPrefixedFramer : IGameStreamFramer
    {
        private readonly List<byte> _buffer = new();

        public LengthPrefixedFramer(StreamDirection direction) => Direction = direction;
        public StreamDirection Direction { get; }

        public IReadOnlyList<GameFrame> Consume(ReadOnlySpan<byte> delivered)
        {
            _buffer.AddRange(delivered.ToArray());
            var frames = new List<GameFrame>();
            while (_buffer.Count > 0)
            {
                int length = _buffer[0];
                if (_buffer.Count < 1 + length)
                    break;
                var body = _buffer.GetRange(1, length).ToArray();
                _buffer.RemoveRange(0, 1 + length);
                frames.Add(GameFrame.Live(length, body));
            }
            return frames;
        }
    }
}
