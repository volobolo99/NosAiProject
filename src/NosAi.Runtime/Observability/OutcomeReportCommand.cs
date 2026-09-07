using System.Globalization;
using NosAi.Core.Memory;
using NosAi.Core.WorldModel;
using NosAi.Storage;

namespace NosAi.Runtime.Observability;

/// <summary>One context's measured history, as the summary prints it.</summary>
/// <param name="Context">The context key.</param>
/// <param name="Rows">How many entries named this context.</param>
/// <param name="Succeeded">Entries whose outcome was observed <see cref="ActionOutcome.Succeeded"/>.</param>
/// <param name="Failed">Entries whose outcome was observed <see cref="ActionOutcome.Failed"/>.</param>
/// <param name="InProgress">Entries whose outcome was observed <see cref="ActionOutcome.InProgress"/>.</param>
/// <param name="Unknown">Entries whose outcome was never observed (<see cref="WorldFact{T}.HasValue"/> false). Never folded into <see cref="Failed"/>.</param>
/// <param name="FirstRecordedAtUtc">Oldest <see cref="ActionOutcomeLedgerEntry.RecordedAtUtc"/>.</param>
/// <param name="LastRecordedAtUtc">Newest <see cref="ActionOutcomeLedgerEntry.RecordedAtUtc"/>.</param>
/// <param name="SourcesByKind">How many entries carry each provenance.</param>
public readonly record struct OutcomeContextSummary(
    string Context,
    int Rows,
    int Succeeded,
    int Failed,
    int InProgress,
    int Unknown,
    DateTime FirstRecordedAtUtc,
    DateTime LastRecordedAtUtc,
    IReadOnlyDictionary<DataSourceKind, int> SourcesByKind);

/// <summary>One ledger row, oldest first, as <c>--context</c> prints it.</summary>
/// <param name="RecordedAtUtc">When the entry was written.</param>
/// <param name="ActionId">Which <see cref="WorldAction"/> the entry is about.</param>
/// <param name="Outcome">The observed outcome, or null when the outcome was never observed.</param>
/// <param name="Source">The outcome's provenance.</param>
public readonly record struct OutcomeContextRow(
    DateTime RecordedAtUtc,
    string ActionId,
    ActionOutcome? Outcome,
    DataSourceKind Source);

/// <summary>
/// Reads the durable action-outcome ledger and reports it to an operator
/// (CLI <c>--outcome-report</c>). Read-only: it never appends, migrates or
/// feeds anything.
/// </summary>
/// <remarks>
/// <para>
/// The ledger is written by <c>--scout</c>, <c>--autoplay</c>, <c>--engage</c>
/// and <c>--recover</c>, and until this command existed nothing could read it
/// back. This command is the missing read path, and it stays a read path:
/// <c>CLAUDE.md</c> § <i>Autonomy requirements</i> keeps prediction advisory,
/// so the numbers here are printed for an operator and never handed to the
/// planner, ranker, Guard, Safety or <c>PredictionLedger</c>.
/// </para>
/// <para>
/// An outcome that was never observed is reported as <c>Unknown</c> and counted
/// apart from <c>Failed</c>: a ledger of guard-refused/unverified acts must not
/// read as a history of measured failures. <i>Unknown is not zero, false or
/// empty.</i>
/// </para>
/// </remarks>
public static class OutcomeReportCommand
{
    /// <summary>The operator flag.</summary>
    public const string Flag = "--outcome-report";

    /// <summary>Selects one context's rows in time order.</summary>
    public const string ContextOption = "--context";

    /// <summary>Refused when the labeled volume (hence the store) is absent.</summary>
    public const string LedgerUnavailableReason = "outcome_report_ledger_unavailable";

    /// <summary>Refused when <c>--context</c> carries no value.</summary>
    public const string ContextWithoutValueReason = "context_without_value";

    /// <summary>Refused when <c>--context</c> names a context with no rows.</summary>
    public const string ContextNotFoundReason = "context_not_found";

    /// <summary>Exit code for a named refusal. Matches the other diagnostics.</summary>
    public const int ExitRefused = 2;

    /// <summary>
    /// Computes one <see cref="OutcomeContextSummary"/> per context that has
    /// rows, in the store's own ordinal order. Pure: reads only
    /// <paramref name="store"/>, touches no clock and no console.
    /// </summary>
    public static IReadOnlyList<OutcomeContextSummary> Summarize(ActionOutcomeLedgerStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        var summaries = new List<OutcomeContextSummary>();
        foreach (string context in store.Contexts())
        {
            EquatableArray<ActionOutcomeLedgerEntry> entries = store.LoadByContext(context);
            int succeeded = 0, failed = 0, inProgress = 0, unknown = 0;
            var sources = new Dictionary<DataSourceKind, int>();
            DateTime first = DateTime.MaxValue, last = DateTime.MinValue;

            foreach (ActionOutcomeLedgerEntry entry in entries)
            {
                if (!entry.Outcome.HasValue)
                    unknown++;
                else switch (entry.Outcome.Value)
                {
                    case ActionOutcome.Succeeded: succeeded++; break;
                    case ActionOutcome.Failed: failed++; break;
                    case ActionOutcome.InProgress: inProgress++; break;
                }

                sources.TryGetValue(entry.Outcome.Source, out int seen);
                sources[entry.Outcome.Source] = seen + 1;

                if (entry.RecordedAtUtc < first)
                    first = entry.RecordedAtUtc;
                if (entry.RecordedAtUtc > last)
                    last = entry.RecordedAtUtc;
            }

            summaries.Add(new OutcomeContextSummary(
                context, entries.Count, succeeded, failed, inProgress, unknown, first, last, sources));
        }

        return summaries;
    }

