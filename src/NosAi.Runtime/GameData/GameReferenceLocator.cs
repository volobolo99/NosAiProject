using Microsoft.Data.Sqlite;

namespace NosAi.Runtime.GameData;

/// <summary>Where the reference database was looked for, and whether it is there.</summary>
/// <param name="Path">
/// The file that would be opened. Null when the dedicated volume itself is absent,
/// because there is then no drive letter to compose a path from.
/// </param>
/// <param name="Exists">True only when that file is present on disk.</param>
/// <param name="FailureReason">
/// A named token when <see cref="Exists"/> is false: the volume missing, or the
/// file missing at a known path. Null when the file is there.
/// </param>
public readonly record struct GameReferenceLocation(
    string? Path,
    bool Exists,
    string? FailureReason);

/// <summary>
/// Finds the on-disk reference catalogue the importer writes, without creating it.
/// </summary>
/// <remarks>
/// <para>
/// The dedicated volume is resolved the same way
/// <c>MapGridExtractor.TryResolveDedicatedMapsDirectory</c> resolves the map
/// grids: label <see cref="VolumeLabel"/>, then
/// <c>&lt;volume&gt;\NosAi\data\</c>. The catalogue file sits next to the
/// <c>maps</c> directory rather than under a second tree.
/// </para>
/// <para>
/// Absence is a named refusal, not a created empty file.
/// <see cref="GameReferenceDatabase.Open"/> would create one, and an empty
/// catalogue answers every vnum with "not in the catalogue", which is a different
/// fact from "the catalogue was never loaded".
/// </para>
/// </remarks>
public static class GameReferenceLocator
{
    /// <summary>The volume the rest of the runtime already looks for.</summary>
    public const string VolumeLabel = "NOSAI-SSD";

    /// <summary>File name under <c>NosAi\data</c>.</summary>
    public const string FileName = "reference.db";

    /// <summary>The dedicated volume is not mounted, or could not be enumerated.</summary>
    public const string VolumeNotFound = "nosai_ssd_not_found";

    /// <summary>The volume (or a test directory) is known and the file is not on it.</summary>
    public const string DatabaseNotFound = "reference_database_not_found";

    /// <summary>The file is present and SQLite refused to open it.</summary>
    public const string DatabaseUnreadable = "reference_database_unreadable";

    /// <summary>
    /// Looks on the dedicated volume. Does not create anything.
    /// </summary>
    public static GameReferenceLocation Locate()
    {
        if (!TryFindDedicatedDataDirectory(out string dataDirectory, out string? reason))
            return new GameReferenceLocation(null, false, reason);
        return LocateIn(dataDirectory);
    }

    /// <summary>
    /// Looks under an explicit <c>NosAi\data</c> directory. Used by tests that
    /// must not invent a drive letter, matching how map extraction is tested.
    /// </summary>
    public static GameReferenceLocation LocateIn(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        string path = Path.GetFullPath(Path.Combine(dataDirectory, FileName));
        if (File.Exists(path))
            return new GameReferenceLocation(path, true, null);
        return new GameReferenceLocation(path, false, $"{DatabaseNotFound}:{path}");
    }

    /// <summary>
    /// Opens the catalogue on the dedicated volume, or names why it cannot.
    /// </summary>
    public static bool TryOpen(out GameReferenceDatabase? database, out string? failureReason) =>
        TryOpen(Locate(), out database, out failureReason);

    /// <summary>
    /// Opens the catalogue at a location already resolved. Does not create a file.
    /// </summary>
    public static bool TryOpen(
        GameReferenceLocation location,
        out GameReferenceDatabase? database,
        out string? failureReason)
    {
        database = null;
        if (!location.Exists || string.IsNullOrWhiteSpace(location.Path))
        {
            failureReason = location.FailureReason ?? DatabaseNotFound;
            return false;
        }

        try
        {
            database = GameReferenceDatabase.OpenExisting(location.Path);
            failureReason = null;
            return true;
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException
                                       or InvalidOperationException)
        {
            failureReason = $"{DatabaseUnreadable}:{ex.GetType().Name}";
            return false;
        }
    }

    /// <summary>
    /// <c>&lt;NOSAI-SSD&gt;\NosAi\data</c>, or a reason the volume is absent.
    /// </summary>
    public static bool TryFindDedicatedDataDirectory(out string path, out string? failureReason)
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

                path = Path.Combine(drive.RootDirectory.FullName, "NosAi", "data");
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
}
