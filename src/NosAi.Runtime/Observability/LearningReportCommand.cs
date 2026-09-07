using System.Globalization;
using NosAi.Runtime.Gate4;
using NosAi.Runtime.Learning;
using NosAi.Storage;

namespace NosAi.Runtime.Observability;

/// <summary>One context's persisted calibration, as the report prints it.</summary>
/// <param name="ContextKey">The context key.</param>
/// <param name="ExpectedAccuracy">The believed probability the next prediction here holds.</param>
/// <param name="Trials">How much evidence that belief stands on.</param>
/// <param name="Confirmed">Live outcomes within tolerance.</param>
/// <param name="Refuted">Live outcomes outside tolerance.</param>
/// <param name="Ignored">Outcomes seen but not learnable (not Live).</param>
/// <param name="MeanAbsoluteError">Mean absolute error over learnable outcomes.</param>
public readonly record struct LearningContextSummary(
    string ContextKey,
    double ExpectedAccuracy,
    int Trials,
    int Confirmed,
    int Refuted,
    int Ignored,
    double MeanAbsoluteError);

/// <summary>
/// Reads the durable prediction-calibration store back and reports it to an
/// operator (CLI <c>--learning-report</c>). Read-only: it never saves, migrates
/// or feeds the planner, ranker, Guard, Safety or <c>PredictionLedger</c>.
/// </summary>
/// <remarks>
/// <para>
/// The calibration is written by nothing yet: <c>PredictionCalibrationStore</c>
/// is the persistence for <c>PredictionLedger</c>, and its save/restore wiring is
/// a host lifecycle decision that this task left to a later integration step (the
/// candidate shutdown point was not a shutdown point at all). This command is the
/// read path, and it stays a read path.
/// </para>
/// <para>
/// Accuracy is never printed alone. A rate of 100% resting on two trials and a
/// rate of 100% resting on two hundred are different claims, and a table that
/// shows them equal will be believed equal. <c>Trials</c> is printed on every row
/// for exactly this reason, in the worst-calibrated-first order
/// <c>PredictionLedger.Weakest</c> already uses.
/// </para>
/// </remarks>
public static class LearningReportCommand
{
    /// <summary>The operator flag.</summary>
    public const string Flag = "--learning-report";

    /// <summary>Selects one context's calibration.</summary>
    public const string ContextOption = "--context";

    /// <summary>Refused when the labeled volume (hence the store) is absent.</summary>
    public const string CalibrationUnavailableReason = "learning_calibration_unavailable";

    /// <summary>Refused when <c>--context</c> carries no value.</summary>
    public const string ContextWithoutValueReason = "context_without_value";

    /// <summary>Refused when <c>--context</c> names a context with no calibration.</summary>
    public const string ContextNotFoundReason = "context_not_found";

    /// <summary>
    /// The volume is there and the calibration has never been written. Not a
    /// refusal: it is an answer, and the most likely one on a machine where no
    /// cycle has ever saved a calibration.
    /// </summary>
    public const string CalibrationNotCreatedReason = "calibration_never_created";

    /// <summary>Exit code for a named refusal. Matches the other diagnostics.</summary>
    public const int ExitRefused = 2;

    /// <summary>
    /// One <see cref="LearningContextSummary"/> per stored context, worst
    /// calibrated first (accuracy ascending, then most evidence first) -- the
    /// same ordering <c>PredictionLedger.Weakest</c> uses and for the same reason:
    /// the useful question is not where the model is right but where it keeps
    /// being wrong.
    /// </summary>
    public static IReadOnlyList<LearningContextSummary> Summarize(PredictionCalibrationStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        CalibrationSnapshot snapshot = store.Load();
        return snapshot.Entries
            .Select(ToCalibration)
            .OrderBy(c => c.ExpectedAccuracy)
            .ThenByDescending(c => c.Trials)
            .Select(c => new LearningContextSummary(
                c.ContextKey, c.ExpectedAccuracy, c.Trials,
                c.Confirmed, c.Refuted, c.Ignored, c.MeanAbsoluteError))
            .ToArray();
    }

