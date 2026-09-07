using Microsoft.Data.Sqlite;
using NosAi.Runtime.Observability;
using NosAi.Storage;
using System.IO;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// <c>--learning-report</c>: reads the durable prediction-calibration store
/// back, offline and read-only, against a real temp-file SQLite store (never
/// <c>:memory:</c>, for the same WAL reason <c>PredictionCalibrationStoreTests</c>
/// records). No volume and no mock: the constructor that takes an explicit path
/// is what keeps every case here offline.
/// </summary>
public sealed class LearningReportTests : IDisposable
{
    private readonly string _databasePath;

    public LearningReportTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"nosai-learning-report-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    private PredictionCalibrationStore Open() => new(_databasePath, Options());

    private static SqliteJournalOptions Options() => new(FileName: "irrelevant.db");

    private static CalibrationSnapshotEntry Entry(
        string context, double alpha, double beta, int totalTrials,
        int confirmed, int refuted, int ignored, double errorSum) =>
        new(context, alpha, beta, totalTrials, confirmed, refuted, ignored, errorSum);

    private void SeedTwoContexts()
    {
        using PredictionCalibrationStore store = Open();
        store.Save(CalibrationSnapshot.From(new[]
        {
            Entry("buono", alpha: 10.0, beta: 1.0, totalTrials: 9, confirmed: 9, refuted: 0, ignored: 0, errorSum: 1.0),
            Entry("pessimo", alpha: 1.0, beta: 10.0, totalTrials: 9, confirmed: 0, refuted: 9, ignored: 1, errorSum: 90.0),
        }));
    }

    // -- ordering -------------------------------------------------------------

    [Fact]
    public void Summarize_OrdersWorstCalibratedFirst()
    {
        SeedTwoContexts();
        using var store = Open();

        IReadOnlyList<LearningContextSummary> summaries = LearningReportCommand.Summarize(store);

        Assert.Equal(2, summaries.Count);
        Assert.Equal("pessimo", summaries[0].ContextKey); // lower accuracy first
        Assert.Equal("buono", summaries[1].ContextKey);
    }

    // -- the table carries Trials on every row ---------------------------------

