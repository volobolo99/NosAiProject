using Microsoft.Data.Sqlite;
using NosAi.Core.Memory;
using NosAi.Core.WorldModel;
using NosAi.Runtime.Observability;
using NosAi.Storage;
using System.IO;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// <c>--outcome-report</c>: reads the durable action-outcome ledger back,
/// offline and read-only, against a real temp-file SQLite store (never
/// <c>:memory:</c>, for the same WAL reason <c>ActionOutcomeLedgerStoreTests</c>
/// records). No volume and no mock: the constructor that takes an explicit
/// path is what keeps every case here offline.
/// </summary>
public sealed class OutcomeReportTests : IDisposable
{
    private readonly string _databasePath;

    public OutcomeReportTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"nosai-outcome-report-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    private static readonly DateTime T0 = new(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);

    private ActionOutcomeLedgerStore Open() => new(_databasePath, Options());

    private static SqliteJournalOptions Options() => new(FileName: "irrelevant.db");

    private static ActionOutcomeLedgerEntry Entry(
        string context, ActionOutcome outcome, DataSourceKind source, DateTime when, string actionId = "action")
        => new(
            Guid.NewGuid(),
            new ActionId(actionId),
            MemoryType.Combat,
            Fact(outcome, source, when),
            context,
            when);

    private static WorldFact<ActionOutcome> Fact(ActionOutcome outcome, DataSourceKind source, DateTime when)
        => source switch
        {
            DataSourceKind.Derived => WorldFact<ActionOutcome>.Derived(outcome, 1d, when, "measured"),
            DataSourceKind.Cached => WorldFact<ActionOutcome>.Cached(outcome, 1d, when, "measured"),
            DataSourceKind.Simulated => WorldFact<ActionOutcome>.Simulated(outcome, 1d, when, "measured"),
            _ => WorldFact<ActionOutcome>.Live(outcome, 1d, when, "measured"),
        };

    private static ActionOutcomeLedgerEntry UnknownEntry(string context, DateTime when, string actionId = "action")
        => new(
            Guid.NewGuid(),
            new ActionId(actionId),
            MemoryType.Combat,
            WorldFact<ActionOutcome>.Unknown("guard_refused", when),
            context,
            when);

    // -- Contexts() -----------------------------------------------------------

    [Fact]
    public void Contexts_OnAnEmptyStore_ReturnsEmpty()
    {
        using var store = Open();
        Assert.Empty(store.Contexts());
    }

    [Fact]
    public void Contexts_WithThreeContexts_ReturnsThemOrderedWithoutDuplicates()
    {
        using var store = Open();
        store.Append(Entry("zeta", ActionOutcome.Succeeded, DataSourceKind.Live, T0));
        store.Append(Entry("alpha", ActionOutcome.Failed, DataSourceKind.Live, T0));
        store.Append(Entry("middle", ActionOutcome.Succeeded, DataSourceKind.Live, T0));
        store.Append(Entry("alpha", ActionOutcome.Succeeded, DataSourceKind.Live, T0.AddSeconds(1)));

        EquatableArray<string> contexts = store.Contexts();

        Assert.Equal(3, contexts.Count);
        Assert.Equal("alpha", contexts[0]);
        Assert.Equal("middle", contexts[1]);
        Assert.Equal("zeta", contexts[2]);
    }

    // -- summary --------------------------------------------------------------

    [Fact]
    public void Summarize_CountsRowsPerContextAndOutcomeDistribution()
    {
        using var store = Open();
        store.Append(Entry("combat", ActionOutcome.Succeeded, DataSourceKind.Live, T0));
        store.Append(Entry("combat", ActionOutcome.Failed, DataSourceKind.Live, T0.AddSeconds(1)));
        store.Append(Entry("combat", ActionOutcome.Succeeded, DataSourceKind.Live, T0.AddSeconds(2)));
        store.Append(Entry("other", ActionOutcome.InProgress, DataSourceKind.Live, T0.AddSeconds(3)));

        OutcomeContextSummary combat = Assert.Single(OutcomeReportCommand.Summarize(store), c => c.Context == "combat");

        Assert.Equal(3, combat.Rows);
        Assert.Equal(2, combat.Succeeded);
        Assert.Equal(1, combat.Failed);
        Assert.Equal(0, combat.InProgress);
    }

    [Fact]
    public void Summarize_CountsUnknownOutcomesApart_NeverAmongFailures()
    {
        using var store = Open();
        store.Append(Entry("guard", ActionOutcome.Failed, DataSourceKind.Live, T0));
        store.Append(UnknownEntry("guard", T0.AddSeconds(1)));
        store.Append(UnknownEntry("guard", T0.AddSeconds(2)));
        store.Append(UnknownEntry("guard", T0.AddSeconds(3)));

        OutcomeContextSummary guard = Assert.Single(OutcomeReportCommand.Summarize(store), c => c.Context == "guard");

        Assert.Equal(4, guard.Rows);
        Assert.Equal(1, guard.Failed);
        Assert.Equal(3, guard.Unknown); // never folded into Failed
    }

