using System.IO;

using System.Linq;

namespace NosAi.Storage;

/// <summary>
/// Resolves a database path from a Windows volume <em>label</em>
/// (docs/ROADMAP_ESECUTIVA.md S:2.4), never from a hardcoded drive letter: a
/// drive letter is an OS-assigned accident of enumeration order and changes
/// across reboots and across machines, while a label is what the operator
/// actually attached.
/// </summary>
public static class VolumeLocator
{
    /// <summary>
    /// Looks for a ready drive whose volume label matches <paramref name="volumeLabel"/>.
    /// </summary>
    /// <returns><see langword="true"/> and the drive's root directory when found.</returns>
    public static bool TryResolve(string volumeLabel, out string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(volumeLabel);

        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady)
                continue;

            string label;
            try
            {
                // A drive can stop being ready between IsReady and VolumeLabel
                // (removable media race); that is an absent volume, not an error.
                label = drive.VolumeLabel;
            }
            catch (IOException)
            {
                continue;
            }

            if (string.Equals(label, volumeLabel, StringComparison.OrdinalIgnoreCase))
            {
                rootPath = drive.RootDirectory.FullName;
                return true;
            }
        }

        rootPath = string.Empty;
        return false;
    }

    /// <summary>
    /// The directory the deployment document reserves for SQLite and its
    /// WAL/SHM companions, relative to the volume root.
    /// </summary>
    /// <remarks>
    /// <c>docs/EXTERNAL_SSD_DEPLOYMENT.md</c> § 3 has always said
    /// <c>&lt;NOSAI-SSD&gt;:\NosAi\data\db\</c>. Until 2026-09-07 this class
    /// combined the file name straight onto the volume root, so <c>nosai.db</c>
    /// and <c>nosai-maps.db</c> landed beside <c>$RECYCLE.BIN</c> while
    /// <c>reference.db</c> sat two directories deeper — three databases of one
    /// project in two places, neither of them the documented one. Aligned on
    /// the operator's instruction, with the existing files moved rather than
    /// abandoned: a database the code stops looking at is a database the
    /// project silently starts over from.
    /// </remarks>
    public static readonly string[] DatabaseDirectorySegments = { "NosAi", "data", "db" };

    /// <summary>
    /// Where the database would be, without creating anything and without
    /// throwing when the volume is absent.
    /// </summary>
    /// <remarks>
    /// Per chi deve <b>guardare</b> il file invece di aprirlo — un rapporto di
    /// sola lettura che distingue «mai creato» da «creato e vuoto» non puo'
    /// usare <see cref="ResolveDatabasePath"/>, che la cartella la crea. Ed e'
    /// qui e non nel chiamante perche' la formula del percorso deve stare in un
    /// posto solo: la versione precedente la ricalcolava altrove e le due
    /// divergevano appena una delle due cambiava.
    /// </remarks>
    public static bool TryResolveDatabasePath(
        SqliteJournalOptions options, out string path, out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!TryResolve(options.VolumeLabel, out string root))
        {
            path = string.Empty;
            failureReason = $"volume_not_attached:{options.VolumeLabel}";
            return false;
        }

        path = Path.Combine(
            new[] { root }.Concat(DatabaseDirectorySegments).Append(options.FileName).ToArray());
        failureReason = null;
        return true;
    }

    /// <summary>
    /// Resolves the full database path for <paramref name="options"/>, creating
    /// the directory when it does not exist yet.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The labeled volume is not attached. There is deliberately no fallback to
    /// another drive: a journal that silently landed somewhere other than the
    /// volume the operator dedicated to it is a journal the operator can no
    /// longer find, back up or reason about the durability of.
    /// </exception>
    public static string ResolveDatabasePath(SqliteJournalOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!TryResolve(options.VolumeLabel, out string root))
        {
            throw new InvalidOperationException(
                $"Volume '{options.VolumeLabel}' is not attached. The Gate 1 journal requires the " +
                "labeled volume and does not fall back to a different drive.");
        }

        string directory = Path.Combine(new[] { root }.Concat(DatabaseDirectorySegments).ToArray());

        // Creare la cartella, non il database: sono due cose diverse e solo la
        // prima e' innocua. Chi legge un registro deve poter distinguere "mai
        // creato" da "creato e vuoto" (OutcomeReportCommand), e quella
        // distinzione sopravvive solo se ad aprire lo store e' chi scrive.
        Directory.CreateDirectory(directory);

        return Path.Combine(directory, options.FileName);
    }
}
