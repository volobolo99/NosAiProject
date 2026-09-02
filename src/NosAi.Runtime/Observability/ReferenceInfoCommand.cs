using System.Globalization;
using System.Text;
using NosAi.Runtime.GameData;

namespace NosAi.Runtime.Observability;

/// <summary>
/// Prints which reference catalogue this process would open, and what is in it
/// (CLI <c>--reference-info</c>).
/// </summary>
/// <remarks>
/// The operator needs to know whether the file on the dedicated volume is the
/// one imported from this machine's client, not whether some catalogue exists
/// somewhere. Counts, provenance and import time are what the database already
/// records; this command reads them.
/// </remarks>
public static class ReferenceInfoCommand
{
    /// <summary>The operator flag.</summary>
    public const string Flag = "--reference-info";

    /// <summary>Console entry. Missing catalogue is reported, not a failed run.</summary>
    public static int Run()
    {
        GameReferenceLocation location = GameReferenceLocator.Locate();
        Console.Write(Format(location));
        return 0;
    }

    /// <summary>The missing-file form, used when the volume or the file is absent.</summary>
    public static string Format(GameReferenceLocation location)
    {
        string? openReason = null;
        if (location.Exists
            && !string.IsNullOrWhiteSpace(location.Path)
            && GameReferenceLocator.TryOpen(location, out GameReferenceDatabase? database, out openReason)
            && database is not null)
        {
            using (database)
                return Format(database);
        }

        var text = new StringBuilder();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"reference database: {location.Path ?? "(unresolved)"}"));
        text.AppendLine("exists: no");
        text.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"reason: {openReason ?? location.FailureReason ?? GameReferenceLocator.DatabaseNotFound}"));
        return text.ToString();
    }

    /// <summary>
    /// What a live database contains. Used by tests against
    /// <see cref="GameReferenceDatabase.OpenInMemory"/>.
    /// </summary>
    public static string Format(GameReferenceDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);

        var text = new StringBuilder();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture,
            $"reference database: {database.DatabasePath}"));
        text.AppendLine("exists: yes");

        foreach (ReferenceTable table in ReferenceImporter.Tables)
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{table.Kind}: {database.Count(table.Kind)}"));
        }

        IReadOnlyList<ReferenceSource> sources = database.Sources();
        if (sources.Count == 0)
        {
            text.AppendLine("source: none");
            return text.ToString();
        }

        foreach (ReferenceSource source in sources)
        {
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"source: archive={source.Archive} table={source.TableName} records={source.RecordCount} imported={source.ImportedAtUtc:O} client={source.ClientPath}"));
        }

        return text.ToString();
    }
}
