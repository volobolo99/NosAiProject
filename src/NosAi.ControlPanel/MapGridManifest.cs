using System.Globalization;
using System.IO;
using NosAi.Runtime.Navigation;

namespace NosAi.ControlPanel;

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
}
