namespace NosAi.Runtime.GameData;

/// <summary>
/// One file a directory scan found, exactly as it was classified --
/// decoded or not. A file this project has no dedicated decoder for
/// still gets a row, with <see cref="ArchiveType"/> null: the point of an
/// inventory is to notice a client update touched it, not to explain it.
/// </summary>
public sealed record ClientInventoryEntry(
    string File,
    string? ArchiveType,
    string? Details,
    string? Error);

/// <summary>
/// Classifies every file under a client data directory using this
/// project's own archive reader -- no external tool, nothing outside the
/// runtime process.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ClientUpdateCommand"/> needs to notice when a client update
/// touched a file this project has no dedicated decoder for yet, not only
/// the five tables <see cref="ReferenceImporter"/> already understands.
/// <see cref="NosArchive.Open"/> already reads the client's own container
/// format natively, so classifying by "does this open as one of the two
/// known archive layouts" reaches every file in the directory tree with
/// code this project has already established against the real client,
/// with nothing run out of process.
/// </para>
/// <para>
/// A file that does not open as an archive is not a failure of this
/// scanner: most of a client installation is not a `.NOS` container (a
/// `.dll`, a `.exe`, a loose config file), and <see cref="NosArchive"/>
/// says so with a named reason rather than a fabricated classification.
/// </para>
/// </remarks>
public static class ClientDirectoryScanner
{
    /// <summary>
    /// Classifies every regular file under <paramref name="dataDirectory"/>.
    /// </summary>
    public static IReadOnlyList<ClientInventoryEntry> Scan(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        if (!Directory.Exists(dataDirectory))
            return Array.Empty<ClientInventoryEntry>();

        var entries = new List<ClientInventoryEntry>();
        foreach (string path in Directory.EnumerateFiles(dataDirectory, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(dataDirectory, path);
            entries.Add(ClassifyOne(relative, path));
        }

        return entries;
    }

    private static ClientInventoryEntry ClassifyOne(string relativePath, string fullPath)
    {
        NosArchiveResult archive = NosArchive.Open(fullPath);
        if (archive.Ok)
        {
            string details = archive.Magic is null
                ? $"entries={archive.Entries.Count}"
                : $"entries={archive.Entries.Count} magic={archive.Magic}";
            return new ClientInventoryEntry(relativePath, archive.Format.ToString(), details, null);
        }

        return new ClientInventoryEntry(relativePath, null, null, archive.FailureReason);
    }
}
