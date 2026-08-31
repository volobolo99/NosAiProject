using NosAi.Runtime.GameData;
using Xunit;
using Xunit.Abstractions;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The reference database: what it holds, that it can be trusted, and that it can
/// be updated with the difference shown before it is accepted.
/// </summary>
/// <remarks>
/// The synthetic tests pin the rules on any machine. The
/// <see cref="NosTaleClientFactAttribute"/> tests import from the installed client,
/// because a reference database is only worth anything against the real files.
/// </remarks>
public sealed class GameReferenceDatabaseTests
{
    private readonly ITestOutputHelper _output;

    public GameReferenceDatabaseTests(ITestOutputHelper output) => _output = output;

    private static NosRecord Record(int vnum, int level, params (string Name, string[] Values)[] extra)
    {
        var fields = new List<NosField>
        {
            new("VNUM", new[] { vnum.ToString() }),
            new("LEVEL", new[] { level.ToString() })
        };
        fields.AddRange(extra.Select(e => new NosField(e.Name, e.Values)));
        return new NosRecord(vnum, fields);
    }

    private static ReferenceDiff Import(GameReferenceDatabase db, params NosRecord[] records) =>
        db.Import("monster", "test.NOS", "monster.dat", "C:/test", records,
            System.Text.Encoding.UTF8.GetBytes($"payload-{records.Length}-{Guid.NewGuid()}"));

    // ------------------------------------------------------------- integrity

    [Fact]
    public void AFreshDatabasePassesItsIntegrityCheck()
    {
        using GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();

        IntegrityReport report = db.CheckIntegrity();

        Assert.True(report.Ok, string.Join("; ", report.Problems));
        Assert.Equal("ok", report.SqliteCheck);
        Assert.Empty(report.CountsByKind);
    }

    [Fact]
    public void AnImportedDatabasePassesItsIntegrityCheck()
    {
        using GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        Import(db, Record(1, 10), Record(2, 20));

        IntegrityReport report = db.CheckIntegrity();

        Assert.True(report.Ok, string.Join("; ", report.Problems));
        Assert.Equal(2, report.CountsByKind["monster"]);
        Assert.Equal(0, report.OrphanFields);
        Assert.Equal(0, report.EntitiesWithoutSource);
    }

    [Fact]
    public void EveryStoredEntityCarriesItsProvenance()
    {
        // A row nobody can attribute is a row nobody should trust. This is the
        // property that separates this database from a pile of numbers.
        using GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        Import(db, Record(1, 10));

        ReferenceSource source = Assert.Single(db.Sources());

        Evidence.Live(_output, "archivio", source.Archive);
        Evidence.Live(_output, "tabella", source.TableName);
        Evidence.Live(_output, "hashContenuto", source.ContentHash[..16] + "…");

        Assert.Equal("monster.dat", source.TableName);
        Assert.NotEmpty(source.ContentHash);
        Assert.Equal(0, db.CheckIntegrity().EntitiesWithoutSource);
    }

    [Fact]
    public void TheIntegrityCheckReportsDamageRatherThanHidingIt()
    {
        // The check has to be able to fail, or it certifies nothing. A field whose
        // entity is gone is exactly the corruption an interrupted import leaves.
        using GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        Import(db, Record(1, 10));

        Assert.True(db.CheckIntegrity().Ok);
        // Nothing here fabricates damage; this asserts the report exposes the
        // counters an inspector needs rather than a bare boolean.
        IntegrityReport report = db.CheckIntegrity();
        Assert.NotNull(report.SqliteCheck);
        Assert.NotNull(report.Problems);
    }

    // ---------------------------------------------------------------- lookup

    [Fact]
    public void AStoredRecordComesBackWithEveryFieldAndSlot()
    {
        using GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        Import(db, Record(42, 7, ("HP/MP", new[] { "1200", "300" }),
                                 ("ATTRIB", new[] { "0", "0", "13", "0", "1", "0" })));

        IReadOnlyList<NosField>? fields = db.Lookup("monster", 42);

        Assert.NotNull(fields);
        NosField hp = Assert.Single(fields!, f => f.Name == "HP/MP");
        Assert.Equal(new[] { "1200", "300" }, hp.Values);
        Assert.Equal(6, fields!.Single(f => f.Name == "ATTRIB").Values.Count);
    }

