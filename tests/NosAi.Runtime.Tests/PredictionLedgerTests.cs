using NosAi.Runtime.Contracts;
using NosAi.Runtime.Learning;
using Xunit;
using Xunit.Abstractions;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The loop that lets the runtime find out it was wrong.
/// </summary>
/// <remarks>
/// The test that matters most here is the negative one: a simulated outcome must
/// never move a belief. A system allowed to learn from its own predictions
/// converges on its own fantasy, and from the inside that is indistinguishable
/// from getting very good very quickly.
/// </remarks>
public sealed class PredictionLedgerTests
{
    private readonly ITestOutputHelper _output;

    public PredictionLedgerTests(ITestOutputHelper output) => _output = output;

    private static Observation Live(Prediction p, double actual) =>
        new(p.Id, actual, DataSourceKind.Live, DateTime.UtcNow);

    // -------------------------------------------------------- the honest rule

    [Fact]
    public void ASimulatedOutcomeIsCountedButNeverLearnedFrom()
    {
        var ledger = new PredictionLedger();
        Prediction p = ledger.Predict("attacco:volpe", "danno", 100, 10);

        LearningOutcome outcome = ledger.Record(
            new Observation(p.Id, 100, DataSourceKind.Simulated, DateTime.UtcNow));

        Assert.Equal(LearningOutcome.NotLearnable, outcome);
        Calibration? calibration = ledger.CalibrationOf("attacco:volpe");
        Assert.NotNull(calibration);
        Assert.Equal(0, calibration!.Trials);
        Assert.Equal(1, calibration.Ignored);
    }

    [Theory]
    [InlineData(DataSourceKind.Simulated)]
    [InlineData(DataSourceKind.Cached)]
    [InlineData(DataSourceKind.Derived)]
    [InlineData(DataSourceKind.Unknown)]
    public void OnlyALiveOutcomeCanMoveABelief(DataSourceKind source)
    {
        var ledger = new PredictionLedger();

        for (int i = 0; i < 20; i++)
        {
            Prediction p = ledger.Predict("ctx", "danno", 100, 1);
            ledger.Record(new Observation(p.Id, 100, source, DateTime.UtcNow));
        }

        Assert.Equal(0, ledger.CalibrationOf("ctx")!.Trials);
    }

    [Fact]
    public void ALiveOutcomeWithinToleranceConfirms()
    {
        var ledger = new PredictionLedger();
        Prediction p = ledger.Predict("attacco:volpe", "danno", 100, 10);

        Assert.Equal(LearningOutcome.Confirmed, ledger.Record(Live(p, 106)));
        Assert.Equal(1, ledger.CalibrationOf("attacco:volpe")!.Confirmed);
    }

    [Fact]
    public void ALiveOutcomeOutsideToleranceRefutes()
    {
        var ledger = new PredictionLedger();
        Prediction p = ledger.Predict("attacco:volpe", "danno", 100, 10);

        Assert.Equal(LearningOutcome.Refuted, ledger.Record(Live(p, 140)));
        Assert.Equal(1, ledger.CalibrationOf("attacco:volpe")!.Refuted);
    }

    // ------------------------------------------------------------- learning

    [Fact]
    public void RepeatedMistakesPushTheBeliefDown()
    {
        // This is the whole point: being wrong has to change something.
        var ledger = new PredictionLedger();

        for (int i = 0; i < 10; i++)
        {
            Prediction p = ledger.Predict("colpo:critico", "danno", 500, 5);
            ledger.Record(Live(p, 120));
        }

        Calibration calibration = ledger.CalibrationOf("colpo:critico")!;
        Evidence.Live(_output, "accuratezzaAttesa", $"{calibration.ExpectedAccuracy:F3}");
        Evidence.Live(_output, "prove", calibration.Trials);
        Evidence.Live(_output, "erroreMedioAssoluto", calibration.MeanAbsoluteError);

        Assert.Equal(10, calibration.Refuted);
        Assert.True(calibration.ExpectedAccuracy < 0.2,
            $"atteso un crollo della fiducia, ottenuto {calibration.ExpectedAccuracy:F3}");
        Assert.Equal(380, calibration.MeanAbsoluteError);
    }

