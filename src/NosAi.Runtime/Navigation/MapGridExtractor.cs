using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using NosAi.Runtime.GameData;

namespace NosAi.Runtime.Navigation;

/// <summary>One extracted map, or why it could not be written.</summary>
public sealed record MapGridExtractedFile(int MapId, string Path, string Sha256, int Width, int Height);

/// <summary>The outcome of extracting every map archive in a client install.</summary>
public sealed record MapGridExtractReport(
    string ClientDataDirectory,
    string OutputDirectory,
    string ClientFingerprint,
    IReadOnlyList<MapGridExtractedFile> Written,
    IReadOnlyList<(int MapId, string Archive, string Reason)> Refused,
    string? FailureReason)
{
    public bool Ok => FailureReason is null;
}

/// <summary>
/// Reads map entries out of the client's numbered archives and writes them as
/// <c>.grid</c> files, with a manifest of per-file hashes.
/// </summary>
/// <remarks>
/// <para>
/// The on-disk format is the one <see cref="BinaryMapGridLoader"/> reads. An
/// archive entry is accepted only when it already is that format, or when it is
/// the same zlib wrap <see cref="NosDataTable"/> already documented and the
/// inflated bytes are that format. Anything else is refused with a
/// <see cref="MapGridFormat"/> token — not decoded by guess. A guessed walkability
/// map would plan across walls the client never had.
/// </para>
/// <para>
/// Destination: <c>&lt;NOSAI-SSD&gt;\NosAi\data\maps\&lt;mapId&gt;.grid</c> when
/// the dedicated volume is present. Tests pass an output directory of their own
/// rather than inventing a drive letter.
/// </para>
/// </remarks>
public static class MapGridExtractor
{
    public const string VolumeLabel = "NOSAI-SSD";
    public const string ManifestFileName = "maps.manifest";
    public const string ManifestMagic = "nosai-map-grids";
    public const int ManifestVersion = MapGridSetIdentity.FormatVersion;
    public const string ArchiveSearchPattern = "NStcData*.NOS";

    public const string VolumeNotFound = "nosai_ssd_not_found";
    public const string ClientDataNotFound = "client_data_directory_not_found";
    public const string NoMapArchives = "no_map_archives";
    public const string DuplicateMapId = "duplicate_map_id";
    public const string InflateFailed = "grid_payload_inflate_failed";