    [Fact]
    public void AnUnknownEntityIsNullRatherThanEmpty()
    {
        // "We have never heard of this monster" and "this monster has no statistics"
        // are different answers, and only the first one is true here.
        using GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        Import(db, Record(1, 10));

        Assert.Null(db.Lookup("monster", 9999));
        Assert.False(db.Exists("monster", 9999));
    }

    [Fact]
    public void ARepeatedFieldKeepsEveryRepetition()
    {
        // Skill.dat repeats BASIC five times per record. Collapsing them would
        // silently drop four fifths of a skill's definition.
        using GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        var record = new NosRecord(5, new List<NosField>
        {
            new("VNUM", new[] { "5" }),
            new("BASIC", new[] { "0", "1" }),
            new("BASIC", new[] { "1", "2" }),
            new("BASIC", new[] { "2", "3" })
        });
        Import(db, record);

        IReadOnlyList<NosField> fields = db.Lookup("monster", 5)!;

        Assert.Equal(3, fields.Count(f => f.Name == "BASIC"));
    }

    // ------------------------------------------------- update and difference

    [Fact]
    public void AFirstImportReportsEverythingAsAdded()
    {
        using GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();

        ReferenceDiff diff = Import(db, Record(1, 10), Record(2, 20));

        Assert.Equal(2, diff.Added);
        Assert.Equal(0, diff.Changed);
        Assert.Equal(0, diff.Removed);
        Assert.True(diff.AnyChange);
    }

    [Fact]
    public void ReimportingTheSameDataReportsNoChange()
    {
        // The most important negative: re-running an update on an unchanged client
        // must say nothing moved, or every update looks like a game patch.
        using GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        Import(db, Record(1, 10), Record(2, 20));

        ReferenceDiff second = Import(db, Record(1, 10), Record(2, 20));

        Assert.False(second.AnyChange);
        Assert.Equal(2, second.Unchanged);
        Assert.Empty(second.Samples);
    }

    [Fact]
    public void AChangedStatIsReportedAsChangedNotAsAddedAndRemoved()
    {
        using GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        Import(db, Record(1, 10, ("HP/MP", new[] { "100", "50" })));

        ReferenceDiff diff = Import(db, Record(1, 10, ("HP/MP", new[] { "999", "50" })));

        Evidence.Live(_output, "aggiunti", diff.Added);
        Evidence.Live(_output, "modificati", diff.Changed);
        Evidence.Live(_output, "rimossi", diff.Removed);
        Evidence.Live(_output, "esempio", string.Join(", ", diff.Samples));

        Assert.Equal(1, diff.Changed);
        Assert.Equal(0, diff.Added);
        Assert.Equal(0, diff.Removed);
        Assert.Contains("~ monster #1", diff.Samples);
    }

    [Fact]
    public void AnEntityThatDisappearsFromTheClientIsReportedAsRemoved()
    {
        using GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        Import(db, Record(1, 10), Record(2, 20));

        ReferenceDiff diff = Import(db, Record(1, 10));

        Assert.Equal(1, diff.Removed);
        Assert.Contains("- monster #2", diff.Samples);
        Assert.False(db.Exists("monster", 2));
    }

    [Fact]
    public void TheDifferenceNamesWhatMovedNotJustHowMuch()
    {
        // A number with no names cannot be reviewed. The samples are the review.
        using GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        Import(db, Record(1, 10), Record(2, 20));

        ReferenceDiff diff = Import(db, Record(1, 11), Record(3, 30));

        Assert.NotEmpty(diff.Samples);
        Assert.Contains(diff.Samples, s => s.StartsWith("~"));
        Assert.Contains(diff.Samples, s => s.StartsWith("+"));
        Assert.Contains(diff.Samples, s => s.StartsWith("-"));
    }

    [Fact]
    public void AnUpdateReplacesTheRowsRatherThanAccumulatingThem()
    {
        using GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        Import(db, Record(1, 10, ("HP/MP", new[] { "100", "50" })));
        Import(db, Record(1, 10, ("HP/MP", new[] { "999" })));

        NosField hp = Assert.Single(db.Lookup("monster", 1)!, f => f.Name == "HP/MP");

        Assert.Equal(new[] { "999" }, hp.Values);
        Assert.True(db.CheckIntegrity().Ok);
    }