    [Fact]
    public void BeingRightRepeatedlyRaisesTheBelief()
    {
        var ledger = new PredictionLedger();

        for (int i = 0; i < 10; i++)
        {
            Prediction p = ledger.Predict("cura", "hp", 50, 5);
            ledger.Record(Live(p, 52));
        }

        Assert.True(ledger.CalibrationOf("cura")!.ExpectedAccuracy > 0.8);
    }

    [Fact]
    public void ContextsAreLearnedSeparately()
    {
        // Being wrong about fire monsters must not lower the confidence about water
        // ones; a single global accuracy would hide exactly where the model fails.
        var ledger = new PredictionLedger();

        for (int i = 0; i < 6; i++)
        {
            ledger.Record(Live(ledger.Predict("fuoco", "danno", 100, 1), 300));
            ledger.Record(Live(ledger.Predict("acqua", "danno", 100, 1), 100));
        }

        Assert.True(ledger.CalibrationOf("fuoco")!.ExpectedAccuracy < 0.3);
        Assert.True(ledger.CalibrationOf("acqua")!.ExpectedAccuracy > 0.7);
    }

    [Fact]
    public void TheWeakestContextsComeFirst()
    {
        var ledger = new PredictionLedger();
        for (int i = 0; i < 5; i++)
        {
            ledger.Record(Live(ledger.Predict("buono", "x", 10, 1), 10));
            ledger.Record(Live(ledger.Predict("pessimo", "x", 10, 1), 99));
        }

        IReadOnlyList<Calibration> weakest = ledger.Weakest();

        Evidence.Live(_output, "peggiore", weakest[0].ContextKey);
        Assert.Equal("pessimo", weakest[0].ContextKey);
    }

    // ------------------------------------------------------------- refusals

    [Fact]
    public void NoBeliefIsReportedWhereNothingWasObserved()
    {
        // "We have no evidence" and "we believe it is a coin flip" are different
        // claims, and returning a uniform prior would state the second.
        var ledger = new PredictionLedger();
        ledger.Predict("mai-osservato", "danno", 100, 10);

        Assert.Null(ledger.CalibrationOf("mai-osservato"));
    }

    [Fact]
    public void AnOutcomeForAPredictionNeverMadeIsRefused()
    {
        var ledger = new PredictionLedger();

        LearningOutcome outcome = ledger.Record(
            new Observation(Guid.NewGuid(), 1, DataSourceKind.Live, DateTime.UtcNow));

        Assert.Equal(LearningOutcome.Unmatched, outcome);
    }

    [Fact]
    public void APredictionIsSettledOnlyOnce()
    {
        // Otherwise one action could be scored twice and count double.
        var ledger = new PredictionLedger();
        Prediction p = ledger.Predict("ctx", "danno", 100, 10);

        Assert.Equal(LearningOutcome.Confirmed, ledger.Record(Live(p, 100)));
        Assert.Equal(LearningOutcome.Unmatched, ledger.Record(Live(p, 100)));
        Assert.Equal(1, ledger.CalibrationOf("ctx")!.Trials);
    }

    [Fact]
    public void AnUnsettledPredictionIsAbandonedNotScored()
    {
        var ledger = new PredictionLedger();
        ledger.Predict("ctx", "danno", 100, 10, DateTime.UtcNow.AddHours(-2));

        int expired = ledger.Expire(TimeSpan.FromHours(1));

        Assert.Equal(1, expired);
        Assert.Equal(0, ledger.OpenPredictions);
        Assert.Null(ledger.CalibrationOf("ctx"));
    }

    [Fact]
    public void ANegativeToleranceIsRefused()
    {
        var ledger = new PredictionLedger();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ledger.Predict("ctx", "danno", 100, -1));
    }

    [Fact]
    public void ManyThreadsSettlingAtOnceCountEachOutcomeOnce()
    {
        // The ledger is shared by the observing and acting paths, so its counters
        // have to hold under concurrency or the calibration silently drifts.
        var ledger = new PredictionLedger();
        Prediction[] predictions = Enumerable.Range(0, 200)
            .Select(_ => ledger.Predict("parallelo", "danno", 100, 1))
            .ToArray();

        Parallel.ForEach(predictions, p => ledger.Record(Live(p, 100)));

        Assert.Equal(200, ledger.CalibrationOf("parallelo")!.Confirmed);
        Assert.Equal(0, ledger.OpenPredictions);
    }
}