    /// <summary>
    /// <c>&lt;NOSAI-SSD&gt;\NosAi\data\maps</c>, or a reason the volume is absent.
    /// </summary>
    public static bool TryResolveDedicatedMapsDirectory(out string path, out string? failureReason)
    {
        path = "";
        try
        {
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady)
                    continue;
                if (!string.Equals(drive.VolumeLabel, VolumeLabel, StringComparison.OrdinalIgnoreCase))
                    continue;

                path = Path.Combine(drive.RootDirectory.FullName, "NosAi", "data", "maps");
                failureReason = null;
                return true;
            }
        }
        catch (IOException ex)
        {
            failureReason = $"{VolumeNotFound}:{ex.GetType().Name}";
            return false;
        }

        failureReason = VolumeNotFound;
        return false;
    }

    /// <summary>Writes every decodable map from <paramref name="clientDataDirectory"/>.</summary>
    public static MapGridExtractReport Extract(string clientDataDirectory, string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientDataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        if (!Directory.Exists(clientDataDirectory))
        {
            return new MapGridExtractReport(
                clientDataDirectory, outputDirectory, "",
                Array.Empty<MapGridExtractedFile>(),
                Array.Empty<(int, string, string)>(),
                $"{ClientDataNotFound}:{clientDataDirectory}");
        }

        string[] archives = Directory.GetFiles(clientDataDirectory, ArchiveSearchPattern);
        Array.Sort(archives, StringComparer.OrdinalIgnoreCase);
        if (archives.Length == 0)
        {
            return new MapGridExtractReport(
                clientDataDirectory, outputDirectory, "",
                Array.Empty<MapGridExtractedFile>(),
                Array.Empty<(int, string, string)>(),
                NoMapArchives);
        }

        string fingerprint = Fingerprint(clientDataDirectory, archives);
        Directory.CreateDirectory(outputDirectory);

        var loader = new BinaryMapGridLoader();
        var written = new List<MapGridExtractedFile>();
        var refused = new List<(int MapId, string Archive, string Reason)>();
        var seen = new Dictionary<int, string>();

        foreach (string archivePath in archives)
        {
            string archiveName = Path.GetFileName(archivePath);
            NosArchiveResult archive = NosArchive.Open(archivePath);
            if (!archive.Ok)
            {
                refused.Add((0, archiveName, archive.FailureReason ?? "archive_unreadable"));
                continue;
            }

            foreach (NosArchiveEntry entry in archive.Entries)
            {
                if (!TryMapId(entry, out int mapId))
                {
                    refused.Add((0, archiveName, "entry_unidentified"));
                    continue;
                }

                MemoryReadOutcome payload = NosArchive.ReadEntry(archivePath, entry);
                if (!payload.Ok)
                {
                    refused.Add((mapId, archiveName, payload.FailureReason ?? "entry_unreadable"));
                    continue;
                }

                if (!TryDecodeGrid(loader, mapId, payload.Bytes, out MapGrid grid, out byte[] fileBytes, out string? reason))
                {
                    refused.Add((mapId, archiveName, reason ?? MapGridFormat.HeaderTruncated));
                    continue;
                }

                if (seen.TryGetValue(mapId, out string? previous))
                {
                    refused.Add((mapId, archiveName, $"{DuplicateMapId}:{previous}"));
                    continue;
                }

                string fileName = mapId.ToString(CultureInfo.InvariantCulture) + ".grid";
                string dest = Path.Combine(outputDirectory, fileName);
                File.WriteAllBytes(dest, fileBytes);
                string hash = MapGridSetIdentity.HashFile(fileBytes);
                seen[mapId] = archiveName;
                written.Add(new MapGridExtractedFile(mapId, dest, hash, grid.Width, grid.Height));
            }
        }

        written.Sort(static (a, b) => a.MapId.CompareTo(b.MapId));
        WriteManifest(outputDirectory, fingerprint, written);

        return new MapGridExtractReport(
            clientDataDirectory, outputDirectory, fingerprint, written, refused, null);
    }

    /// <summary>Loads one extracted map by id, or says why it cannot.</summary>
    public static bool TryInfo(
        string mapsDirectory,
        int mapId,
        out MapGrid grid,
        out string? fileHash,
        out string? failureReason)
    {
        grid = default;
        fileHash = null;
        failureReason = null;

        ArgumentException.ThrowIfNullOrWhiteSpace(mapsDirectory);
        string path = Path.Combine(mapsDirectory, mapId.ToString(CultureInfo.InvariantCulture) + ".grid");
        if (!File.Exists(path))
        {
            failureReason = $"grid_file_not_found:{mapId}";
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (IOException ex)
        {
            failureReason = $"grid_unreadable:{ex.GetType().Name}";
            return false;
        }

        fileHash = MapGridSetIdentity.HashFile(bytes);
        return new BinaryMapGridLoader().TryLoad(mapId, bytes, out grid, out failureReason);
    }

    /// <summary>Console entry for <c>--extract-maps</c>.</summary>
    public static int RunExtract(string? clientDataDirectory = null, string? outputDirectory = null)
    {
        clientDataDirectory ??= ReferenceImporter.DefaultDataDirectory;
        if (outputDirectory is null
            && !TryResolveDedicatedMapsDirectory(out outputDirectory, out string? volumeReason))
        {
            Console.WriteLine($"[REFUSED] {volumeReason}");
            return 1;
        }

        MapGridExtractReport report = Extract(clientDataDirectory, outputDirectory);
        if (!report.Ok)
        {
            Console.WriteLine($"[REFUSED] {report.FailureReason}");
            return 1;
        }

        Console.WriteLine($"Client: {report.ClientDataDirectory}");
        Console.WriteLine($"Output: {report.OutputDirectory}");
        Console.WriteLine($"Fingerprint: {report.ClientFingerprint}");
        foreach (MapGridExtractedFile file in report.Written)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  {file.MapId} {file.Width}x{file.Height} {file.Sha256}"));
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Wrote {report.Written.Count} grids, refused {report.Refused.Count}."));
        Console.WriteLine($"Manifest: {Path.Combine(report.OutputDirectory, ManifestFileName)}");
        return report.Written.Count > 0 ? 0 : 1;
    }

    /// <summary>Console entry for <c>--map-info &lt;mapId&gt;</c>.</summary>
    public static int RunInfo(int mapId, string? mapsDirectory = null)
    {
        if (mapsDirectory is null
            && !TryResolveDedicatedMapsDirectory(out mapsDirectory, out string? volumeReason))
        {
            Console.WriteLine($"[REFUSED] {volumeReason}");
            return 1;
        }

        if (!TryInfo(mapsDirectory, mapId, out MapGrid grid, out string? hash, out string? reason))
        {
            Console.WriteLine($"[REFUSED] {reason}");
            return 1;
        }

        int walkable = 0, attackBlocked = 0;
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                if (grid.IsWalkable(x, y)) walkable++;
                if (grid.BlocksAttack(x, y)) attackBlocked++;
            }
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"map={grid.MapId} {grid.Width}x{grid.Height} cells={grid.CellCount}"));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"walkable={walkable} attack_blocked={attackBlocked} hash={hash}"));
        return 0;
    }

    internal static bool TryDecodeGrid(
        IMapGridLoader loader,
        int mapId,
        ReadOnlySpan<byte> payload,
        out MapGrid grid,
        out byte[] fileBytes,
        out string? failureReason)
    {
        fileBytes = Array.Empty<byte>();

        if (loader.TryLoad(mapId, payload, out grid, out failureReason))
        {
            fileBytes = payload.ToArray();
            return true;
        }

        if (!TryInflate(payload, out byte[]? inflated, out string? inflateFailure))
        {
            if (inflateFailure is not null)
            {
                failureReason = $"{InflateFailed}:{inflateFailure}";
                grid = default;
            }

            return false;
        }

        if (!loader.TryLoad(mapId, inflated, out grid, out failureReason))
            return false;

        fileBytes = inflated;
        return true;
    }

    /// <summary>
    /// The zlib wrap <see cref="NosDataTable"/> already established: 13-byte header
    /// whose own numbers describe the stream, then a zlib payload. False with a
    /// null failure means the bytes were not that wrap.
    /// </summary>
    private static bool TryInflate(ReadOnlySpan<byte> payload, out byte[] inflated, out string? failure)
    {
        const int headerSize = 13;
        inflated = Array.Empty<byte>();
        failure = null;

        if (payload.Length <= headerSize)
            return false;

        int declaredPlain = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(4));
        int declaredPacked = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(8));
        ReadOnlySpan<byte> stream = payload.Slice(headerSize);

        if (declaredPacked != stream.Length || declaredPlain <= 0)
            return false;
        if (stream.Length < 2 || stream[0] != 0x78)
            return false;

        try
        {
            using var input = new MemoryStream(stream.ToArray());
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream(declaredPlain);
            zlib.CopyTo(output);
            byte[] result = output.ToArray();
            if (result.Length != declaredPlain)
            {
                failure = $"inflate_size_mismatch:{result.Length}!={declaredPlain}";
                return false;
            }

            inflated = result;
            return true;
        }
        catch (InvalidDataException)
        {
            failure = "inflate_invalid_data";
            return false;
        }
    }

    private static bool TryMapId(NosArchiveEntry entry, out int mapId)
    {
        if (entry.Id is { } id)
        {
            mapId = id;
            return true;
        }

        string? name = entry.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            mapId = 0;
            return false;
        }

        ReadOnlySpan<char> stem = Path.GetFileNameWithoutExtension(name.AsSpan());
        return int.TryParse(stem, NumberStyles.Integer, CultureInfo.InvariantCulture, out mapId);
    }

    private static string Fingerprint(string clientDataDirectory, string[] archives)
    {
        string? exe = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(clientDataDirectory)) ?? "", "NostaleClientX.exe");
        if (File.Exists(exe))
        {
            using FileStream stream = File.OpenRead(exe);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        var builder = new StringBuilder();
        foreach (string archive in archives)
        {
            var info = new FileInfo(archive);
            builder.Append(info.Name).Append('\u001f')
                .Append(info.Length.ToString(CultureInfo.InvariantCulture)).Append('\u001e');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static void WriteManifest(
        string outputDirectory,
        string fingerprint,
        IReadOnlyList<MapGridExtractedFile> written)
    {
        var text = new StringBuilder();
        text.Append(ManifestMagic).Append(' ').Append(ManifestVersion).Append('\n');
        text.Append("fingerprint ").Append(fingerprint).Append('\n');
        foreach (MapGridExtractedFile file in written)
        {
            text.Append(file.MapId.ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(file.Sha256).Append(' ')
                .Append(file.Width.ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(file.Height.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        File.WriteAllText(Path.Combine(outputDirectory, ManifestFileName), text.ToString());
    }
}