    /// <summary>Whether <paramref name="context"/> has a stored calibration.</summary>
    public static bool HasContext(PredictionCalibrationStore store, string context)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(context);
        foreach (CalibrationSnapshotEntry entry in store.Load().Entries)
            if (string.Equals(entry.ContextKey, context, StringComparison.Ordinal))
                return true;
        return false;
    }

    /// <summary>
    /// Writes the whole report: the per-context summary without
    /// <paramref name="context"/>, or that context's single row with it.
    /// </summary>
    public static void WriteReport(PredictionCalibrationStore store, string? context, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(output);

        IReadOnlyList<LearningContextSummary> summaries = Summarize(store);

        if (context is null)
        {
            output.WriteLine("=== prediction calibration ===");
            if (summaries.Count == 0)
            {
                output.WriteLine("no contexts recorded");
                return;
            }

            foreach (LearningContextSummary summary in summaries)
                output.WriteLine(FormatSummary(summary));
            return;
        }

        foreach (LearningContextSummary summary in summaries)
        {
            if (!string.Equals(summary.ContextKey, context, StringComparison.Ordinal))
                continue;
            output.WriteLine($"=== context {context} ===");
            output.WriteLine(FormatSummary(summary));
            return;
        }

        // Defensive: Run refuses an absent context before reaching here, but a
        // caller of WriteReport directly must still get an honest answer, never
        // an empty table pretending the context exists.
        output.WriteLine($"=== context {context}: not recorded ===");
    }

    /// <summary>Console entry. Validates, opens the store from the labeled volume, and reports.</summary>
    public static int Run(string[] args)
    {
        string? refusal = TryParse(args, out string? context);
        if (refusal is not null)
        {
            Console.WriteLine($"[REFUSED] {refusal}");
            Console.WriteLine($"Usage: {Flag} [--context <name>]");
            return ExitRefused;
        }

        // The file is looked at before it is opened, and that is not pedantry:
        // opening a SQLite store creates it. Without this check a read-only
        // command would write on the operator's volume and -- worse -- its first
        // run would turn "the calibration was never written" into "the store
        // exists and is empty", erasing the distinction this report exists to
        // make (the same trap OutcomeReportCommand documents and avoids).
        var options = new SqliteJournalOptions();
        if (!VolumeLocator.TryResolveDatabasePath(options, out string databasePath, out string? pathReason))
        {
            Console.WriteLine($"[REFUSED] {CalibrationUnavailableReason}:{pathReason}");
            return ExitRefused;
        }

        if (!File.Exists(databasePath))
        {
            // An answer, not a refusal: the volume responds, and what it says is
            // that no cycle has ever saved a calibration.
            Console.WriteLine("=== prediction calibration ===");
            Console.WriteLine($"{CalibrationNotCreatedReason}: {databasePath}");
            return 0;
        }

        using PredictionCalibrationStore? store =
            PredictionCalibrationStore.TryOpenFromVolume(options, out string? failureReason);
        if (store is null)
        {
            Console.WriteLine($"[REFUSED] {CalibrationUnavailableReason}:{failureReason}");
            return ExitRefused;
        }

        if (context is not null && !HasContext(store, context))
        {
            Console.WriteLine($"[REFUSED] {ContextNotFoundReason}:{context}");
            return ExitRefused;
        }

        WriteReport(store, context, Console.Out);
        return 0;
    }

    /// <summary>
    /// Validates the argument vector against the flag grammar. Returns the named
    /// refusal reason, or null when well-formed (with the parsed context in the
    /// out parameter).
    /// </summary>
    public static string? TryParse(string[] args, out string? context)
    {
        context = null;
        int contextFlag = Array.FindIndex(args, a => string.Equals(a, ContextOption, StringComparison.OrdinalIgnoreCase));
        if (contextFlag >= 0)
        {
            if (contextFlag + 1 >= args.Length || args[contextFlag + 1].StartsWith("--", StringComparison.Ordinal))
                return ContextWithoutValueReason;
            context = args[contextFlag + 1];
        }
        return null;
    }

    /// <summary>One summary as a single stable line, accuracy never without its trials.</summary>
    public static string FormatSummary(LearningContextSummary summary) =>
        string.Create(CultureInfo.InvariantCulture,
            $"context {summary.ContextKey}: accuracy={summary.ExpectedAccuracy:F3} trials={summary.Trials} "
            + $"confirmed={summary.Confirmed} refuted={summary.Refuted} ignored={summary.Ignored} "
            + $"mae={summary.MeanAbsoluteError:F3}");

    // Rebuilds the runtime's own Calibration view from the persisted raw fields,
    // so the report's accuracy and mean-absolute-error come from the same
    // Beta-Binomial math PredictionLedger.CalibrationOf already uses.
    private static Calibration ToCalibration(CalibrationSnapshotEntry entry)
    {
        var evidence = new BetaBinomialEvidence(entry.Alpha, entry.Beta, entry.TotalTrials);
        int learned = entry.Confirmed + entry.Refuted;
        double mae = learned == 0 ? 0 : entry.ErrorSum / learned;
        return new Calibration(
            entry.ContextKey, evidence, entry.Confirmed, entry.Refuted, entry.Ignored, mae);
    }
}
