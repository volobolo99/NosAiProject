using System.Buffers.Binary;
using System.Net;
using System.Text;
using NosAi.LiveIntegration.Capture;
using Xunit;
using Xunit.Abstractions;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Measuring a capture without interpreting it.
/// </summary>
/// <remarks>
/// The analyzer's value is that its numbers are trustworthy, so the two things
/// worth testing are that it measures the reassembled stream (retransmissions and
/// reordering must not skew the distribution) and that it never dresses a
/// measurement as a meaning.
/// </remarks>
public sealed class CaptureAnalyzerTests
{
    private readonly ITestOutputHelper _output;

    public CaptureAnalyzerTests(ITestOutputHelper output) => _output = output;

    private static readonly IPAddress Server = IPAddress.Parse("79.110.84.175");
    private const int ServerPort = 4006;
    private const string Client = "192.168.0.4";

    private static byte[] Packet(bool fromServer, uint seq, byte[] payload, bool syn = false)
    {
        string src = fromServer ? Server.ToString() : Client;
        int srcPort = fromServer ? ServerPort : 56027;
        string dst = fromServer ? Client : Server.ToString();
        int dstPort = fromServer ? 56027 : ServerPort;

        var packet = new byte[20 + 20 + payload.Length];
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
        payload.CopyTo(tcp[20..]);
        return packet;
    }

    private static CapturedPacket Cap(DateTime when, bool fromServer, uint seq, byte[] payload, bool syn = false) =>
        new(when, Packet(fromServer, seq, payload, syn));

    private static InMemoryPacketSource Source(params CapturedPacket[] packets) =>
        new(Server, ServerPort, packets);

    [Fact]
    public void ItMeasuresEachDirectionSeparately()
    {
        var t0 = new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);
        var analysis = CaptureAnalyzer.Analyze(Source(
            Cap(t0, true, 99, Array.Empty<byte>(), syn: true),
            Cap(t0.AddSeconds(1), true, 100, Encoding.ASCII.GetBytes("hello")),
            Cap(t0.AddSeconds(2), false, 50, Encoding.ASCII.GetBytes("ab"))));

        Evidence.Live(_output, "entrata", $"{analysis.Inbound.PacketCount} pacchetti / {analysis.Inbound.TotalBytes} byte");
        Evidence.Live(_output, "uscita", $"{analysis.Outbound.PacketCount} pacchetti / {analysis.Outbound.TotalBytes} byte");

        Assert.Equal(1, analysis.Inbound.PacketCount);
        Assert.Equal(5, analysis.Inbound.TotalBytes);
        Assert.Equal(1, analysis.Outbound.PacketCount);
        Assert.Equal(2, analysis.Outbound.TotalBytes);
    }

    [Fact]
    public void ItMeasuresTheReassembledStreamNotRawPackets()
    {
        // A retransmission must not double-count, and out-of-order delivery must
        // not appear as two short reads where there was one contiguous run. This
        // is why the analyzer reassembles first.
        var t0 = new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);
        var analysis = CaptureAnalyzer.Analyze(Source(
            Cap(t0, true, 99, Array.Empty<byte>(), syn: true),
            Cap(t0, true, 105, Encoding.ASCII.GetBytes("world")),   // out of order
            Cap(t0, true, 100, Encoding.ASCII.GetBytes("hello")),   // fills the gap
            Cap(t0, true, 100, Encoding.ASCII.GetBytes("hello"))));   // retransmission

        // Ten bytes total ("hello" + "world"), counted once, not fifteen.
        Assert.Equal(10, analysis.Inbound.TotalBytes);
    }

    [Fact]
    public void ADominantFirstByteIsSurfacedAsAHintNotAConclusion()
    {
        // If a protocol prefixes each message with the same opcode, the analyzer
        // should make that visible — as a candidate, which the type name and the
        // summary both keep it.
        var t0 = new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);
        var packets = new List<CapturedPacket> { Cap(t0, true, 99, Array.Empty<byte>(), syn: true) };
        uint seq = 100;
        for (var i = 0; i < 5; i++)
        {
            var msg = new byte[] { 0x42, (byte)i, (byte)i };
            packets.Add(Cap(t0, true, seq, msg));
            seq += (uint)msg.Length;
        }

        var analysis = CaptureAnalyzer.Analyze(Source(packets.ToArray()));

        // The stream is one contiguous run, so its first delivered byte is 0x42.
        Evidence.Live(_output, "primoBytePrevalente", $"0x{analysis.Inbound.DominantFirstByte:X2}",
            "indizio per ricavare l'inquadramento del protocollo");

        Assert.Equal((byte)0x42, analysis.Inbound.DominantFirstByte);
        Assert.Contains("candidato", analysis.Describe());
        Assert.Contains("nessuna interpretazione", analysis.Describe());
    }

    [Fact]
    public void NoiseFromAnotherConversationIsCountedAsRejectedNotMeasured()
    {
        // A capture that is mostly packets for another endpoint must not look like
        // a clean protocol; the reject count is how that shows.
        var t0 = new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);
        var stranger = new byte[40];
        stranger[0] = 0x45;
        BinaryPrimitives.WriteUInt16BigEndian(stranger.AsSpan(2, 2), 40);
        stranger[9] = 6;
        IPAddress.Parse("10.0.0.1").GetAddressBytes().CopyTo(stranger, 12);
        IPAddress.Parse("93.184.216.34").GetAddressBytes().CopyTo(stranger, 16);
        BinaryPrimitives.WriteUInt16BigEndian(stranger.AsSpan(22, 2), 443);
        stranger[32] = 5 << 4;

        var analysis = CaptureAnalyzer.Analyze(Source(
            new CapturedPacket(t0, stranger),
            Cap(t0, true, 0, Encoding.ASCII.GetBytes("real"))));

        Assert.Equal(1, analysis.PacketsRejected);
        Assert.Equal(4, analysis.Inbound.TotalBytes);
    }

    [Fact]
    public void AnEmptyCaptureMeasuresToZeroWithoutDividingByZero()
    {
        var analysis = CaptureAnalyzer.Analyze(Source());

        Assert.Equal(0, analysis.Inbound.PacketCount);
        Assert.Equal(0, analysis.Inbound.MeanPayload);
        Assert.Equal(0, analysis.Inbound.MinPayload);
        Assert.Null(analysis.Inbound.DominantFirstByte);
    }

    [Fact]
    public void ItReadsARecordingFromDisk()
    {
        var path = Path.Combine(Path.GetTempPath(), $"nosai_analyze_{Guid.NewGuid():N}.noscap");
        try
        {
            var t0 = new DateTime(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);
            CaptureFile.Record(Source(
                Cap(t0, true, 99, Array.Empty<byte>(), syn: true),
                Cap(t0, true, 100, Encoding.ASCII.GetBytes("payload"))), path);

            var analysis = CaptureAnalyzer.AnalyzeFile(path);

            Assert.Equal(7, analysis.Inbound.TotalBytes);
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }
}