    /// <summary>
    /// One context's rows, oldest first, with the outcome's observed value
    /// (null when unobserved) and provenance kept distinct.
    /// </summary>
    public static IReadOnlyList<OutcomeContextRow> Rows(ActionOutcomeLedgerStore store, string context)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(context);

        var rows = new List<OutcomeContextRow>();
        foreach (ActionOutcomeLedgerEntry entry in store.LoadByContext(context))
        {
            rows.Add(new OutcomeContextRow(
                entry.RecordedAtUtc,
                entry.ActionId.Value,
                entry.Outcome.HasValue ? entry.Outcome.Value : null,
                entry.Outcome.Source));
        }
        return rows;
    }

    /// <summary>Whether <paramref name="context"/> has at least one recorded row.</summary>
    public static bool HasContext(ActionOutcomeLedgerStore store, string context)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(context);
        foreach (string known in store.Contexts())
            if (string.Equals(known, context, StringComparison.Ordinal))
                return true;
        return false;
    }

    /// <summary>
    /// Writes the whole report: the per-context summary without
    /// <paramref name="context"/>, or that context's rows with it.
    /// </summary>
    public static void WriteReport(ActionOutcomeLedgerStore store, string? context, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(output);

        if (context is null)
        {
            IReadOnlyList<OutcomeContextSummary> summaries = Summarize(store);
            output.WriteLine("=== action-outcome ledger ===");
            if (summaries.Count == 0)
            {
                output.WriteLine("no contexts recorded");
                return;
            }

            foreach (OutcomeContextSummary summary in summaries)
                output.WriteLine(FormatSummary(summary));
            return;
        }

        IReadOnlyList<OutcomeContextRow> rows = Rows(store, context);
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"=== context {context}: {rows.Count} rows ==="));
        foreach (OutcomeContextRow row in rows)
            output.WriteLine(FormatRow(row));
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

        // The same open the four writer commands use: a missing NOSAI-SSD
        // volume returns null with a reason. Here it is a refusal, not a WARN,
        // because the whole point of this command is to read that ledger.
        using ActionOutcomeLedgerStore? store =
            ActionOutcomeLedgerStore.TryOpenFromVolume(new SqliteJournalOptions(), out string? failureReason);
        if (store is null)
        {
            Console.WriteLine($"[REFUSED] {LedgerUnavailableReason}:{failureReason}");
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
    /// Validates the argument vector against the flag grammar. Returns the
    /// named refusal reason, or null when well-formed (with the parsed
    /// context in the out parameter).
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

    /// <summary>One summary as a single stable line.</summary>
    public static string FormatSummary(OutcomeContextSummary summary)
    {
        var sources = new List<string>();
        foreach (KeyValuePair<DataSourceKind, int> pair in summary.SourcesByKind.OrderBy(p => p.Key))
            sources.Add(string.Create(CultureInfo.InvariantCulture, $"{SourceLabel(pair.Key)}={pair.Value}"));
        string sourceText = sources.Count == 0 ? "none" : string.Join(" ", sources);

        return string.Create(CultureInfo.InvariantCulture,
            $"context {summary.Context}: rows={summary.Rows} succeeded={summary.Succeeded} failed={summary.Failed} "
            + $"in_progress={summary.InProgress} unknown={summary.Unknown} "
            + $"first={summary.FirstRecordedAtUtc:O} last={summary.LastRecordedAtUtc:O} sources={sourceText}");
    }

    /// <summary>One row as a single stable line.</summary>
    public static string FormatRow(OutcomeContextRow row)
    {
        string outcome = row.Outcome is { } value ? value.ToString() : "Unknown";
        return string.Create(CultureInfo.InvariantCulture,
            $"{row.RecordedAtUtc:O}  {row.ActionId}  {outcome}  {SourceLabel(row.Source)}");
    }

    private static string SourceLabel(DataSourceKind kind) => kind switch
    {
        DataSourceKind.Live => "Live",
        DataSourceKind.Derived => "Derived",
        DataSourceKind.Cached => "Cached",
        DataSourceKind.Simulated => "Simulated",
        _ => "Unknown",
    };
}