    [Fact]
    public void WriteReport_SummaryMode_RendersAccuracyNeverWithoutTrials()
    {
        SeedTwoContexts();
        using var store = Open();

        var output = new StringWriter();
        LearningReportCommand.WriteReport(store, context: null, output);

        string text = output.ToString();
        Assert.Contains("=== prediction calibration ===", text, StringComparison.Ordinal);
        // Accuracy (0.091 / 0.909) is never printed without its trials=9.
        Assert.Contains("context pessimo: accuracy=0.091 trials=9 confirmed=0 refuted=9 ignored=1", text, StringComparison.Ordinal);
        Assert.Contains("context buono: accuracy=0.909 trials=9 confirmed=9 refuted=0 ignored=0", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatSummary_AlwaysCarriesTrials()
    {
        var summary = new LearningContextSummary("ctx", ExpectedAccuracy: 1.0, Trials: 2, Confirmed: 2, Refuted: 0, Ignored: 0, MeanAbsoluteError: 0);

        string line = LearningReportCommand.FormatSummary(summary);

        Assert.Contains("accuracy=1.000", line, StringComparison.Ordinal);
        Assert.Contains("trials=2", line, StringComparison.Ordinal);
    }

    // -- context selection ----------------------------------------------------

    [Fact]
    public void HasContext_IsFalseForANeverSavedContext_AndTrueForOneWithCalibration()
    {
        SeedTwoContexts();
        using var store = Open();

        Assert.True(LearningReportCommand.HasContext(store, "buono"));
        Assert.False(LearningReportCommand.HasContext(store, "mai-visto"));
    }

    [Fact]
    public void WriteReport_ContextMode_ForAnAbsentContext_SaysNotRecordedNeverAnEmptyTable()
    {
        SeedTwoContexts();
        using var store = Open();

        var output = new StringWriter();
        LearningReportCommand.WriteReport(store, "mai-visto", output);

        string text = output.ToString();
        Assert.Contains("not recorded", text, StringComparison.Ordinal);
        Assert.DoesNotContain("accuracy=", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteReport_ContextMode_ForAnExistingContext_RendersOnlyThatContext()
    {
        SeedTwoContexts();
        using var store = Open();

        var output = new StringWriter();
        LearningReportCommand.WriteReport(store, "pessimo", output);

        string text = output.ToString();
        Assert.Contains("=== context pessimo ===", text, StringComparison.Ordinal);
        Assert.DoesNotContain("buono", text, StringComparison.Ordinal);
    }

    // -- refusals / parsing ---------------------------------------------------

    [Theory]
    [InlineData("--context")]
    [InlineData("--context", "--other-flag")]
    public void TryParse_ContextWithoutValue_IsRefused(string first, string? second = null)
    {
        string[] args = second is null ? new[] { first } : new[] { first, second };

        string? refusal = LearningReportCommand.TryParse(args, out string? context);

        Assert.Equal(LearningReportCommand.ContextWithoutValueReason, refusal);
        Assert.Null(context);
    }

    [Fact]
    public void TryParse_ContextWithValue_ParsesTheValue()
    {
        string? refusal = LearningReportCommand.TryParse(
            new[] { "--learning-report", "--context", "skill:201" }, out string? context);

        Assert.Null(refusal);
        Assert.Equal("skill:201", context);
    }

    [Fact]
    public void TryOpenFromVolume_ForANonexistentVolume_ReturnsNullWithAReason()
    {
        // The exact mechanism Run relies on to name the volume-absent refusal: a
        // label with no attached drive yields null and a non-empty reason,
        // deterministically, on every machine (no real NOSAI-SSD required).
        using PredictionCalibrationStore? store = PredictionCalibrationStore.TryOpenFromVolume(
            new SqliteJournalOptions(VolumeLabel: "NOSAI-VOLUME-DOES-NOT-EXIST-9F3E"), out string? failureReason);

        Assert.Null(store);
        Assert.False(string.IsNullOrWhiteSpace(failureReason));
        // And the reason Run reports for it is the named constant, never a bare
        // boolean: the operator sees "learning_calibration_unavailable", not a
        // silent null.
        Assert.Equal("learning_calibration_unavailable", LearningReportCommand.CalibrationUnavailableReason);
    }

    // -- the absent-file trap -------------------------------------------------

    [Fact]
    public void AnAbsentCalibrationIsReportedAsNeverCreated_AndTheFileStaysAbsent()
    {
        // La guardia vera, chiamata davvero. Fino al 2026-09-08 questo test
        // costruiva un percorso con un GUID nuovo e asseriva che non esistesse --
        // vero per costruzione -- senza mai invocare il comando: verificava la
        // propria premessa e nient'altro.
        string directory = Path.Combine(Path.GetTempPath(), $"nosai_learning_{Guid.NewGuid():N}");
        string absent = Path.Combine(directory, "nosai.db");
        var output = new StringWriter();

        bool reported = LearningReportCommand.TryReportNeverCreated(absent, output);

        Assert.True(reported);
        Assert.Contains(
            LearningReportCommand.CalibrationNotCreatedReason, output.ToString(), StringComparison.Ordinal);

        // E il file continua a non esistere: e' l'intera ragione della guardia.
        Assert.False(File.Exists(absent));
        Assert.False(Directory.Exists(directory));

        // Il motivo e' quello dell'archivio mai creato, non quello dello store vuoto.
        Assert.Equal("calibration_never_created", LearningReportCommand.CalibrationNotCreatedReason);
    }

    [Fact]
    public void AnEmptyStoreAndAnAbsentCalibrationDoNotShareAMessage()
    {
        using var store = Open();
        var output = new StringWriter();
        LearningReportCommand.WriteReport(store, context: null, output);

        // Store opened and empty.
        Assert.Contains("no contexts recorded", output.ToString(), StringComparison.Ordinal);

        // Store never created: a different phrase, and named.
        Assert.Equal("calibration_never_created", LearningReportCommand.CalibrationNotCreatedReason);
        Assert.DoesNotContain(
            LearningReportCommand.CalibrationNotCreatedReason, output.ToString(), StringComparison.Ordinal);
    }

    // -- wiring ---------------------------------------------------------------

    [Fact]
    public void TheRuntimeWiresTheLearningReportFlag()
    {
        string program = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "NosAi.Runtime", "Program.cs"));
        Assert.Contains("LearningReportCommand.Run", program, StringComparison.Ordinal);
        Assert.Contains("LearningReportCommand.Flag", program, StringComparison.Ordinal);
        Assert.Contains("\"--learning-report\"", program, StringComparison.Ordinal);
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