    [Fact]
    public void Summarize_ReportsTheRealFirstAndLastRecordedAtUtc_EvenWhenAppendedOutOfOrder()
    {
        using var store = Open();
        store.Append(Entry("t", ActionOutcome.Succeeded, DataSourceKind.Live, T0.AddSeconds(30)));
        store.Append(Entry("t", ActionOutcome.Succeeded, DataSourceKind.Live, T0));
        store.Append(Entry("t", ActionOutcome.Succeeded, DataSourceKind.Live, T0.AddSeconds(10)));

        OutcomeContextSummary summary = Assert.Single(OutcomeReportCommand.Summarize(store));

        Assert.Equal(T0, summary.FirstRecordedAtUtc);
        Assert.Equal(T0.AddSeconds(30), summary.LastRecordedAtUtc);
    }

    [Fact]
    public void Summarize_ReportsProvenancePerKind()
    {
        using var store = Open();
        store.Append(Entry("p", ActionOutcome.Succeeded, DataSourceKind.Live, T0));
        store.Append(Entry("p", ActionOutcome.Succeeded, DataSourceKind.Derived, T0.AddSeconds(1)));
        store.Append(UnknownEntry("p", T0.AddSeconds(2)));

        OutcomeContextSummary summary = Assert.Single(OutcomeReportCommand.Summarize(store));

        Assert.Equal(1, summary.SourcesByKind[DataSourceKind.Live]);
        Assert.Equal(1, summary.SourcesByKind[DataSourceKind.Derived]);
        Assert.Equal(1, summary.SourcesByKind[DataSourceKind.Unknown]);
    }

    // -- --context rows -------------------------------------------------------

    [Fact]
    public void Rows_ForAnExistingContext_AreOldestFirst()
    {
        using var store = Open();
        store.Append(Entry("walk", ActionOutcome.Succeeded, DataSourceKind.Live, T0.AddSeconds(5), actionId: "later"));
        store.Append(Entry("walk", ActionOutcome.Failed, DataSourceKind.Live, T0, actionId: "earlier"));
        store.Append(UnknownEntry("walk", T0.AddSeconds(3), actionId: "unknown"));

        IReadOnlyList<OutcomeContextRow> rows = OutcomeReportCommand.Rows(store, "walk");

        Assert.Equal(3, rows.Count);
        Assert.Equal("earlier", rows[0].ActionId);
        Assert.Equal("unknown", rows[1].ActionId);
        Assert.Equal("later", rows[2].ActionId);
        Assert.Null(rows[1].Outcome); // the unknown row carries no fabricated outcome
        Assert.Equal(DataSourceKind.Unknown, rows[1].Source);
    }

    [Fact]
    public void HasContext_IsFalseForANeverRecordedContext_AndTrueForOneWithRows()
    {
        using var store = Open();
        store.Append(Entry("real", ActionOutcome.Succeeded, DataSourceKind.Live, T0));

        Assert.True(OutcomeReportCommand.HasContext(store, "real"));
        Assert.False(OutcomeReportCommand.HasContext(store, "never-recorded"));
    }

    // -- refusals -------------------------------------------------------------

    [Theory]
    [InlineData("--context")]
    [InlineData("--context", "--other-flag")]
    public void TryParse_ContextWithoutValue_IsRefused(string first, string? second = null)
    {
        string[] args = second is null ? new[] { first } : new[] { first, second };

        string? refusal = OutcomeReportCommand.TryParse(args, out string? context);

        Assert.Equal(OutcomeReportCommand.ContextWithoutValueReason, refusal);
        Assert.Null(context);
    }

    [Fact]
    public void TryParse_ContextWithValue_ParsesTheValue()
    {
        string? refusal = OutcomeReportCommand.TryParse(new[] { "--outcome-report", "--context", "skill:201" }, out string? context);

        Assert.Null(refusal);
        Assert.Equal("skill:201", context);
    }

    [Fact]
    public void TryOpenFromVolume_ForANonexistentVolume_ReturnsNullWithAReason()
    {
        // The exact mechanism Run relies on to name the volume-absent refusal:
        // a label with no attached drive yields null and a non-empty reason,
        // deterministically, on every machine (no real NOSAI-SSD required).
        using ActionOutcomeLedgerStore? store = ActionOutcomeLedgerStore.TryOpenFromVolume(
            new SqliteJournalOptions(VolumeLabel: "NOSAI-VOLUME-DOES-NOT-EXIST-9F3E"), out string? failureReason);

        Assert.Null(store);
        Assert.False(string.IsNullOrWhiteSpace(failureReason));
    }

    // -- append-only ----------------------------------------------------------

    [Fact]
    public void AppendingTheSameEntryIdTwice_IsRejectedByThePrimaryKey_AndTheReportDoesNotCountTwice()
    {
        using (var store = Open())
        {
            ActionOutcomeLedgerEntry entry = Entry("dup", ActionOutcome.Succeeded, DataSourceKind.Live, T0);
            store.Append(entry);

            ActionOutcomeLedgerEntry duplicate = entry;
            Assert.Throws<SqliteException>(() => store.Append(duplicate));
        }

        using var reopened = Open();
        OutcomeContextSummary summary = Assert.Single(OutcomeReportCommand.Summarize(reopened));

        Assert.Equal(1, summary.Rows);
        Assert.Equal(1, summary.Succeeded);
    }

