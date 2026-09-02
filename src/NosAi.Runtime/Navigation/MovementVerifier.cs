// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Navigation — Did the step actually happen? (C-P4)
// ============================================================================
//
// DOMAIN-11: sending an input is not evidence that it worked. The card
// (docs/CONTROLLO_PERSONAGGIO_ATTUAZIONE.md § 5) gives movement an expected
// delta, a 350 ms window and a ±20 ms tolerance, and this is where those become
// an answer rather than a table row.

using System.Diagnostics;
using System.Globalization;
using System.Threading;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Navigation;

/// <summary>What became of one step.</summary>
/// <remarks>
/// <para>
/// The roadmap names three — success, stall, abort. Two more are here because
/// folding them into those three would make the runtime claim something it did not
/// observe, which is the failure mode this project treats as worse than the failure
/// itself.
/// </para>
/// <para>
/// <b><see cref="Unobserved"/> is not a stall.</b> A stall says the character was
/// watched and did not move. If no reading arrives that postdates the act, nothing
/// was watched, and calling that a stall would report an observation that was never
/// made — and would feed the recovery breaker a failure with no evidence under it.
/// Both are failures; only one of them is a statement about the character.
/// </para>
/// <para>
/// <b><see cref="Displaced"/> is not a success.</b> Arriving somewhere is not
/// arriving <i>there</i>. The defect that made Gate 3 unable to tell the truth was a
/// <c>Completed</c> with no execution behind it (<c>docs/GATE3_PIPELINE.md</c>), and
/// a verifier that accepted any movement would be the same defect one layer up.
/// </para>
/// </remarks>
public enum MovementOutcome : byte
{
    /// <summary>A reading taken after the act put the character on the destination.</summary>
    Succeeded = 0,

    /// <summary>Readings arrived, and the character was still on the origin when the window closed.</summary>
    Stalled = 1,

    /// <summary>The character moved, and not to the cell that was asked for.</summary>
    Displaced = 2,

    /// <summary>No reading postdating the act arrived before the window closed.</summary>
    Unobserved = 3,

    /// <summary>Nothing was emitted: a guard refused, or the act was abandoned.</summary>
    Aborted = 4
}

/// <summary>Where the character was, when that was seen, and how.</summary>
/// <param name="Source">
/// <see cref="DataSourceKind.Simulated"/> is refused as testimony: a simulated
/// position can be planned on and can never confirm that something real happened.
/// </param>
public readonly record struct PositionReading(
    MapPoint At,
    DateTime ObservedAtUtc,
    DataSourceKind Source);

/// <summary>The verdict on one step, with the measurement behind it.</summary>
/// <param name="Outcome">What became of the step.</param>
/// <param name="Detail">The named reason or the observed cell. Null only for a plain success.</param>
/// <param name="Observed">The last accepted reading's cell, or null when none was accepted.</param>
/// <param name="Elapsed">
/// How long the verification took, on the monotonic clock. Reported for every outcome:
/// a window that is always closing at its limit is a different problem from one that
/// resolves in 40 ms, and only the number distinguishes them.
/// </param>
/// <param name="ReadingsAccepted">How many readings postdating the act were considered.</param>
public readonly record struct MovementVerification(
    MovementOutcome Outcome,
    string? Detail,
    MapPoint? Observed,
    TimeSpan Elapsed,
    int ReadingsAccepted)
{
    /// <summary>True only for an arrival on the cell that was asked for.</summary>
    public bool Succeeded => Outcome == MovementOutcome.Succeeded;

    /// <summary>The step was never emitted, and why.</summary>
    public static MovementVerification NotAttempted(string reason) =>
        new(MovementOutcome.Aborted, reason, null, TimeSpan.Zero, 0);
}

/// <summary>
/// Watches the observed grid position and says whether the step happened.
/// </summary>
/// <remarks>
/// <para>
/// <b>Grid against grid, and nothing else.</b> Not a pixel comparison, not the fact
/// that <c>SendInput</c> returned true. The character's square before, the
/// character's square after, from the same source that the planner reasoned over.
/// </para>
/// <para>
/// <b>The reading has to postdate the act, and this is the load-bearing rule.</b>
/// A world feed republishes what it last knew; a position stamped before the click
/// is the position the character had before the click, and comparing it would
/// "verify" the act against the state it was supposed to change. It would read as a
/// stall every time — or, when the character happened to already be adjacent, as a
/// success nobody caused. So a reading counts only if its observation instant is
/// strictly after the emission instant.
/// </para>
/// <para>
/// <b>How the window is read.</b> 350 ms with ±20 ms of tolerance means the arrival
/// must be observed by 370 ms; it does not mean an arrival at 200 ms is early and
/// therefore suspect. There is no lower bound on a step being fast, and a two-sided
/// window would fail exactly the steps that worked best.
/// </para>
/// <para>
/// <b>Monotonic.</b> The window is measured with <see cref="Stopwatch"/>, per the
/// standing constraint: a wall clock that steps sideways mid-step would end the
/// window early or never.
/// </para>
/// </remarks>
public sealed class MovementVerifier
{
    /// <summary>The card's window for a movement (§ 5).</summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMilliseconds(350);

