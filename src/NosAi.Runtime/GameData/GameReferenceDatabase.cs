using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace NosAi.Runtime.GameData;

/// <summary>Where one table's rows came from, so no row is unattributable.</summary>
public sealed record ReferenceSource(
    long SourceId,
    string Archive,
    string TableName,
    string ContentHash,
    DateTime ImportedAtUtc,
    string ClientPath,
    int RecordCount);

/// <summary>What an import changed, entity by entity.</summary>
/// <remarks>
/// The counts alone would say a table moved; the samples say what moved. An update
/// that reports "1 200 changed" without naming any of them cannot be reviewed.
/// </remarks>
public sealed record ReferenceDiff(
    string Kind,
    int Added,
    int Changed,
    int Removed,
    int Unchanged,
    IReadOnlyList<string> Samples)
{
    public bool AnyChange => Added > 0 || Changed > 0 || Removed > 0;

    public static ReferenceDiff Empty(string kind) =>
        new(kind, 0, 0, 0, 0, Array.Empty<string>());
}

/// <summary>What an integrity check found.</summary>
public sealed record IntegrityReport(
    bool Ok,
    IReadOnlyList<string> Problems,
    IReadOnlyDictionary<string, int> CountsByKind,
    int OrphanFields,
    int EntitiesWithoutSource,
    string? SqliteCheck);

/// <summary>
/// The reference data the client ships, stored so it can be read fast and trusted.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here is invented.</b> Every row comes from a table decoded out of the
/// installed client and is tied to a <see cref="ReferenceSource"/> recording which
/// archive it came from, the hash of the decoded bytes, and when. A row with no
/// source is a defect the integrity check reports, not a row to be trusted.
/// </para>
/// <para>
/// <b>What is stored, and what is not interpreted.</b> The record is kept exactly
/// as the client wrote it: field name, which repetition, which slot, the value.
/// Only what the field names themselves state — <c>VNUM</c> and <c>LEVEL</c> — is
/// promoted to a typed column. Which slot of <c>ATTRIB</c> is the element, or which
/// of <c>WEAPON</c> is the attack value, is not something these files declare, and
/// guessing it here would put a number nobody verified into the middle of a damage
/// calculation. That mapping belongs to a later layer that can be checked against
/// what actually happens in game.
/// </para>
/// <para>
/// <b>The hot path is a point lookup.</b> A monster appears and the runtime needs
/// its statistics now, so <c>entity</c> is keyed on <c>(kind, vnum)</c> and the
/// whole record is on that row. Searching by field value is a second, indexed path.
/// </para>
/// </remarks>
public sealed class GameReferenceDatabase : IDisposable
{
    /// <summary>Bumped when the layout changes in a way that needs a rebuild.</summary>
    public const int SchemaVersion = 1;

    private readonly SqliteConnection _connection;
    private bool _disposed;

    public string DatabasePath { get; }

    private GameReferenceDatabase(SqliteConnection connection, string path)
    {
        _connection = connection;
        DatabasePath = path;
    }

