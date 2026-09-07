using System.Buffers.Binary;
using System.Net;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Observability;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// <c>--wire-inspect --fields</c>: the per-field census of one opcode, and the
/// refusal of an option the command does not know.
/// </summary>
public sealed class WireInspectFieldsTests
{
    private static readonly IPAddress Server = IPAddress.Parse("79.110.84.175");
    private const int ServerPort = 4002;
    private const string Client = "192.168.0.4";
    private const int ClientPort = 56027;

    // --------------------------------------------------- synthetic shapes

    [Fact]
    public void The_field_census_reports_constant_varying_min_max_and_examples()
    {
        using IPacketSource source = Recording(
            Encoded("foo 1 20 3"),
            Encoded("foo 1 200 3"),
            Encoded("foo 1 10 4"));
        var output = new StringWriter();

        WireInspectCommand.WriteFieldCensus(output, source, DataSourceKind.Cached, "foo");

        string text = output.ToString();
        Assert.Contains("foo  n=3  campi=3", text, StringComparison.Ordinal);
        Assert.Contains("1  costante=1", text, StringComparison.Ordinal);                       // field 1 never changes
        Assert.Contains("2  distinti=3  min=10  max=200  es=20,200,10", text, StringComparison.Ordinal);
        Assert.Contains("3  distinti=2  min=3  max=4  es=3,4", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Packets_of_different_lengths_are_censused_separately()
    {
        using IPacketSource source = Recording(
            Encoded("bar 1 2"),
            Encoded("bar 1 2 3 4"));
        var output = new StringWriter();

        WireInspectCommand.WriteFieldCensus(output, source, DataSourceKind.Cached, "bar");

        string text = output.ToString();
        Assert.Contains("bar  n=2  campi=2,4", text, StringComparison.Ordinal);
        Assert.Contains("-- campi=2 (n=1) --", text, StringComparison.Ordinal);
        Assert.Contains("-- campi=4 (n=1) --", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_non_numeric_field_is_not_bounded_by_a_min_or_max()
    {
        using IPacketSource source = Recording(
            Encoded("baz 1 #abc"),
            Encoded("baz 1 #def"));
        var output = new StringWriter();

        WireInspectCommand.WriteFieldCensus(output, source, DataSourceKind.Cached, "baz");

        string text = output.ToString();
        Assert.Contains("2  distinti=2  es=#abc,#def  (non numerico)", text, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------- refusals

    [Fact]
    public void An_unknown_option_is_refused_with_its_name()
    {
        string[] args = "--wire-inspect x.noscap --summary".Split(' ', StringSplitOptions.RemoveEmptyEntries);

        string? refusal = WireInspectCommand.Validate(args, out _, out _, out _);
        Assert.Equal("unknown_option:--summary", refusal);
        Assert.NotEqual(0, WireInspectCommand.Run(args));
    }

    [Fact]
    public void Fields_without_an_opcode_is_refused()
    {
        string[] args = "--wire-inspect x.noscap --fields".Split(' ', StringSplitOptions.RemoveEmptyEntries);

        string? refusal = WireInspectCommand.Validate(args, out _, out _, out _);
        Assert.Equal(WireInspectCommand.FieldsWithoutOpcodeReason, refusal);
    }

    // ------------------------------------------------------- recorded shape

    [RecordedCaptureFact("nostale_combat.noscap")]
    public void The_stat_field_census_marks_field_five_constant_and_field_one_varying()
    {
        string path = RecordedCaptureFactAttribute.Resolve("nostale_combat.noscap")!;
        using IPacketSource source = CaptureFile.Open(path);
        var output = new StringWriter();

        WireInspectCommand.Inspect(source, output, DataSourceKind.Cached, opcode: "stat", maxLines: 20, showFields: true);

        string text = output.ToString();
        Assert.Contains("5  costante=0", text, StringComparison.Ordinal);   // stat field 5 is 0 everywhere
        Assert.Contains("1  distinti=", text, StringComparison.Ordinal);    // stat field 1 (HP) varies
    }

    // ------------------------------------------------------------- helpers

    private static IPacketSource Recording(params byte[][] bodies)
    {
        var packets = new List<CapturedPacket>();
        uint seq = 1000;
        var when = new DateTime(2026, 9, 6, 10, 0, 0, DateTimeKind.Utc);
        foreach (byte[] body in bodies)
        {
            packets.Add(new CapturedPacket(when, TcpPacket(seq, body)));
            seq += (uint)body.Length;
        }
        return new InMemoryPacketSource(Server, ServerPort, packets);
    }

    private static byte[] TcpPacket(uint seq, ReadOnlySpan<byte> body)
    {
        var packet = new byte[20 + 20 + body.Length];
        packet[0] = 0x45;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), (ushort)packet.Length);
        packet[9] = 6;
        Server.GetAddressBytes().CopyTo(packet, 12);
        IPAddress.Parse(Client).GetAddressBytes().CopyTo(packet, 16);
        var tcp = packet.AsSpan(20);
        BinaryPrimitives.WriteUInt16BigEndian(tcp[..2], ServerPort);
        BinaryPrimitives.WriteUInt16BigEndian(tcp.Slice(2, 2), ClientPort);
        BinaryPrimitives.WriteUInt32BigEndian(tcp.Slice(4, 4), seq);
        tcp[12] = 5 << 4;
        body.CopyTo(tcp[20..]);
        return packet;
    }

    /// <summary>Encodes a line the way the server does: literal branch, complemented, terminator.</summary>
    private static byte[] Encoded(string line)
    {
        var bytes = new List<byte>();
        foreach (string chunk in Chunks(line, 0x7F))
        {
            bytes.Add((byte)chunk.Length);
            foreach (char c in chunk)
                bytes.Add((byte)(c ^ 0xFF));
        }
        bytes.Add(NosAi.Runtime.Perception.Network.NosTaleWorldDecoder.PacketTerminator);
        return bytes.ToArray();
    }

    private static IEnumerable<string> Chunks(string text, int size)
    {
        for (int i = 0; i < text.Length; i += size)
            yield return text.Substring(i, Math.Min(size, text.Length - i));
    }
}
