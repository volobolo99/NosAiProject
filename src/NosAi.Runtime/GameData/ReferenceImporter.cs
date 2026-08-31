using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.GameData;

/// <summary>What a language import produced, by kind.</summary>
public sealed record LanguageImportReport(
    string Language,
    bool Ok,
    string? FailureReason,
    IReadOnlyDictionary<string, int> EntriesByKind)
{
    public int Total => EntriesByKind.Values.Sum();
}

/// <summary>One kind of reference data and where the client keeps it.</summary>
public sealed record ReferenceTable(string Kind, string Archive, string TableName, string Purpose);

/// <summary>The outcome of importing one table.</summary>
public sealed record ImportOutcome(
    ReferenceTable Table,
    bool Ok,
    string? FailureReason,
    ReferenceDiff Diff,
    int RecordsRead,
    int BinaryTails,
    int UnreliableLengths)
{
    public static ImportOutcome Failed(ReferenceTable table, string reason) =>
        new(table, false, reason, ReferenceDiff.Empty(table.Kind), 0, 0, 0);
}

/// <summary>The result of importing everything.</summary>
public sealed record ImportReport(
    DateTime StartedAtUtc,
    string ClientDirectory,
    IReadOnlyList<ImportOutcome> Outcomes)
{
    public bool AllOk => Outcomes.All(o => o.Ok);
    public bool AnyChange => Outcomes.Any(o => o.Diff.AnyChange);
    public int TotalRecords => Outcomes.Sum(o => o.RecordsRead);
}

/// <summary>
/// Fills the reference database from an installed client.
/// </summary>
/// <remarks>
/// <para>
/// The tables below are the ones this project needs to reason about a fight, and
/// each says what it is for. The list is deliberately explicit rather than "import
/// every .dat": pulling in tables nobody has looked at would put unexamined rows
/// beside examined ones with nothing to tell them apart.
/// </para>
/// <para>
/// An import that cannot decode a table <b>fails that table</b> and leaves the
/// previous rows in place. A reference database half-replaced by a bad decode is
/// worse than a stale one, because a stale one is at least self-consistent.
/// </para>
/// </remarks>
public sealed class ReferenceImporter
{
    /// <summary>Where a stock installation keeps its archives.</summary>
    public const string DefaultDataDirectory = @"C:\Program Files (x86)\Nostale\NostaleData";

    /// <summary>The tables that carry what a fight simulation needs.</summary>
    public static IReadOnlyList<ReferenceTable> Tables { get; } = new ReferenceTable[]
    {
        new("monster", "NSgtdData.NOS", "monster.dat",
            "livello, HP/MP, razza, attributo, arma, armatura, abilità, comportamento, drop"),
        new("item", "NSgtdData.NOS", "Item.dat",
            "tipo, statistiche, buff: l'equipaggiamento e i consumabili"),
        new("skill", "NSgtdData.NOS", "Skill.dat",
            "costo, bersaglio, portata, danno, combo"),
        new("card", "NSgtdData.NOS", "Card.dat",
            "buff e debuff con effetto e durata"),
        new("bcard", "NSgtdData.NOS", "BCard.dat",
            "carte di combattimento: gli effetti a cui le altre tabelle rimandano")
    };

