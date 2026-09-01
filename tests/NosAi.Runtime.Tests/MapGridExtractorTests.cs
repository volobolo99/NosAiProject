using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using NosAi.Runtime.Navigation;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Extraction of <c>.grid</c> files from numbered archives, and reloading every
/// file that was written.
/// </summary>
public sealed class MapGridExtractorTests
{
    [Fact]
    public void AWellFormedArchiveEntryIsWrittenAndReloads()
    {
        byte[] gridFile = BuildGrid(3, 2, 0, 1, 2, 3, 4, 5);
        using TempDir dir = TempDir.Create();
        WriteArchive(dir.Client, "NStcData01.NOS", (1024, gridFile));

        MapGridExtractReport report = MapGridExtractor.Extract(dir.Client, dir.Output);

        Assert.True(report.Ok, report.FailureReason);
        Assert.Single(report.Written);
        Assert.Equal(1024, report.Written[0].MapId);
        Assert.Equal(3, report.Written[0].Width);
        Assert.Equal(2, report.Written[0].Height);

        Assert.True(MapGridExtractor.TryInfo(dir.Output, 1024, out MapGrid grid, out string? hash, out string? reason), reason);
        Assert.Equal(MapGridSetIdentity.HashFile(gridFile), hash);
        Assert.Equal(1, grid.RawAt(1, 0));
        Assert.Equal(5, grid.RawAt(2, 1));
    }

    [Fact]
    public void EveryWrittenFileLoadsThroughTheBinaryLoader()
    {
        using TempDir dir = TempDir.Create();
        WriteArchive(dir.Client, "NStcData00.NOS",
            (1, BuildGrid(2, 1, 0x00, 0x01)),
            (2, BuildGrid(1, 2, 0x02, 0x03)));

        MapGridExtractReport report = MapGridExtractor.Extract(dir.Client, dir.Output);
        Assert.Equal(2, report.Written.Count);

        var loader = new BinaryMapGridLoader();
        string[] files = Directory.GetFiles(dir.Output, "*.grid");
        Assert.Equal(2, files.Length);

        foreach (string path in files)
        {
            int mapId = int.Parse(Path.GetFileNameWithoutExtension(path));
            byte[] bytes = File.ReadAllBytes(path);
            Assert.True(loader.TryLoad(mapId, bytes, out MapGrid grid, out string? reason), reason);
            Assert.True(grid.IsLoaded);
            Assert.Equal(mapId, grid.MapId);
        }
    }

    [Fact]
    public void AZlibWrappedGridInflatesAndLoads()
    {
        byte[] inner = BuildGrid(2, 2, 1, 2, 3, 4);
        byte[] wrapped = WrapZlib(inner);
        using TempDir dir = TempDir.Create();
        WriteArchive(dir.Client, "NStcData01.NOS", (7, wrapped));

        MapGridExtractReport report = MapGridExtractor.Extract(dir.Client, dir.Output);

        Assert.True(report.Ok, report.FailureReason);
        Assert.Single(report.Written);
        Assert.True(MapGridExtractor.TryInfo(dir.Output, 7, out MapGrid grid, out _, out string? reason), reason);
        Assert.Equal(2, grid.Width);
        Assert.Equal(4, grid.RawAt(1, 1));
    }

    [Fact]
    public void ATruncatedPayloadIsRefusedAndWritesNothing()
    {
        using TempDir dir = TempDir.Create();
        WriteArchive(dir.Client, "NStcData01.NOS", (3, BuildGridHeaderOnly(4, 4)));

        MapGridExtractReport report = MapGridExtractor.Extract(dir.Client, dir.Output);

        Assert.True(report.Ok, report.FailureReason);
        Assert.Empty(report.Written);
        Assert.Contains(report.Refused, r => r.MapId == 3 && r.Reason == MapGridFormat.PayloadTruncated);
        Assert.Empty(Directory.GetFiles(dir.Output, "*.grid"));
    }

