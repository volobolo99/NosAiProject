using System.IO;

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
    /// Resolves the full database path for <paramref name="options"/>.
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

        return Path.Combine(root, options.FileName);
    }
}
