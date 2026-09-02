using System.Buffers.Binary;
using System.Net;
using NosAi.LiveIntegration.Capture;
using NosAi.Runtime.GameData;
using NosAi.Runtime.Observability;
using NosAi.Runtime.Perception.Network;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// <c>--world-replay</c>: the observation table, including the empty-contract case.
/// </summary>
public sealed class WorldReplayCommandTests : IDisposable
{
    private static readonly IPAddress Server = IPAddress.Parse("79.110.84.175");
    private const int ServerPort = 4002;
    private const string Client = "192.168.0.4";
    private const int ClientPort = 56027;

    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "nosai-world-replay-" + Guid.NewGuid().ToString("N"));

    public WorldReplayCommandTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void TheRuntimeWiresTheWorldReplayFlag()
    {
        string program = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "NosAi.Runtime", "Program.cs"));
        Assert.Contains("WorldReplayCommand.Run", program, StringComparison.Ordinal);
        Assert.Contains("\"--world-replay\"", program, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyContractPrintsZeroAndDoesNotFail()
    {
        WorldReplayReport report = WorldReplayCommand.Inspect(Recording());
        string text = WorldReplayCommand.Format(report);

        Assert.True(report.Ok);
        Assert.Empty(report.Entities);
        Assert.Empty(report.Hits);
        Assert.Empty(report.SkillsReady);
        Assert.Empty(report.Inventory);
        Assert.Empty(report.Pickups);
        Assert.Empty(report.GroundItems);
        Assert.Equal(0, report.SelectionCount);
        Assert.Equal(WorldReplayCommand.CtNotOnObservation, report.SelectionReason);
        Assert.Contains("entities: 0  reason=nothing_sighted", text, StringComparison.Ordinal);
        Assert.Contains("aggressors: 0  reason=player_hit_empty", text, StringComparison.Ordinal);
        Assert.Contains("selections (ct): 0  reason=ct_not_on_observation", text, StringComparison.Ordinal);
        Assert.Contains("cooldowns (sr): 0  reason=skill_ready_empty", text, StringComparison.Ordinal);
        Assert.Contains("inventory (ivn): 0  reason=inventory_slot_empty", text, StringComparison.Ordinal);
        Assert.Contains("pickups (get): 0  reason=item_pickup_empty", text, StringComparison.Ordinal);
        Assert.Contains("ground (drop): 0  reason=ground_item_empty", text, StringComparison.Ordinal);
        Assert.Contains("packets: observed=0", text, StringComparison.Ordinal);
        Assert.DoesNotContain("entities: 1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingFileIsNonZero()
    {
        string path = Path.Combine(_dir, "absent.noscap");
        WorldReplayReport report = WorldReplayCommand.InspectFile(path);
        Assert.False(report.Ok);
        Assert.Equal("recording_not_found", report.FailureReason);
        Assert.Equal(WorldReplayCommand.ExitUnreadable, WorldReplayCommand.Run(path));
    }

    [Fact]
    public void AnUnreadableFileIsNonZero()
    {
        string path = Path.Combine(_dir, "junk.noscap");
        File.WriteAllText(path, "not a noscap");
        WorldReplayReport report = WorldReplayCommand.InspectFile(path);
        Assert.False(report.Ok);
        Assert.StartsWith("recording_unreadable:", report.FailureReason, StringComparison.Ordinal);
        Assert.Equal(WorldReplayCommand.ExitUnreadable, WorldReplayCommand.Run(path));
    }

    [Fact]
    public void AnEntityRowNamesVnumAbsenceExplicitly()
    {
        WorldReplayReport report = WorldReplayCommand.Inspect(Recording(Encoded("in 3 221 999 10 20 0 80 100")));
        string text = WorldReplayCommand.Format(report);

        Assert.True(report.Ok);
        WorldReplayEntityRow row = Assert.Single(report.Entities);
        Assert.Equal(999, row.EntityId);
        Assert.Equal(10, row.X);
        Assert.Equal(20, row.Y);
        Assert.True(
            row.VnumText is WorldReplayCommand.VnumNotRead
                or WorldReplayCommand.VnumAbsent
                || row.VnumText.StartsWith("vnum=", StringComparison.Ordinal),
            row.VnumText);
        Assert.Contains("UNKNOWN", row.NameText, StringComparison.Ordinal);
        Assert.Contains("id=999", text, StringComparison.Ordinal);
        Assert.Contains("pos=10,20", text, StringComparison.Ordinal);
    }

    [Fact]
    public void DisplayNameIsUsedWhenTheCatalogKnowsTheVnum()
    {
        using GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        db.Import(
            "monster", "test.NOS", "monster.dat", "C:/test",
            [new NosRecord(221, [
                new NosField("VNUM", ["221"]),
                new NosField("LEVEL", ["1"]),
                new NosField("NAME", ["zts1e"])
            ])],
            "payload"u8.ToArray());

        // NAME is a key; without a language row DisplayName stays null. The
        // command must still say UNKNOWN rather than print the key.
        WorldReplayReport report = WorldReplayCommand.Inspect(
            Recording(Encoded("in 3 221 999 10 20 0 80 100")), db);
        WorldReplayEntityRow row = Assert.Single(report.Entities);
        Assert.Contains("UNKNOWN", row.NameText, StringComparison.Ordinal);
        Assert.DoesNotContain("zts1e", row.NameText, StringComparison.Ordinal);
    }

    [Fact]
    public void PostConditionContractsArePrintedWhenTheWireCarriesThem()
    {
        WorldReplayReport report = WorldReplayCommand.Inspect(Recording(
            Encoded("sr 2"),
            Encoded("ivn 2 34.2006.1.0"),
            Encoded("get 1 3443217 1092257 0"),
            Encoded("drop 2006 1092257 11 12 1 0 3443217")));
        string text = WorldReplayCommand.Format(report);

        Assert.True(report.Ok);
        Assert.Single(report.SkillsReady);
        Assert.Equal(2, report.SkillsReady[0].Slot);
        Assert.Single(report.Inventory);
        Assert.Equal(2006, report.Inventory[0].Vnum);
        Assert.Single(report.Pickups);
        Assert.Single(report.GroundItems);
        Assert.Contains("cooldowns (sr): 1", text, StringComparison.Ordinal);
        Assert.Contains("inventory (ivn): 1", text, StringComparison.Ordinal);
        Assert.Contains("pickups (get): 1", text, StringComparison.Ordinal);
        Assert.Contains("ground (drop): 1", text, StringComparison.Ordinal);
        Assert.Contains("packets:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AReadableEmptyFileExitsZero()
    {
        string path = Path.Combine(_dir, "empty.noscap");
        using (var source = new InMemoryPacketSource(Server, ServerPort, Array.Empty<CapturedPacket>()))
            CaptureFile.Record(source, path);

        Assert.Equal(0, WorldReplayCommand.Run(path));
    }

    private static Func<IPacketSource> Recording(params byte[][] bodies)
    {
        var packets = new List<CapturedPacket>();
        uint seq = 1000;
        foreach (byte[] body in bodies)
        {
            packets.Add(new CapturedPacket(new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc), TcpPacket(seq, body)));
            seq += (uint)body.Length;
        }

        return () => new InMemoryPacketSource(Server, ServerPort, packets);
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

    private static byte[] Encoded(string line)
    {
        var bytes = new List<byte>();
        foreach (string chunk in Chunks(line, 0x7F))
        {
            bytes.Add((byte)chunk.Length);
            foreach (char c in chunk)
                bytes.Add((byte)(c ^ 0xFF));
        }

        bytes.Add(NosTaleWorldDecoder.PacketTerminator);
        return bytes.ToArray();
    }

    private static IEnumerable<string> Chunks(string text, int size)
    {
        for (int i = 0; i < text.Length; i += size)
            yield return text.Substring(i, Math.Min(size, text.Length));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;
        Assert.True(directory is not null, "Repository root not found.");
        return directory!.FullName;
    }
}
