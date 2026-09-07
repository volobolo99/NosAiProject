using Microsoft.Data.Sqlite;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Learning;
using NosAi.Storage;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// <see cref="PredictionLedger"/>'s export/import pair: the two pure methods
/// that let the resolved calibration survive the process instead of dying with
/// the in-memory dictionaries.
/// </summary>
public sealed class CalibrationPersistenceTests
{
    private static Observation Live(Prediction p, double actual) =>
        new(p.Id, actual, DataSourceKind.Live, DateTime.UtcNow);

    private static Observation With(Prediction p, double actual, DataSourceKind source) =>
        new(p.Id, actual, source, DateTime.UtcNow);

    // -- the round trip -------------------------------------------------------

    [Fact]
    public void ExportThenImportIntoAFreshLedger_ReproducesTheSameCalibration()
    {
        var source = new PredictionLedger();
        for (int i = 0; i < 6; i++)
        {
            source.Record(Live(source.Predict("fuoco", "danno", 100, 1), 300));
            source.Record(Live(source.Predict("acqua", "danno", 100, 1), 100));
        }
        // A counted-but-ignored outcome must survive too.
        source.Record(With(source.Predict("fuoco", "danno", 100, 1), 300, DataSourceKind.Simulated));

        var target = new PredictionLedger();
        target.ImportCalibration(source.ExportCalibration());

        foreach (string context in new[] { "fuoco", "acqua" })
        {
            Calibration expected = source.CalibrationOf(context)!;
            Calibration actual = target.CalibrationOf(context)!;
            Assert.Equal(expected.ExpectedAccuracy, actual.ExpectedAccuracy);
            Assert.Equal(expected.Trials, actual.Trials);
            Assert.Equal(expected.Confirmed, actual.Confirmed);
            Assert.Equal(expected.Refuted, actual.Refuted);
            Assert.Equal(expected.Ignored, actual.Ignored);
            Assert.Equal(expected.MeanAbsoluteError, actual.MeanAbsoluteError);
        }
    }

    [Fact]
    public void OpenPredictionsDoNotSurviveTheRoundTrip()
    {
        var source = new PredictionLedger();
        source.Predict("aperto", "danno", 100, 10);                        // open, never settled
        source.Record(Live(source.Predict("chiuso", "danno", 100, 10), 100)); // settled

        Assert.Equal(1, source.OpenPredictions);

        var target = new PredictionLedger();
        target.ImportCalibration(source.ExportCalibration());

        Assert.Equal(0, target.OpenPredictions);
        Assert.NotNull(target.CalibrationOf("chiuso"));
        Assert.Null(target.CalibrationOf("aperto"));
    }

    // -- the replace-vs-sum decision ------------------------------------------

    [Fact]
    public void ImportingTheSameSnapshotTwice_DoesNotDoubleTheCounts()
    {
        var source = new PredictionLedger();
        for (int i = 0; i < 5; i++)
            source.Record(Live(source.Predict("ctx", "danno", 100, 1), 100));

        CalibrationSnapshot snapshot = source.ExportCalibration();

        var target = new PredictionLedger();
        target.ImportCalibration(snapshot);
        target.ImportCalibration(snapshot);

        Calibration calibration = target.CalibrationOf("ctx")!;
        Assert.Equal(5, calibration.Trials);
        Assert.Equal(5, calibration.Confirmed);
    }

    // -- the honest rule, through persistence --------------------------------

    [Fact]
    public void ANonLiveOutcomeStaysCountedAndDoesNotMoveTheEvidence_EvenAfterSaveAndReload()
    {
        var ledger = new PredictionLedger();
        ledger.Record(Live(ledger.Predict("ctx", "danno", 100, 1), 100)); // one learnable trial
        ledger.Record(With(ledger.Predict("ctx", "danno", 100, 1), 999, DataSourceKind.Simulated)); // counted, ignored

        string path = Path.Combine(Path.GetTempPath(), $"nosai-calib-persist-{Guid.NewGuid():N}.db");
        try
        {
            var options = new SqliteJournalOptions();
            using (var store = new PredictionCalibrationStore(path, options))
                store.Save(ledger.ExportCalibration());

            CalibrationSnapshot reloaded;
            using (var store = new PredictionCalibrationStore(path, options))
                reloaded = store.Load();

            var target = new PredictionLedger();
            target.ImportCalibration(reloaded);

            Calibration calibration = target.CalibrationOf("ctx")!;
            Assert.Equal(1, calibration.Trials);      // the non-Live outcome never became a trial
            Assert.Equal(1, calibration.Confirmed);
            Assert.Equal(1, calibration.Ignored);     // ...but it is still counted
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
