using System.Globalization;
using System.IO;
using NosAi.Runtime.Navigation;

namespace NosAi.ControlPanel;

/// <summary>How the .grid files on disk relate to the manifest's recorded set.</summary>
public enum MapGridSetDiskState
{
    Intact = 0,
    Changed = 1,
    NotComputable = 2
}

/// <summary>
/// The disk-only half of grid identity: whether the .grid files still on disk
/// hash to the manifest's recorded set. The client fingerprint is held at its
/// recorded value, so a mismatch here means the files themselves moved, and the
/// check stays computable without attaching to a running client.
/// </summary>
public readonly record struct MapGridSetDiskCheck(MapGridSetDiskState State, string? Reason)
{
    public static MapGridSetDiskCheck Intact() => new(MapGridSetDiskState.Intact, null);
    public static MapGridSetDiskCheck Changed(string reason) => new(MapGridSetDiskState.Changed, reason);
    public static MapGridSetDiskCheck NotComputable(string reason) => new(MapGridSetDiskState.NotComputable, reason);
}

/// <summary>
/// Reads the recorded <see cref="MapGridSetIdentity"/> from an extracted maps
/// directory. Parse-only: it does not rewrite the manifest or the grid files.
/// </summary>
internal static class MapGridManifest
{
    /// <summary>The extractor has not left a recorded identity in this directory.</summary>
    public const string ManifestMissing = "map_grids_no_recorded_identity";

    /// <summary>The file was present and could not be read.</summary>
    public const string ManifestUnreadable = "map_grid_manifest_unreadable";

    /// <summary>The bytes do not match the format the extractor writes.</summary>
    public const string ManifestMalformed = "map_grid_manifest_malformed";

    /// <summary>A stale layout: refuse rather than reinterpret.</summary>
    public const string ManifestVersionMismatch = "map_grid_manifest_version";

    /// <summary>
    /// Loads the identity the extractor recorded, or names why it cannot.
    /// Current identity is not invented here: without a fingerprint of the
    /// running client, <see cref="MapGridSetIdentity.MayLoad"/> stays closed.
    /// </summary>
    public static bool TryRead(string mapsDirectory, out MapGridSetIdentity? identity, out string? failureReason)
    {
        identity = null;
        failureReason = null;

        ArgumentException.ThrowIfNullOrWhiteSpace(mapsDirectory);
        string path = Path.Combine(mapsDirectory, MapGridExtractor.ManifestFileName);
        if (!File.Exists(path))
        {
            failureReason = ManifestMissing;
            return false;
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (IOException)
        {
            failureReason = ManifestUnreadable;
            return false;
        }

        string[] lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2)
        {
            failureReason = ManifestMalformed;
            return false;
        }

        string[] header = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (header.Length != 2
            || !string.Equals(header[0], MapGridExtractor.ManifestMagic, StringComparison.Ordinal))
        {
            failureReason = ManifestMalformed;
            return false;
        }

        if (!int.TryParse(header[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int version)
            || version != MapGridExtractor.ManifestVersion)
        {
            failureReason = ManifestVersionMismatch;
            return false;
        }

        const string fingerprintPrefix = "fingerprint ";
        if (!lines[1].StartsWith(fingerprintPrefix, StringComparison.Ordinal)
            || lines[1].Length <= fingerprintPrefix.Length)
        {
            failureReason = ManifestMalformed;
            return false;
        }

        string fingerprint = lines[1][fingerprintPrefix.Length..].Trim();
        var files = new List<MapGridFile>(lines.Length - 2);
        for (int i = 2; i < lines.Length; i++)
        {
            string[] parts = lines[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2
                || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int mapId))
            {
                failureReason = ManifestMalformed;
                return false;
            }

            files.Add(new MapGridFile(mapId, parts[1]));
        }

        try
        {
            identity = MapGridSetIdentity.Compute(files, fingerprint);
            return true;
        }
        catch (ArgumentException)
        {
            failureReason = ManifestMalformed;
            identity = null;
            return false;
        }
    }

    /// <summary>
    /// Whether the .grid files still on disk hash to the recorded set. This is
    /// the half of the identity computable without a running client: editing or
    /// truncating a .grid changes its file hash, which changes the folded set
    /// hash. The client fingerprint is held at its recorded value, so a mismatch
    /// means the files themselves moved, not that the client changed.
    /// </summary>
    public static MapGridSetDiskCheck CheckIntact(string mapsDirectory, MapGridSetIdentity? recorded)
    {
        if (recorded is null)
            return MapGridSetDiskCheck.NotComputable(ManifestMissing);

        if (!TryComputeCurrent(
                mapsDirectory,
                recorded.ClientFingerprint,
                out MapGridSetIdentity? current,
                out string? reason))
        {
            return MapGridSetDiskCheck.NotComputable(reason ?? ManifestMissing);
        }

        if (string.Equals(current!.SetHash, recorded.SetHash, StringComparison.Ordinal))
            return MapGridSetDiskCheck.Intact();

        return MapGridSetDiskCheck.Changed(
            $"map_grid_set_changed:{Short(recorded.SetHash)}_to_{Short(current.SetHash)}");
    }

    /// <summary>
    /// The identity of the .grid files on disk folded under a fingerprint supplied
    /// by the caller, or why it cannot be computed.
    /// </summary>
    /// <remarks>
    /// Nothing is invented here: an unreadable directory, an unreadable grid, an
    /// empty set or a duplicate map id all answer false with the reason, and the
    /// caller keeps UNKNOWN. Callers that pass the recorded fingerprint are asking
    /// whether the files moved; callers that pass a fingerprint taken now are asking
    /// whether the whole identity still holds.
    /// </remarks>
    public static bool TryComputeCurrent(
        string mapsDirectory,
        string clientFingerprint,
        out MapGridSetIdentity? identity,
        out string? failureReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mapsDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientFingerprint);

        identity = null;
        if (!TryReadDiskFiles(mapsDirectory, out List<MapGridFile> files, out failureReason))
            return false;

        try
        {
            identity = MapGridSetIdentity.Compute(files, clientFingerprint);
            failureReason = null;
            return true;
        }
        catch (ArgumentException)
        {
            identity = null;
            failureReason = MapGridExtractor.DuplicateMapId;
            return false;
        }
    }

    /// <summary>
    /// Every .grid file in the directory with the hash of its exact bytes. Shared by
    /// <see cref="CheckIntact"/> and <see cref="TryComputeCurrent"/> so both see the
    /// same set, read once and read the same way.
    /// </summary>
    private static bool TryReadDiskFiles(
        string mapsDirectory,
        out List<MapGridFile> files,
        out string? failureReason)
    {
        files = new List<MapGridFile>();
        failureReason = null;

        string[] paths;
        try
        {
            paths = Directory.GetFiles(mapsDirectory, "*.grid");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            failureReason = $"grid_directory_unreadable:{ex.GetType().Name}";
            return false;
        }

        foreach (string path in paths)
        {
            string stem = Path.GetFileNameWithoutExtension(path);
            if (!int.TryParse(stem, NumberStyles.Integer, CultureInfo.InvariantCulture, out int mapId))
                continue;

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failureReason = $"grid_unreadable:{ex.GetType().Name}";
                return false;
            }

            files.Add(new MapGridFile(mapId, MapGridSetIdentity.HashFile(bytes)));
        }

        if (files.Count == 0)
        {
            failureReason = MapGridExtractor.NoMapArchives;
            return false;
        }

        return true;
    }

    private static string Short(string hash) => hash.Length <= 12 ? hash : hash[..12];
}