    /// <summary>The card's tolerance, applied on the late side only.</summary>
    public static readonly TimeSpan DefaultTolerance = TimeSpan.FromMilliseconds(20);

    /// <summary>How often the position is re-read while the window is open.</summary>
    /// <remarks>
    /// Thirty-five samples across the window. Fine enough that the first arrival is
    /// seen within a frame of it happening, coarse enough that the loop is not itself
    /// what keeps the thread busy while the client is trying to move a character.
    /// </remarks>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(10);

    /// <summary>Reported when readings arrived and none of them postdated the act.</summary>
    public const string NoFreshReadingReason = "movement_no_reading_after_act";

    /// <summary>Reported when the only readings available were simulated.</summary>
    public const string SimulatedReadingReason = "movement_reading_is_simulated";

    /// <summary>Reported when the window closed with the character still on the origin.</summary>
    public const string StalledReason = "movement_did_not_leave_origin";

    private readonly TimeSpan _window;
    private readonly TimeSpan _tolerance;
    private readonly TimeSpan _pollInterval;

    public MovementVerifier(
        TimeSpan? window = null,
        TimeSpan? tolerance = null,
        TimeSpan? pollInterval = null)
    {
        _window = window ?? DefaultWindow;
        _tolerance = tolerance ?? DefaultTolerance;
        _pollInterval = pollInterval ?? DefaultPollInterval;

        ArgumentOutOfRangeException.ThrowIfNegative(_window.Ticks, nameof(window));
        ArgumentOutOfRangeException.ThrowIfNegative(_tolerance.Ticks, nameof(tolerance));
        ArgumentOutOfRangeException.ThrowIfNegative(_pollInterval.Ticks, nameof(pollInterval));
    }

    /// <summary>The declared window.</summary>
    public TimeSpan Window => _window;

    /// <summary>The slack allowed past the window before an arrival is too late.</summary>
    public TimeSpan Tolerance => _tolerance;

    /// <summary>The instant past which an arrival no longer counts.</summary>
    public TimeSpan Deadline => _window + _tolerance;

    /// <summary>
    /// Watches until the character arrives, is seen not to have moved, or the window closes.
    /// </summary>
    /// <param name="from">The square the character was on when the act was authorised.</param>
    /// <param name="to">The square the act was aimed at.</param>
    /// <param name="emittedAtUtc">
    /// When the irreversible step left. Every reading is judged against this: earlier
    /// is the world before the act and testifies to nothing.
    /// </param>
    /// <param name="readPosition">
    /// The current observed position, or null when there is none right now. Called
    /// repeatedly; it must not block.
    /// </param>
    /// <param name="cancellationToken">Ends the watch early; the outcome is then whatever was observed.</param>
    public MovementVerification Verify(
        MapPoint from,
        MapPoint to,
        DateTime emittedAtUtc,
        Func<PositionReading?> readPosition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(readPosition);

        long started = Stopwatch.GetTimestamp();
        var accepted = 0;
        var sawSimulated = false;
        MapPoint? lastSeen = null;

        while (true)
        {
            PositionReading? current = readPosition();

            if (current is { } reading)
            {
                if (reading.Source == DataSourceKind.Simulated)
                {
                    sawSimulated = true;
                }
                else if (reading.Source != DataSourceKind.Unknown && reading.ObservedAtUtc > emittedAtUtc)
                {
                    accepted++;
                    lastSeen = reading.At;

                    if (reading.At == to)
                        return Result(MovementOutcome.Succeeded, null, reading.At, started, accepted);

                    // Anywhere that is neither where it was nor where it was sent: it
                    // moved, and not as asked. Reported at once, because a second
                    // reading cannot make the first one mean something else.
                    if (reading.At != from)
                    {
                        return Result(
                            MovementOutcome.Displaced,
                            string.Create(CultureInfo.InvariantCulture,
                                $"movement_landed_elsewhere:{reading.At.X},{reading.At.Y}_not_{to.X},{to.Y}"),
                            reading.At,
                            started,
                            accepted);
                    }
                }
            }

            TimeSpan elapsed = Stopwatch.GetElapsedTime(started);
            if (elapsed >= Deadline || cancellationToken.IsCancellationRequested)
                break;

            // Never sleep past the deadline: a poll interval longer than what is left
            // would turn a window into a slightly larger window.
            TimeSpan remaining = Deadline - elapsed;
            Thread.Sleep(remaining < _pollInterval ? remaining : _pollInterval);
        }

        if (accepted > 0)
            return Result(MovementOutcome.Stalled, StalledReason, lastSeen, started, accepted);

        return Result(
            MovementOutcome.Unobserved,
            sawSimulated ? SimulatedReadingReason : NoFreshReadingReason,
            null,
            started,
            accepted);
    }

    private static MovementVerification Result(
        MovementOutcome outcome,
        string? detail,
        MapPoint? observed,
        long started,
        int accepted) =>
        new(outcome, detail, observed, Stopwatch.GetElapsedTime(started), accepted);
}