    [Fact]
    public void TheManifestRecordsPerFileHashes()
    {
        byte[] file = BuildGrid(1, 1, 0x01);
        using TempDir dir = TempDir.Create();
        WriteArchive(dir.Client, "NStcData00.NOS", (9, file));

        MapGridExtractReport report = MapGridExtractor.Extract(dir.Client, dir.Output);
        string manifest = File.ReadAllText(Path.Combine(dir.Output, MapGridExtractor.ManifestFileName));

        Assert.StartsWith($"{MapGridExtractor.ManifestMagic} {MapGridExtractor.ManifestVersion}", manifest);
        Assert.Contains($"\n9 {report.Written[0].Sha256}", "\n" + manifest);
        Assert.Equal(MapGridSetIdentity.HashFile(file), report.Written[0].Sha256);
    }

    [Fact]
    public void AMissingClientDirectoryIsANamedRefusal()
    {
        using TempDir dir = TempDir.Create();
        string missing = Path.Combine(dir.Root, "no-such-client");

        MapGridExtractReport report = MapGridExtractor.Extract(missing, dir.Output);

        Assert.False(report.Ok);
        Assert.StartsWith(MapGridExtractor.ClientDataNotFound, report.FailureReason);
    }

    [Fact]
    public void ExtractDoesNotModifyTheArchive()
    {
        byte[] gridFile = BuildGrid(2, 1, 1, 2);
        using TempDir dir = TempDir.Create();
        string archive = WriteArchive(dir.Client, "NStcData01.NOS", (1, gridFile));
        byte[] before = File.ReadAllBytes(archive);

        MapGridExtractor.Extract(dir.Client, dir.Output);

        Assert.Equal(before, File.ReadAllBytes(archive));
    }

    private static byte[] BuildGrid(int width, int height, params byte[] cells)
    {
        var file = new byte[MapGridFormat.HeaderBytes + cells.Length];
        file[0] = (byte)(width & 0xFF);
        file[1] = (byte)((width >> 8) & 0xFF);
        file[2] = (byte)(height & 0xFF);
        file[3] = (byte)((height >> 8) & 0xFF);
        cells.CopyTo(file.AsSpan(MapGridFormat.HeaderBytes));
        return file;
    }

    private static byte[] BuildGridHeaderOnly(int width, int height) => BuildGrid(width, height);

    private static byte[] WrapZlib(byte[] plain)
    {
        using var packed = new MemoryStream();
        using (var zlib = new ZLibStream(packed, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(plain, 0, plain.Length);

        byte[] stream = packed.ToArray();
        var wrapped = new byte[13 + stream.Length];
        wrapped[0] = 0x15;
        wrapped[1] = 0x04;
        wrapped[2] = 0x03;
        wrapped[3] = 0x20;
        BinaryPrimitives.WriteInt32LittleEndian(wrapped.AsSpan(4), plain.Length);
        BinaryPrimitives.WriteInt32LittleEndian(wrapped.AsSpan(8), stream.Length);
        wrapped[12] = 0x01;
        stream.CopyTo(wrapped.AsSpan(13));
        return wrapped;
    }

    private static string WriteArchive(string directory, string name, params (int Id, byte[] Payload)[] entries)
    {
        var buffer = new List<byte>();
        buffer.AddRange(Encoding.ASCII.GetBytes("NT Data 02"));
        buffer.AddRange(new byte[] { 0, 0 });
        buffer.AddRange(BitConverter.GetBytes(0x20040715u));
        buffer.AddRange(BitConverter.GetBytes((uint)entries.Length));
        buffer.Add(0);

        const int header = 21;
        long at = header + (entries.Length * 8L);
        foreach ((int id, byte[] payload) in entries)
        {
            buffer.AddRange(BitConverter.GetBytes((uint)id));
            buffer.AddRange(BitConverter.GetBytes((uint)at));
            at += payload.Length;
        }

        foreach ((_, byte[] payload) in entries)
            buffer.AddRange(payload);

        string path = Path.Combine(directory, name);
        File.WriteAllBytes(path, buffer.ToArray());
        return path;
    }

    private sealed class TempDir : IDisposable
    {
        public string Root { get; }
        public string Client => Path.Combine(Root, "client");
        public string Output => Path.Combine(Root, "out");

        private TempDir(string root) => Root = root;

        public static TempDir Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "nosai-maps-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "client"));
            Directory.CreateDirectory(Path.Combine(root, "out"));
            return new TempDir(root);
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (IOException) { }
        }
    }
}