    /// <summary>
    /// Which language table names each kind of entity.
    /// </summary>
    /// <remarks>
    /// A record's <c>NAME</c> is a key such as <c>zts1e</c>, not a name. The client
    /// resolves it through <c>NSlangData_&lt;LANG&gt;.NOS</c>, and so does this: the
    /// meanings come from the game's own files rather than from anyone's reading of
    /// what a field probably means.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> LanguageTables { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["monster"] = "monster",
            ["item"] = "Item",
            ["skill"] = "Skill",
            ["card"] = "Card",
            ["bcard"] = "BCard"
        };

    private readonly string _directory;

    public ReferenceImporter(string? clientDataDirectory = null) =>
        _directory = clientDataDirectory ?? DefaultDataDirectory;

    /// <summary>True when an installation is present to import from.</summary>
    public bool ClientAvailable => Directory.Exists(_directory);

    public string Directory_ => _directory;

    /// <summary>Imports every known table, reporting each one separately.</summary>
    public ImportReport ImportAll(GameReferenceDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        DateTime started = DateTime.UtcNow;

        if (!ClientAvailable)
        {
            return new ImportReport(started, _directory, Tables
                .Select(t => ImportOutcome.Failed(t, $"client_data_directory_not_found:{_directory}"))
                .ToArray());
        }

        var outcomes = new List<ImportOutcome>();
        foreach (ReferenceTable table in Tables)
            outcomes.Add(ImportOne(database, table));

        return new ImportReport(started, _directory, outcomes);
    }

    /// <summary>Imports one table.</summary>
    public ImportOutcome ImportOne(GameReferenceDatabase database, ReferenceTable table)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(table);

        string archivePath = Path.Combine(_directory, table.Archive);
        NosArchiveResult archive = NosArchive.Open(archivePath);
        if (!archive.Ok)
            return ImportOutcome.Failed(table, $"archive:{archive.FailureReason}");

        NosArchiveEntry? entry = archive.Entries.FirstOrDefault(e =>
            string.Equals(e.Name, table.TableName, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            return ImportOutcome.Failed(table, $"table_not_in_archive:{table.TableName}");

        MemoryReadOutcome payload = NosArchive.ReadEntry(archivePath, entry);
        if (!payload.Ok)
            return ImportOutcome.Failed(table, $"read:{payload.FailureReason}");

        NosTableResult decoded = NosDataTable.Decode(table.TableName, payload.Bytes);
        if (!decoded.Ok)
            return ImportOutcome.Failed(table, $"decode:{decoded.FailureReason}");

        if (decoded.Source != DataSourceKind.Live)
            return ImportOutcome.Failed(table, $"unexpected_source:{decoded.Source}");

        // A table that decoded to nothing is a failure, not an empty table: the
        // client does not ship an empty monster list, so zero means we misread it.
        int withVnum = decoded.Records.Count(r => r.Vnum.HasValue);
        if (withVnum == 0)
            return ImportOutcome.Failed(table, "no_identified_records");

        ReferenceDiff diff = database.Import(
            table.Kind, table.Archive, table.TableName, archivePath,
            decoded.Records, payload.Bytes);

        return new ImportOutcome(
            table, true, null, diff, withVnum, decoded.BinaryTailCount, decoded.UnreliableLengthCount);
    }

    /// <summary>
    /// Imports the displayed names and descriptions for one language.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The entries live in <c>NSlangData_&lt;LANG&gt;.NOS</c> as
    /// <c>_code_&lt;lang&gt;_&lt;table&gt;.txt</c>, each line a key and its text.
    /// This is what turns <c>zts1e</c> into <i>Volpe piccola</i>, and a BCard
    /// identifier into <i>Impedisce l'Attacco Ravvicinato</i> — the effect system's
    /// meaning, stated by the client rather than inferred.
    /// </para>
    /// <para>
    /// A language that is not installed is reported, not substituted with another.
    /// Showing German text where Italian was asked for would be a quiet lie about
    /// what the operator is reading.
    /// </para>
    /// </remarks>
    public LanguageImportReport ImportLanguage(GameReferenceDatabase database, string language = "IT")
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        string archivePath = Path.Combine(_directory, $"NSlangData_{language.ToUpperInvariant()}.NOS");
        NosArchiveResult archive = NosArchive.Open(archivePath);
        if (!archive.Ok)
            return new LanguageImportReport(language, false, $"archive:{archive.FailureReason}",
                new Dictionary<string, int>());

        var imported = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach ((string kind, string tableName) in LanguageTables)
        {
            string wanted = $"_code_{language.ToLowerInvariant()}_{tableName}.txt";
            NosArchiveEntry? entry = archive.Entries.FirstOrDefault(e =>
                string.Equals(e.Name, wanted, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                continue;

            MemoryReadOutcome payload = NosArchive.ReadEntry(archivePath, entry);
            if (!payload.Ok)
                continue;

            NosTableResult decoded = NosDataTable.Decode(wanted, payload.Bytes);
            if (!decoded.Ok)
                continue;

            Dictionary<string, string> entries = NosDataTable.ReadKeyedText(payload.Bytes);
            if (entries.Count == 0)
                continue;

            imported[kind] = database.ImportText(language, kind, entries);
        }

        return new LanguageImportReport(language, imported.Count > 0,
            imported.Count > 0 ? null : "no_language_tables_found", imported);
    }

    /// <summary>
    /// Reports what an import would change, without changing anything.
    /// </summary>
    /// <remarks>
    /// The operator sees the difference before accepting it. An update that applies
    /// itself and then reports is not something anyone can decline.
    /// </remarks>
    public IReadOnlyList<ImportOutcome> Preview(GameReferenceDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);

        var outcomes = new List<ImportOutcome>();
        foreach (ReferenceTable table in Tables)
        {
            // Decoded into a scratch database so the real one is untouched.
            using GameReferenceDatabase scratch = GameReferenceDatabase.OpenInMemory();
            ImportOutcome trial = ImportOne(scratch, table);
            if (!trial.Ok)
            {
                outcomes.Add(trial);
                continue;
            }

            outcomes.Add(trial with { Diff = DiffAgainst(database, scratch, table.Kind) });
        }
        return outcomes;
    }

    /// <summary>Compares what is stored with what a trial import produced.</summary>
    private static ReferenceDiff DiffAgainst(
        GameReferenceDatabase current, GameReferenceDatabase trial, string kind)
    {
        int inCurrent = current.Count(kind);
        int inTrial = trial.Count(kind);

        // Counts alone cannot say which records changed; the authoritative diff is
        // the one Import returns. This is the cheap preview: it says whether the
        // population moved, and by how much.
        int added = Math.Max(0, inTrial - inCurrent);
        int removed = Math.Max(0, inCurrent - inTrial);
        var samples = new List<string>
        {
            $"{kind}: nel database {inCurrent}, nel client {inTrial}"
        };
        return new ReferenceDiff(kind, added, 0, removed, Math.Min(inCurrent, inTrial), samples);
    }
}