    // -- WriteReport (the actual report path) ---------------------------------

    [Fact]
    public void WriteReport_SummaryMode_RendersEveryContextWithItsCounts()
    {
        using var store = Open();
        store.Append(Entry("combat", ActionOutcome.Succeeded, DataSourceKind.Live, T0));
        store.Append(UnknownEntry("guard", T0.AddSeconds(1)));

        var output = new StringWriter();
        OutcomeReportCommand.WriteReport(store, context: null, output);

        string text = output.ToString();
        Assert.Contains("=== action-outcome ledger ===", text, StringComparison.Ordinal);
        Assert.Contains("context combat: rows=1 succeeded=1 failed=0 in_progress=0 unknown=0", text, StringComparison.Ordinal);
        Assert.Contains("context guard: rows=1 succeeded=0 failed=0 in_progress=0 unknown=1", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Un rapporto di sola lettura non crea il registro che dice di leggere.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Misurato il 2026-09-07: eseguire <c>--outcome-report</c> su una macchina
    /// con il volume collegato e nessun atto mai registrato **creava**
    /// <c>D:\nosai.db</c> (16 KB, vuoto) e poi riferiva «no contexts recorded».
    /// Due danni in una riga: un comando di sola lettura scriveva sul volume
    /// dell'operatore, e la prima esecuzione trasformava «il registro non e' mai
    /// stato creato» in «il registro esiste ed e' vuoto» -- cioe' cancellava una
    /// delle quattro distinzioni che questo rapporto esiste per fare.
    /// </para>
    /// <para>
    /// Aprire uno store SQLite lo crea: l'unico modo di distinguere i due casi e'
    /// guardare il file <b>prima</b> di aprirlo.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnAbsentLedgerIsReportedWithoutBeingCreated()
    {
        // La guardia vera, chiamata davvero. Fino al 2026-09-08 questo test
        // guardava due volte lo stesso percorso inesistente senza mai invocare il
        // comando: asseriva la propria premessa.
        string directory = Path.Combine(Path.GetTempPath(), $"nosai_outcome_{Guid.NewGuid():N}");
        string absent = Path.Combine(directory, "nosai.db");
        var output = new StringWriter();

        bool reported = OutcomeReportCommand.TryReportNeverCreated(absent, output);

        Assert.True(reported);
        Assert.Contains(
            OutcomeReportCommand.LedgerNotCreatedReason, output.ToString(), StringComparison.Ordinal);

        // E il registro continua a non esistere: guardare non apre.
        Assert.False(File.Exists(absent));
        Assert.False(Directory.Exists(directory));
    }

    /// <summary>
    /// I due casi vuoti non dicono la stessa cosa.
    /// </summary>
    [Fact]
    public void AnEmptyStoreAndAnAbsentLedgerDoNotShareAMessage()
    {
        using var store = Open();
        var output = new StringWriter();
        OutcomeReportCommand.WriteReport(store, context: null, output);

        // Registro aperto e vuoto.
        Assert.Contains("no contexts recorded", output.ToString(), StringComparison.Ordinal);

        // Registro mai creato: un'altra frase, e nominata.
        Assert.Equal("outcome_ledger_never_created", OutcomeReportCommand.LedgerNotCreatedReason);
        Assert.DoesNotContain(
            OutcomeReportCommand.LedgerNotCreatedReason, output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void WriteReport_SummaryMode_OnAnEmptyStore_SaysNoContextsRecorded()
    {
        using var store = Open();

        var output = new StringWriter();
        OutcomeReportCommand.WriteReport(store, context: null, output);

        Assert.Contains("no contexts recorded", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void WriteReport_ContextMode_RendersRowsInTimeOrder_WithOutcomeAndProvenance()
    {
        using var store = Open();
        store.Append(Entry("walk", ActionOutcome.Succeeded, DataSourceKind.Live, T0, actionId: "first"));
        store.Append(UnknownEntry("walk", T0.AddSeconds(1), actionId: "second"));

        var output = new StringWriter();
        OutcomeReportCommand.WriteReport(store, context: "walk", output);

        string text = output.ToString();
        Assert.Contains("=== context walk: 2 rows ===", text, StringComparison.Ordinal);
        Assert.Contains("first  Succeeded  Live", text, StringComparison.Ordinal);
        Assert.Contains("second  Unknown  Unknown", text, StringComparison.Ordinal);
    }

    // -- wiring ---------------------------------------------------------------

    [Fact]
    public void TheRuntimeWiresTheOutcomeReportFlag()
    {
        string program = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "NosAi.Runtime", "Program.cs"));
        Assert.Contains("OutcomeReportCommand.Run", program, StringComparison.Ordinal);
        Assert.Contains("OutcomeReportCommand.Flag", program, StringComparison.Ordinal);
        Assert.Contains("\"--outcome-report\"", program, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
