using System.Buffers.Binary;
using System.Net;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.Contracts;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The live decoder feed: one <c>RETE</c> line per opcode on the wire and one
/// <c>CLIENT</c> line per opcode <see cref="NosTaleWorldProtocolDecoder"/>
/// actually reads, both printed while the source is still open rather than
/// after it ends.
/// </summary>
/// <remarks>
/// No driver here: <see cref="InMemoryPacketSource"/> plays the same role it
/// plays for <see cref="WorldChannelReplayTests"/> — a real, finite
/// <see cref="IPacketSource"/> that lets <see cref="LiveWireMonitor.Monitor"/>
/// run to completion with no WinDivert involved. Only
/// <see cref="LiveWireMonitor.Run"/> needs a real device, and that is
/// deliberately the one thing not exercised here.
/// </remarks>
public sealed class LiveWireMonitorTests
{
    private static readonly IPAddress Server = IPAddress.Parse("79.110.84.175");
    private const int ServerPort = 4002;
    private const string Client = "192.168.0.4";
    private const int ClientPort = 56027;

    private static byte[] TcpPacket(uint seq, ReadOnlySpan<byte> body, bool outbound = false)
    {
        var packet = new byte[20 + 20 + body.Length];
        packet[0] = 0x45;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2, 2), (ushort)packet.Length);
        packet[9] = 6;
        IPAddress source = outbound ? IPAddress.Parse(Client) : Server;
        IPAddress destination = outbound ? Server : IPAddress.Parse(Client);
        source.GetAddressBytes().CopyTo(packet, 12);
        destination.GetAddressBytes().CopyTo(packet, 16);
        var tcp = packet.AsSpan(20);
        BinaryPrimitives.WriteUInt16BigEndian(tcp[..2], (ushort)(outbound ? ClientPort : ServerPort));
        BinaryPrimitives.WriteUInt16BigEndian(tcp.Slice(2, 2), (ushort)(outbound ? ServerPort : ClientPort));
        BinaryPrimitives.WriteUInt32BigEndian(tcp.Slice(4, 4), seq);
        tcp[12] = 5 << 4;
        body.CopyTo(tcp[20..]);
        return packet;
    }

    /// <summary>
    /// Encodes a line the way the server does: a length byte with the literal
    /// branch selected, each byte complemented, then the terminator.
    /// </summary>
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

    private static IPacketSource Recording(params (byte[] Body, bool Outbound)[] frames)
    {
        var packets = new List<CapturedPacket>();
        uint seq = 1000;
        var when = new DateTime(2026, 9, 6, 10, 0, 0, DateTimeKind.Utc);
        foreach ((byte[] body, bool outbound) in frames)
        {
            packets.Add(new CapturedPacket(when, TcpPacket(seq, body, outbound)));
            seq += (uint)body.Length;
        }
        return new InMemoryPacketSource(Server, ServerPort, packets);
    }

    [Fact]
    public void A_known_opcode_prints_both_the_raw_line_and_the_client_reading()
    {
        using IPacketSource source = Recording((Encoded("stat 100 7305 50 500"), false));
        var output = new StringWriter();

        LiveWireMonitor.Summary summary = LiveWireMonitor.Monitor(source, output);

        string text = output.ToString();
        Assert.Contains("RETE   stat 100 7305 50 500", text, StringComparison.Ordinal);
        Assert.Contains("CLIENT vitals hp=100/7305 mp=50/500", text, StringComparison.Ordinal);
        Assert.Equal(1, summary.ReadableFrames);
        Assert.Equal(1, summary.Interpreted);
        Assert.Equal(0, summary.NotInterpreted);
        Assert.Equal(0, summary.Undecipherable);
    }

    [Fact]
    public void An_opcode_the_decoder_does_not_read_still_prints_its_raw_line_but_no_client_line()
    {
        // 'guri' is a real opcode nobody has established the meaning of (see
        // WorldChannelReplayTests). It must be visible, not silently dropped —
        // the whole point of RETE beside CLIENT.
        using IPacketSource source = Recording((Encoded("guri 2 1 3443217 0"), false));
        var output = new StringWriter();

        LiveWireMonitor.Summary summary = LiveWireMonitor.Monitor(source, output);

        string text = output.ToString();
        Assert.Contains("RETE   guri 2 1 3443217 0", text, StringComparison.Ordinal);
        Assert.DoesNotContain("CLIENT", text, StringComparison.Ordinal);
        Assert.Equal(1, summary.ReadableFrames);
        Assert.Equal(0, summary.Interpreted);
        Assert.Equal(1, summary.NotInterpreted);
    }

    [Fact]
    public void An_outbound_frame_is_marked_undecipherable_rather_than_printed_as_bytes()
    {
        // Client -> server is separately encrypted on this wire and is never
        // framed as NosTale text (docs/PROTOCOLLO_NOSTALE.md); the framer marks
        // it Unknown and this must say so rather than guess at its content.
        using IPacketSource source = Recording((new byte[] { 0x01, 0x02, 0x03, 0x04 }, true));
        var output = new StringWriter();

        LiveWireMonitor.Summary summary = LiveWireMonitor.Monitor(source, output);

        string text = output.ToString();
        Assert.Contains("<non decifrabile>", text, StringComparison.Ordinal);
        Assert.Equal(0, summary.ReadableFrames);
        Assert.Equal(1, summary.Undecipherable);
    }

    /// <summary>
    /// Byte registrati non escono mai etichettati <c>Live</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>Monitor</c> ha preso un parametro di provenienza il 2026-09-07, insieme
    /// a <c>--wire-inspect</c>: prima costruiva il framer con
    /// <see cref="DataSourceKind.Live"/> fisso, corretto finche' l'unico chiamante
    /// era il percorso col driver, e laundering di provenienza nel momento in cui
    /// gli si desse un file. Il parametro e' arrivato senza una prova, e un
    /// parametro di sicurezza senza prova e' una promessa.
    /// </para>
    /// <para>
    /// Il default resta <c>Live</c> perche' il percorso vivo non deve cambiare, e
    /// il primo dei due test qui sotto lo fissa: se qualcuno invertisse il
    /// default, <c>--live-decode</c> comincerebbe a dichiarare <c>Cached</c> del
    /// traffico che sta guardando adesso.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheDefaultProvenanceIsLiveSoTheDriverPathIsUnchanged()
    {
        using IPacketSource source = Recording((Encoded("stat 100 7305 50 500"), false));
        var output = new StringWriter();

        LiveWireMonitor.Summary summary = LiveWireMonitor.Monitor(source, output);

        Assert.Equal(1, summary.ReadableFrames);
        Assert.Equal(0, summary.Undecipherable);
    }

    [Fact]
    public void ARecordedSourceIsNeverFramedAsLive()
    {
        using IPacketSource source = Recording((Encoded("stat 100 7305 50 500"), false));
        var output = new StringWriter();

        LiveWireMonitor.Summary live = LiveWireMonitor.Monitor(source, output, sourceKind: DataSourceKind.Live);

        using IPacketSource replayed = Recording((Encoded("stat 100 7305 50 500"), false));
        var cachedOutput = new StringWriter();
        LiveWireMonitor.Summary cached =
            LiveWireMonitor.Monitor(replayed, cachedOutput, sourceKind: DataSourceKind.Cached);

        // Lo stesso pacchetto, letto due volte con due provenienze: la riga
        // grezza e la lettura semantica devono essere identiche -- la
        // provenienza non cambia cosa dice il filo -- e nessuna delle due
        // esecuzioni deve perdere il frame.
        Assert.Equal(live.ReadableFrames, cached.ReadableFrames);
        Assert.Equal(live.Interpreted, cached.Interpreted);
        Assert.Contains("RETE   stat 100 7305 50 500", cachedOutput.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheRuntimeWiresTheLiveDecodeFlag()
    {
        string source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "NosAi.Runtime", "Program.cs"));
        Assert.Contains("LiveWireMonitor.Flag", source, StringComparison.Ordinal);
        Assert.Contains("LiveWireMonitor.Run", source, StringComparison.Ordinal);
        Assert.Equal("--live-decode", LiveWireMonitor.Flag);
    }

    private static string RepositoryRoot()
    {
        // Il marcatore e' NosAi.sln, la radice canonica di un repo .NET.
        // Prima era CLAUDE.md, che il 2026-09-09 e' stato spostato in .claude/:
        // la risalita non trovava piu' nulla e il test moriva su Assert.NotNull
        // senza mai arrivare a controllare il cablaggio di Program.cs.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