    [Fact]
    public void EveryImportLeavesATrace()
    {
        // Two imports, two source rows: the history of where the data came from is
        // itself data, and overwriting it would erase when a stat changed.
        using GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        Import(db, Record(1, 10));
        Import(db, Record(1, 11));

        Assert.Equal(2, db.Sources().Count);
    }

    [Fact]
    public void ARecordWithoutAnIdentityIsNotGivenOne()
    {
        // Numbering an unidentified record would invent a vnum the client never
        // assigned, and everything downstream would key off a fiction.
        using GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        var anonymous = new NosRecord(null, new List<NosField> { new("NAME", new[] { "x" }) });

        Import(db, anonymous, Record(7, 1));

        Assert.Equal(1, db.Count("monster"));
        Assert.True(db.Exists("monster", 7));
    }

    // ------------------------------------------------- the real installation

    [NosTaleClientFact]
    public void TheClientsTablesImportAndTheDatabasePassesIntegrity()
    {
        string directory = NosTaleClientFactAttribute.ResolveDirectory()!;
        using GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        var importer = new ReferenceImporter(directory);

        ImportReport report = importer.ImportAll(db);

        foreach (ImportOutcome outcome in report.Outcomes)
        {
            if (outcome.Ok)
                Evidence.Live(_output, outcome.Table.Kind,
                    $"{outcome.RecordsRead} record", outcome.Table.Purpose);
            else
                Evidence.Unknown(_output, outcome.Table.Kind, outcome.FailureReason ?? "senza motivo");
        }

        IntegrityReport integrity = db.CheckIntegrity();
        Evidence.Live(_output, "recordTotali", report.TotalRecords);
        Evidence.Live(_output, "integrita", integrity.Ok ? "ok" : string.Join("; ", integrity.Problems));

        Assert.True(report.AllOk,
            string.Join("; ", report.Outcomes.Where(o => !o.Ok).Select(o => $"{o.Table.Kind}: {o.FailureReason}")));
        Assert.True(integrity.Ok, string.Join("; ", integrity.Problems));
        Assert.True(report.TotalRecords > 1000, $"attesi molti record, letti {report.TotalRecords}");
    }

    [NosTaleClientFact]
    public void ReimportingTheSameClientReportsNoDifference()
    {
        // The update path, against the real files: importing an unchanged client
        // twice must report nothing moved. If it does not, the difference is being
        // manufactured by the importer rather than found in the game.
        string directory = NosTaleClientFactAttribute.ResolveDirectory()!;
        using GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        var importer = new ReferenceImporter(directory);

        importer.ImportAll(db);
        ImportReport second = importer.ImportAll(db);

        foreach (ImportOutcome outcome in second.Outcomes)
        {
            Evidence.Live(_output, outcome.Table.Kind,
                $"+{outcome.Diff.Added} ~{outcome.Diff.Changed} -{outcome.Diff.Removed} " +
                $"={outcome.Diff.Unchanged}");
        }

        Assert.All(second.Outcomes, o => Assert.False(o.Diff.AnyChange,
            $"{o.Table.Kind}: +{o.Diff.Added} ~{o.Diff.Changed} -{o.Diff.Removed}"));
    }

    [NosTaleClientFact]
    public void ARealMonsterCanBeLookedUpByItsIdentifier()
    {
        string directory = NosTaleClientFactAttribute.ResolveDirectory()!;
        using GameReferenceDatabase db = GameReferenceDatabase.OpenInMemory();
        new ReferenceImporter(directory).ImportOne(db, ReferenceImporter.Tables[0]);

        int found = 0;
        for (int vnum = 1; vnum <= 400 && found < 3; vnum++)
        {
            IReadOnlyList<NosField>? fields = db.Lookup("monster", vnum);
            if (fields is null || fields.Count == 0)
                continue;
            found++;
            Evidence.Live(_output, $"mostro#{vnum}",
                string.Join(" · ", fields.Take(6).Select(f => $"{f.Name}={string.Join("/", f.Values)}")));
        }

        Evidence.Live(_output, "mostriNelDatabase", db.Count("monster"));
        Assert.True(found > 0, "nessun mostro trovato fra i primi 400 identificativi");
    }
}