    /// <summary>Opens or creates the database at <paramref name="path"/>.</summary>
    public static GameReferenceDatabase Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false
        }.ToString());
        connection.Open();

        var database = new GameReferenceDatabase(connection, path);
        database.CreateSchema();
        return database;
    }

    /// <summary>Opens a database that lives only for this process. Used by tests.</summary>
    public static GameReferenceDatabase OpenInMemory()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var database = new GameReferenceDatabase(connection, ":memory:");
        database.CreateSchema();
        return database;
    }

    private void CreateSchema()
    {
        Execute("PRAGMA journal_mode=WAL");
        Execute("PRAGMA synchronous=FULL");
        Execute("PRAGMA foreign_keys=ON");

        Execute($"PRAGMA user_version={SchemaVersion}");

        Execute("""
            CREATE TABLE IF NOT EXISTS source (
                source_id       INTEGER PRIMARY KEY AUTOINCREMENT,
                archive         TEXT    NOT NULL,
                table_name      TEXT    NOT NULL,
                content_hash    TEXT    NOT NULL,
                imported_at_utc TEXT    NOT NULL,
                client_path     TEXT    NOT NULL,
                record_count    INTEGER NOT NULL
            )
            """);

        // One row per entity: the point lookup the runtime does while playing.
        Execute("""
            CREATE TABLE IF NOT EXISTS entity (
                kind        TEXT    NOT NULL,
                vnum        INTEGER NOT NULL,
                level       INTEGER,
                name_key    TEXT,
                source_id   INTEGER NOT NULL REFERENCES source(source_id),
                record_hash TEXT    NOT NULL,
                PRIMARY KEY (kind, vnum)
            )
            """);

        // The record as the client wrote it. Nothing is folded away.
        Execute("""
            CREATE TABLE IF NOT EXISTS field (
                kind     TEXT    NOT NULL,
                vnum     INTEGER NOT NULL,
                name     TEXT    NOT NULL,
                ordinal  INTEGER NOT NULL,
                slot     INTEGER NOT NULL,
                value    TEXT    NOT NULL,
                PRIMARY KEY (kind, vnum, name, ordinal, slot),
                FOREIGN KEY (kind, vnum) REFERENCES entity(kind, vnum) ON DELETE CASCADE
            )
            """);

        // The names and descriptions the client displays. A record's NAME is a key
        // such as "zts1e"; this is what turns it into "Volpe piccola".
        Execute("""
            CREATE TABLE IF NOT EXISTS text (
                language   TEXT NOT NULL,
                table_name TEXT NOT NULL,
                key        TEXT NOT NULL,
                value      TEXT NOT NULL,
                PRIMARY KEY (language, table_name, key)
            )
            """);

        Execute("CREATE INDEX IF NOT EXISTS ix_field_lookup ON field(kind, name, value)");
        Execute("CREATE INDEX IF NOT EXISTS ix_entity_level ON entity(kind, level)");
    }

    // -------------------------------------------------------------- importing

    /// <summary>
    /// Replaces one kind's rows with the given records, reporting what changed.
    /// </summary>
    /// <remarks>
    /// The diff is computed before anything is written, so an update can be
    /// inspected and the whole import rolled back as one transaction: a half-applied
    /// reference table is worse than an out-of-date one, because it is wrong without
    /// looking wrong.
    /// </remarks>
    public ReferenceDiff Import(
        string kind,
        string archive,
        string tableName,
        string clientPath,
        IReadOnlyList<NosRecord> records,
        byte[] decodedBytes,
        DateTime? importedAtUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(records);

        Dictionary<int, string> existing = ReadHashes(kind);
        var incoming = new Dictionary<int, (NosRecord Record, string Hash)>();

        foreach (NosRecord record in records)
        {
            if (record.Vnum is not int vnum)
                continue;  // No identity the client assigned; not given one here.
            incoming[vnum] = (record, HashRecord(record));
        }

        ReferenceDiff diff = Compare(kind, existing, incoming);

        using SqliteTransaction transaction = _connection.BeginTransaction();

        long sourceId = InsertSource(transaction, archive, tableName,
            Sha256(decodedBytes), importedAtUtc ?? DateTime.UtcNow, clientPath, incoming.Count);

        Execute(transaction, "DELETE FROM field WHERE kind = $kind", ("$kind", kind));
        Execute(transaction, "DELETE FROM entity WHERE kind = $kind", ("$kind", kind));

        foreach ((int vnum, (NosRecord record, string hash)) in incoming)
        {
            InsertEntity(transaction, kind, vnum, record, hash, sourceId);
            InsertFields(transaction, kind, vnum, record);
        }

        transaction.Commit();
        return diff;
    }

    private static ReferenceDiff Compare(
        string kind,
        Dictionary<int, string> existing,
        Dictionary<int, (NosRecord Record, string Hash)> incoming)
    {
        int added = 0, changed = 0, unchanged = 0;
        var samples = new List<string>();

        foreach ((int vnum, (_, string hash)) in incoming)
        {
            if (!existing.TryGetValue(vnum, out string? old))
            {
                added++;
                if (samples.Count < 20)
                    samples.Add($"+ {kind} #{vnum}");
            }
            else if (old != hash)
            {
                changed++;
                if (samples.Count < 20)
                    samples.Add($"~ {kind} #{vnum}");
            }
            else
            {
                unchanged++;
            }
        }

        int removed = 0;
        foreach (int vnum in existing.Keys)
        {
            if (incoming.ContainsKey(vnum))
                continue;
            removed++;
            if (samples.Count < 20)
                samples.Add($"- {kind} #{vnum}");
        }

        return new ReferenceDiff(kind, added, changed, removed, unchanged, samples);
    }

    private Dictionary<int, string> ReadHashes(string kind)
    {
        var map = new Dictionary<int, string>();
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT vnum, record_hash FROM entity WHERE kind = $kind";
        command.Parameters.AddWithValue("$kind", kind);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
            map[reader.GetInt32(0)] = reader.GetString(1);
        return map;
    }

    private long InsertSource(
        SqliteTransaction transaction, string archive, string tableName,
        string hash, DateTime at, string clientPath, int count)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO source (archive, table_name, content_hash, imported_at_utc, client_path, record_count)
            VALUES ($archive, $table, $hash, $at, $path, $count);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$archive", archive);
        command.Parameters.AddWithValue("$table", tableName);
        command.Parameters.AddWithValue("$hash", hash);
        command.Parameters.AddWithValue("$at", at.ToString("O"));
        command.Parameters.AddWithValue("$path", clientPath);
        command.Parameters.AddWithValue("$count", count);
        return (long)(command.ExecuteScalar() ?? 0L);
    }

    private void InsertEntity(
        SqliteTransaction transaction, string kind, int vnum,
        NosRecord record, string hash, long sourceId)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO entity (kind, vnum, level, name_key, source_id, record_hash)
            VALUES ($kind, $vnum, $level, $name, $source, $hash)
            """;
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$vnum", vnum);
        // LEVEL and NAME are promoted because the field names say what they are.
        command.Parameters.AddWithValue("$level",
            (object?)record.Field("LEVEL")?.Int(0) ?? DBNull.Value);
        command.Parameters.AddWithValue("$name",
            (object?)record.Field("NAME")?.Value(0) ?? DBNull.Value);
        command.Parameters.AddWithValue("$source", sourceId);
        command.Parameters.AddWithValue("$hash", hash);
        command.ExecuteNonQuery();
    }

    private void InsertFields(SqliteTransaction transaction, string kind, int vnum, NosRecord record)
    {
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        using SqliteCommand command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR REPLACE INTO field (kind, vnum, name, ordinal, slot, value)
            VALUES ($kind, $vnum, $name, $ordinal, $slot, $value)
            """;
        SqliteParameter pKind = command.Parameters.Add("$kind", SqliteType.Text);
        SqliteParameter pVnum = command.Parameters.Add("$vnum", SqliteType.Integer);
        SqliteParameter pName = command.Parameters.Add("$name", SqliteType.Text);
        SqliteParameter pOrdinal = command.Parameters.Add("$ordinal", SqliteType.Integer);
        SqliteParameter pSlot = command.Parameters.Add("$slot", SqliteType.Integer);
        SqliteParameter pValue = command.Parameters.Add("$value", SqliteType.Text);

        foreach (NosField field in record.Fields)
        {
            // Some fields repeat within one record (BASIC does). The repetition is
            // part of the identity, so the later one does not overwrite the earlier.
            int ordinal = seen.TryGetValue(field.Name, out int previous) ? previous + 1 : 0;
            seen[field.Name] = ordinal;

            for (int slot = 0; slot < field.Values.Count; slot++)
            {
                pKind.Value = kind;
                pVnum.Value = vnum;
                pName.Value = field.Name;
                pOrdinal.Value = ordinal;
                pSlot.Value = slot;
                pValue.Value = field.Values[slot];
                command.ExecuteNonQuery();
            }
        }
    }

    /// <summary>Stores one language table, replacing whatever was there.</summary>
    public int ImportText(string language, string tableName, IReadOnlyDictionary<string, string> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentNullException.ThrowIfNull(entries);

        using SqliteTransaction transaction = _connection.BeginTransaction();
        Execute(transaction, "DELETE FROM text WHERE language = $lang AND table_name = $table",
            ("$lang", language), ("$table", tableName));

        using (SqliteCommand command = _connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT OR REPLACE INTO text (language, table_name, key, value)
                VALUES ($lang, $table, $key, $value)
                """;
            SqliteParameter pLang = command.Parameters.Add("$lang", SqliteType.Text);
            SqliteParameter pTable = command.Parameters.Add("$table", SqliteType.Text);
            SqliteParameter pKey = command.Parameters.Add("$key", SqliteType.Text);
            SqliteParameter pValue = command.Parameters.Add("$value", SqliteType.Text);

            foreach ((string key, string value) in entries)
            {
                pLang.Value = language;
                pTable.Value = tableName;
                pKey.Value = key;
                pValue.Value = value;
                command.ExecuteNonQuery();
            }
        }

        transaction.Commit();
        return entries.Count;
    }

    /// <summary>
    /// The displayed name of an entity, or null when there is none to show.
    /// </summary>
    /// <remarks>
    /// Null rather than the raw key: showing "zts1e" to an operator would look like
    /// a name and be one only by accident.
    /// </remarks>
    public string? DisplayName(string kind, int vnum, string language)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = """
            SELECT t.value FROM entity e
            JOIN text t ON t.key = e.name_key AND t.language = $lang AND t.table_name = $kind
            WHERE e.kind = $kind AND e.vnum = $vnum
            """;
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$vnum", vnum);
        command.Parameters.AddWithValue("$lang", language);
        return command.ExecuteScalar() as string;
    }

    /// <summary>How many entities of a kind resolve to a displayed name.</summary>
    public int NamedCount(string kind, string language)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM entity e
            JOIN text t ON t.key = e.name_key AND t.language = $lang AND t.table_name = $kind
            WHERE e.kind = $kind
            """;
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$lang", language);
        return Convert.ToInt32(command.ExecuteScalar() ?? 0);
    }

    public int TextCount(string language) =>
        ScalarInt($"SELECT COUNT(*) FROM text WHERE language = '{language.Replace("'", "''")}'");

    // --------------------------------------------------------------- reading

    /// <summary>The fields of one entity, or null when it is not in the database.</summary>
    /// <remarks>
    /// Null rather than an empty record: "this monster is unknown to us" and "this
    /// monster has no statistics" are different, and only the first is honest here.
    /// </remarks>
    public IReadOnlyList<NosField>? Lookup(string kind, int vnum)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = """
            SELECT name, ordinal, slot, value FROM field
            WHERE kind = $kind AND vnum = $vnum
            ORDER BY name, ordinal, slot
            """;
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$vnum", vnum);

        var grouped = new Dictionary<(string, int), List<string>>();
        using (SqliteDataReader reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                var key = (reader.GetString(0), reader.GetInt32(1));
                if (!grouped.TryGetValue(key, out List<string>? values))
                    grouped[key] = values = new List<string>();
                values.Add(reader.GetString(3));
            }
        }

        if (grouped.Count == 0)
            return Exists(kind, vnum) ? Array.Empty<NosField>() : null;

        return grouped.Select(pair => new NosField(pair.Key.Item1, pair.Value)).ToArray();
    }

    public bool Exists(string kind, int vnum)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM entity WHERE kind = $kind AND vnum = $vnum";
        command.Parameters.AddWithValue("$kind", kind);
        command.Parameters.AddWithValue("$vnum", vnum);
        return command.ExecuteScalar() is not null;
    }

    public int Count(string kind)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM entity WHERE kind = $kind";
        command.Parameters.AddWithValue("$kind", kind);
        return Convert.ToInt32(command.ExecuteScalar() ?? 0);
    }

    /// <summary>Every import recorded, newest first.</summary>
    public IReadOnlyList<ReferenceSource> Sources()
    {
        var sources = new List<ReferenceSource>();
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = """
            SELECT source_id, archive, table_name, content_hash, imported_at_utc, client_path, record_count
            FROM source ORDER BY source_id DESC
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            sources.Add(new ReferenceSource(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                DateTime.Parse(reader.GetString(4), null, System.Globalization.DateTimeStyles.RoundtripKind),
                reader.GetString(5), reader.GetInt32(6)));
        }
        return sources;
    }

    // ------------------------------------------------------------- integrity

    /// <summary>
    /// Checks the database against the invariants that make it trustworthy.
    /// </summary>
    /// <remarks>
    /// Not only SQLite's own structural check: an intact file full of rows nobody
    /// can attribute is exactly the failure this project cares about, so provenance
    /// is checked too.
    /// </remarks>
    public IntegrityReport CheckIntegrity()
    {
        var problems = new List<string>();

        string sqliteCheck = ScalarString("PRAGMA integrity_check") ?? "(nessun risultato)";
        if (!string.Equals(sqliteCheck, "ok", StringComparison.OrdinalIgnoreCase))
            problems.Add($"integrity_check SQLite: {sqliteCheck}");

        string foreignKeys = ScalarString("SELECT COUNT(*) FROM pragma_foreign_key_check") ?? "0";
        if (foreignKeys != "0")
            problems.Add($"violazioni di chiave esterna: {foreignKeys}");

        int orphanFields = ScalarInt("""
            SELECT COUNT(*) FROM field f
            WHERE NOT EXISTS (SELECT 1 FROM entity e WHERE e.kind = f.kind AND e.vnum = f.vnum)
            """);
        if (orphanFields > 0)
            problems.Add($"campi senza entità: {orphanFields}");

        int withoutSource = ScalarInt("""
            SELECT COUNT(*) FROM entity e
            WHERE NOT EXISTS (SELECT 1 FROM source s WHERE s.source_id = e.source_id)
            """);
        if (withoutSource > 0)
            problems.Add($"entità senza provenienza: {withoutSource}");

        int emptyEntities = ScalarInt("""
            SELECT COUNT(*) FROM entity e
            WHERE NOT EXISTS (SELECT 1 FROM field f WHERE f.kind = e.kind AND f.vnum = e.vnum)
            """);
        if (emptyEntities > 0)
            problems.Add($"entità senza alcun campo: {emptyEntities}");

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        using (SqliteCommand command = _connection.CreateCommand())
        {
            command.CommandText = "SELECT kind, COUNT(*) FROM entity GROUP BY kind ORDER BY kind";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                counts[reader.GetString(0)] = reader.GetInt32(1);
        }

        return new IntegrityReport(
            problems.Count == 0, problems, counts, orphanFields, withoutSource, sqliteCheck);
    }

    // --------------------------------------------------------------- helpers

    /// <summary>ASCII separators, chosen because they cannot occur in the data.</summary>
    /// <remarks>
    /// The hash decides whether an update changed a record, so the separators must
    /// be unambiguous: joining with a comma would make ("a,b") and ("a","b") hash
    /// alike and hide a real change behind an apparent no-op.
    /// </remarks>
    private const char UnitSeparator = '';
    private const char ValueSeparator = '';
    private const char FieldSeparator = '';

    private static string HashRecord(NosRecord record)
    {
        var builder = new StringBuilder();
        foreach (NosField field in record.Fields)
        {
            builder.Append(field.Name).Append(UnitSeparator);
            builder.AppendJoin(ValueSeparator, field.Values).Append(FieldSeparator);
        }
        return Sha256(Encoding.UTF8.GetBytes(builder.ToString()));
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private void Execute(string sql, params (string Name, object Value)[] parameters) =>
        Execute(null, sql, parameters);

    private void Execute(SqliteTransaction? transaction, string sql, params (string Name, object Value)[] parameters)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object value) in parameters)
            command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }

    private string? ScalarString(string sql)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar()?.ToString();
    }

    private int ScalarInt(string sql)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar() ?? 0);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _connection.Dispose();
    }
}
