// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Navigation — Walking a path, and knowing when to stop trying (C2-7)
// ============================================================================
//
// docs/PIANO_CAPACITA.md C2-7, roadmap P5, catalogue § 4.1.
//
// The semantics only. The `--walk` command drives this; it does not decide
// anything this file decides.

using System.Globalization;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Safety;

namespace NosAi.Runtime.Navigation;

/// <summary>What the walk wants to happen next.</summary>
public enum WalkOutcome : byte
{
    /// <summary>Step onto <see cref="WalkDecision.StepTo"/>. The only outcome that emits anything.</summary>
    Stepping = 0,

    /// <summary>The route no longer describes the world. Compute another and offer it.</summary>
    Replan = 1,

    /// <summary>The character is on the destination.</summary>
    Arrived = 2,

    /// <summary>Stop. Named, and not retried.</summary>
    Abandoned = 3
}

/// <summary>One decision of the walk, with the reason behind it.</summary>
/// <param name="Outcome">What to do.</param>
/// <param name="StepTo">The adjacent cell to step onto. Non-null exactly for <see cref="WalkOutcome.Stepping"/>.</param>
/// <param name="Reason">Why, named. Null only for a plain step or arrival.</param>
/// <param name="ReplansUsed">Consecutive replans spent so far, for the operator's report.</param>
public readonly record struct WalkDecision(
    WalkOutcome Outcome,
    MapPoint? StepTo,
    string? Reason,
    int ReplansUsed);

/// <summary>
/// How many times a walk may re-route before it admits it is not getting there.
/// </summary>
/// <param name="MaxConsecutiveReplans">
/// Consecutive means "without progress" — see <see cref="PathWalkController"/> for what
/// counts as progress and why "the cell changed" does not.
/// </param>
/// <param name="MaxConsecutiveUnverifiedSteps">
/// How many acts in a row may complete without the verifier being able to say anything.
/// </param>
public sealed record ReplanPolicy(
    int MaxConsecutiveReplans = ReplanPolicy.DefaultMaxConsecutiveReplans,
    int MaxConsecutiveUnverifiedSteps = ReplanPolicy.DefaultMaxConsecutiveUnverifiedSteps)
{
    /// <summary>
    /// Three, and the argument is about what a fourth could possibly discover.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A replan is worth doing only if it can return a <i>different</i> route, so the
    /// question is how many distinct hypotheses re-routing can work through. There are
    /// three, and they are exhausted in order:
    /// </para>
    /// <list type="number">
    /// <item>
    /// <b>Something is standing in the way and will move.</b> One replan routes around
    /// it, or the next observation finds the cell clear.
    /// </item>
    /// <item>
    /// <b>Something is in the way that the client's grid does not have</b> — a stall with
    /// the geometry saying open ground, which is the case the catalogue's § 4.1 names.
    /// The second replan produces a route that avoids the cell the first one stalled on.
    /// </item>
    /// <item>
    /// <b>There is no other route, or the character is not moving at all.</b> The third
    /// replan returns a route materially the same as the first, and nothing about a
    /// fourth attempt is different from the third.
    /// </item>
    /// </list>
    /// <para>
    /// So the limit is not a tuning constant looking for a better value: past three, the
    /// remaining explanations are all ones re-routing cannot address, and continuing
    /// would be the failure this limit exists to prevent — <b>a system that replans
    /// forever is indistinguishable from a stuck one, and it consumes the world while it
    /// does it</b>, emitting clicks at the client the whole time.
    /// </para>
    /// <para>
    /// What happens at the limit is deliberately <i>stopping</i> and not backing off: a
    /// cooldown here would be a second recovery mechanism beside
    /// <c>RecoveryController</c>'s, with its own sliding window and its own idea of when
    /// to try again, and two of those disagree. The walk ends with a named reason and the
    /// caller — which does own that ladder — decides what happens next.
    /// </para>
    /// </remarks>
    public const int DefaultMaxConsecutiveReplans = 3;

    /// <summary>
    /// Three acts in a row that nobody could confirm, and then stop.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>VER-05</c> says an unobservable outcome is neither success nor failure and that
    /// Recovery must not count it as a failure. That governs the <i>ledger</i>; it does
    /// not licence unbounded emission. Three consecutive acts with no reading that
    /// postdates them means the channel that would confirm them is not answering, and a
    /// fourth act only adds a fourth unconfirmed act to the series.
    /// </para>
    /// <para>
    /// Counted separately from replans on purpose. Re-routing cannot fix a verifier that
    /// cannot see, so spending the replan budget on it would hide the real fault behind
    /// the wrong name.
    /// </para>
    /// </remarks>
    public const int DefaultMaxConsecutiveUnverifiedSteps = 3;

    /// <summary>The defaults, argued above.</summary>
    public static ReplanPolicy Default { get; } = new();
}

/// <summary>
/// Walks a path one cell at a time, revalidating before each one, and stops when
/// re-routing has run out of things to try.
/// </summary>
/// <remarks>
/// <para>
/// <b>What counts as progress, and why "the cell changed" does not.</b> The replan
/// budget resets when the character gets <i>strictly closer to the destination than it
/// has ever been on this walk</i> — not when its cell differs from the last one. A
/// character oscillating between two cells changes cell on every observation and
/// arrives nowhere; counting that as progress would refill the budget forever and turn
/// the limit into decoration. This is the same defect P0 found in the recovery breaker,
/// where alternating outcomes never let the ladder climb, and it is the same repair:
/// judge the series, not the last sample.
/// </para>
/// <para>
/// <b>The three step outcomes are not interchangeable</b>, and the catalogue § 4.1 says
/// why. A <see cref="MovementOutcome.Stalled"/> step means the cell did not change while
/// the geometry called it open, so the plausible cause is an obstacle the grid does not
/// have: <i>replan rather than repeat</i>. A <see cref="MovementOutcome.Displaced"/> step
/// means the character moved somewhere nobody asked for, and repeating is worse than
/// stopping — the projection is aiming elsewhere, and every further click goes through
/// the same suspect transform. So displacement <b>abandons</b>; it does not replan.
/// </para>
/// <para>
/// <b>This class emits nothing.</b> It answers what should happen; the caller performs it
/// through <see cref="SingleStepExecutor"/>, which is where the guards and the commit
/// point live. Keeping the decision and the act apart is what lets the whole of P5 be
/// tested without a desktop, and it is why the proof that a bad path emits no input is a
/// proof about this object.
/// </para>
/// </remarks>
public sealed class PathWalkController
{
    /// <summary>Reported when the path was refused before anything was emitted.</summary>
    public const string NotAdmittedPrefix = "walk_path_not_admitted";

    /// <summary>Reported when the character does not start where the path does.</summary>
    public const string WrongStartPrefix = "walk_start_not_on_path";

    /// <summary>Reported when the replan budget is spent.</summary>
    public const string ReplanLimitPrefix = "walk_replan_limit_reached";

    /// <summary>Reported when too many acts in a row could not be verified.</summary>
    public const string UnverifiedLimitPrefix = "walk_unverified_limit_reached";

    /// <summary>Reported when a step went somewhere nobody asked for.</summary>
    public const string DisplacedPrefix = "walk_step_displaced";

    /// <summary>Reported when a decision is asked for before a path was accepted.</summary>
    public const string NoPathReason = "walk_no_path";

    /// <summary>Reported when a replacement path was offered after the walk had ended.</summary>
    public const string NotWalkingReason = "walk_not_active";

    private readonly ReplanPolicy _policy;

    private IReadOnlyList<MapPoint> _path = Array.Empty<MapPoint>();
    private MapPoint _destination;
    private int _next;
    private int _bestDistance = int.MaxValue;
    private int _consecutiveReplans;
    private int _consecutiveUnverified;
    private bool _walking;
    private bool _pendingStall;
    private string? _abandonReason;

    public PathWalkController(ReplanPolicy? policy = null)
    {
        _policy = policy ?? ReplanPolicy.Default;
    }

    /// <summary>The budget in force.</summary>
    public ReplanPolicy Policy => _policy;

    /// <summary>Whether a path is being walked.</summary>
    public bool IsWalking => _walking;

    /// <summary>The path as it stands, after any replans.</summary>
    public IReadOnlyList<MapPoint> Path => _path;

    /// <summary>Consecutive replans spent without progress.</summary>
    public int ReplansUsed => _consecutiveReplans;

    /// <summary>Total replans this walk has adopted, whether or not they were consecutive.</summary>
    public long ReplansAdopted { get; private set; }

    /// <summary>Steps whose outcome nobody could confirm, consecutively.</summary>
    public int UnverifiedInARow => _consecutiveUnverified;

    /// <summary>Cells stepped onto and confirmed.</summary>
    public long CellsAdvanced { get; private set; }

    /// <summary>Segment revalidations performed. The measurement JPS would have to beat.</summary>
    public long RevalidationCount { get; private set; }

    /// <summary>
    /// Admits a path and starts walking it, or refuses before anything is emitted.
    /// </summary>
    /// <remarks>
    /// The whole path is checked here, against static geometry, exactly once — P5's DoD
    /// is that a route crossing a blocked cell produces <i>no input</i>, and that can only
    /// be true if the crossing is found before the first step.
    /// </remarks>
    public bool TryStart(
        in MapGrid grid,
        IReadOnlyList<MapPoint> path,
        MapPoint observedPosition,
        out string? refusalReason)
    {
        ArgumentNullException.ThrowIfNull(path);

        PathAdmission admission = PathRevalidation.Admit(in grid, path);
        if (!admission.IsAdmitted)
        {
            refusalReason = $"{NotAdmittedPrefix}:{admission.RefusalReason}";
            _walking = false;
            return false;
        }

        if (path[0] != observedPosition)
        {
            refusalReason = string.Create(CultureInfo.InvariantCulture,
                $"{WrongStartPrefix}:{observedPosition.X},{observedPosition.Y}_not_{path[0].X},{path[0].Y}");
            _walking = false;
            return false;
        }

        _path = path;
        _destination = path[^1];
        _next = 1;
        _bestDistance = Distance(observedPosition, _destination);
        _consecutiveReplans = 0;
        _consecutiveUnverified = 0;
        ReplansAdopted = 0;
        CellsAdvanced = 0;
        RevalidationCount = 0;
        _pendingStall = false;
        _abandonReason = null;
        _walking = true;
        refusalReason = null;
        return true;
    }

    /// <summary>
    /// Decides what to do next, given where the character is and what has been seen.
    /// </summary>
    /// <remarks>
    /// Revalidates the one segment about to be walked. It is asked every time and never
    /// cached: the whole point of the check is that the answer can differ from the one it
    /// gave a moment ago.
    /// </remarks>
    public WalkDecision Next(
        in MapGrid grid,
        MapPoint observedPosition,
        in OccupancyView view,
        DateTime nowUtc)
    {
        if (!_walking)
            return new WalkDecision(WalkOutcome.Abandoned, null, _abandonReason ?? NoPathReason, _consecutiveReplans);

        NoteProgress(observedPosition);
        Resynchronise(observedPosition);

        if (observedPosition == _destination)
        {
            _walking = false;
            return new WalkDecision(WalkOutcome.Arrived, null, null, _consecutiveReplans);
        }

        // § 4.1: a stalled step means the cell did not change while the geometry called
        // it open, so the plausible cause is an obstacle the grid does not carry.
        // Re-route; do not send the same click again.
        if (_pendingStall)
        {
            _pendingStall = false;
            return Replan("walk_step_stalled");
        }

        if (_next >= _path.Count)
        {
            // The path ran out and the character is not on the destination: whatever
            // happened, the route no longer describes where it is.
            return Replan("walk_path_exhausted_before_destination");
        }

        MapPoint to = _path[_next];
        RevalidationCount++;

        SegmentRevalidation segment = PathRevalidation.Revalidate(
            in grid, observedPosition, to, in view, nowUtc);

        if (segment.IsClear)
            return new WalkDecision(WalkOutcome.Stepping, to, null, _consecutiveReplans);

        if (!segment.NeedsReplan)
        {
            // A refusal no route can fix: no grid, or a world the runtime has stopped
            // hearing from. Replanning against it would produce another path with the
            // same defect, repeatedly.
            _walking = false;
            return new WalkDecision(WalkOutcome.Abandoned, null, segment.RefusalReason, _consecutiveReplans);
        }

        return Replan(segment.RefusalReason!);
    }

    /// <summary>
    /// Reports what became of the step this controller asked for.
    /// </summary>
    /// <remarks>
    /// The controller does not observe the act itself, so the outcome has to be handed
    /// back. Everything that decides whether the walk continues is here rather than
    /// inferred from the next position reading, because a position that happens to match
    /// cannot distinguish "the step worked" from "the step did nothing and something else
    /// moved the character".
    /// </remarks>
    public void NoteStepOutcome(in MovementVerification verification)
    {
        if (!_walking)
            return;

        switch (verification.Outcome)
        {
            case MovementOutcome.Succeeded:
                _next++;
                CellsAdvanced++;
                _consecutiveUnverified = 0;
                break;

            case MovementOutcome.Stalled:
                // § 4.1: the plausible cause is an obstacle the grid does not carry, so
                // the next decision replans instead of repeating the same click.
                _consecutiveUnverified = 0;
                _pendingStall = true;
                break;

            case MovementOutcome.Displaced:
                // § 4.1 again, and the opposite conclusion: repeating is worse than
                // stopping, because the projection is aiming somewhere nobody chose.
                _walking = false;
                _abandonReason = $"{DisplacedPrefix}:{verification.Detail ?? "unknown"}";
                break;

            case MovementOutcome.Unobserved:
                _consecutiveUnverified++;
                if (_consecutiveUnverified >= _policy.MaxConsecutiveUnverifiedSteps)
                {
                    _walking = false;
                    _abandonReason = string.Create(CultureInfo.InvariantCulture,
                        $"{UnverifiedLimitPrefix}:{_consecutiveUnverified}_of_{_policy.MaxConsecutiveUnverifiedSteps}");
                }

                break;

            case MovementOutcome.Aborted:
            default:
                // A guard refused before emission. Nothing was done to the world, so
                // nothing about the walk has changed; the caller decides whether the
                // condition that refused is one it can clear.
                break;
        }
    }

    /// <summary>
    /// Offers a replacement path after a <see cref="WalkOutcome.Replan"/>.
    /// </summary>
    /// <remarks>
    /// Admitted exactly as the first one was: a re-route is a path like any other, and a
    /// second route through a wall is still a route through a wall.
    /// </remarks>
    public bool TryAdoptReplan(
        in MapGrid grid,
        IReadOnlyList<MapPoint> path,
        MapPoint observedPosition,
        out string? refusalReason)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (!_walking)
        {
            refusalReason = NotWalkingReason;
            return false;
        }

        PathAdmission admission = PathRevalidation.Admit(in grid, path);
        if (!admission.IsAdmitted)
        {
            refusalReason = $"{NotAdmittedPrefix}:{admission.RefusalReason}";
            return false;
        }

        if (path[0] != observedPosition)
        {
            refusalReason = string.Create(CultureInfo.InvariantCulture,
                $"{WrongStartPrefix}:{observedPosition.X},{observedPosition.Y}_not_{path[0].X},{path[0].Y}");
            return false;
        }

        // The counters that bound the walk are deliberately NOT reset. A replan is what
        // is being budgeted; letting the act of adopting one clear its own budget is the
        // shape of a limit that never fires.
        _path = path;
        _destination = path[^1];
        _next = 1;
        _pendingStall = false;
        ReplansAdopted++;
        refusalReason = null;
        return true;
    }

    /// <summary>Ends the walk on the caller's word.</summary>
    public void Cancel()
    {
        _walking = false;
        _path = Array.Empty<MapPoint>();
        _next = 0;
    }

    private WalkDecision Replan(string reason)
    {
        _consecutiveReplans++;

        if (_consecutiveReplans > _policy.MaxConsecutiveReplans)
        {
            _walking = false;
            _abandonReason = string.Create(CultureInfo.InvariantCulture,
                $"{ReplanLimitPrefix}:{_consecutiveReplans - 1}_of_{_policy.MaxConsecutiveReplans}:{reason}");
            return new WalkDecision(WalkOutcome.Abandoned, null, _abandonReason, _consecutiveReplans - 1);
        }

        return new WalkDecision(WalkOutcome.Replan, null, reason, _consecutiveReplans);
    }

    /// <summary>
    /// Puts the index back where the observation says the character is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The observation is the authority; the index is bookkeeping.</b> The index
    /// advances when a step is confirmed, so it falls behind whenever an outcome was not
    /// reported, or the client carried the character further than the act asked for.
    /// Treating that as "off the path" would replan a route that is still perfectly good,
    /// and would spend the budget doing it.
    /// </para>
    /// <para>
    /// <b>Forwards only, and it does not count as ground walked.</b> A route is walked in
    /// one direction, so the index never moves back; and
    /// <see cref="CellsAdvanced"/> still counts confirmed steps alone, because cells the
    /// runtime skipped past are cells it did not walk — whatever moved the character
    /// through them, it was not this. Nothing is stepped onto un-revalidated either: the
    /// resynchronisation moves an index, and every cell after it still passes
    /// <see cref="PathRevalidation.Revalidate"/> before it is emitted.
    /// </para>
    /// </remarks>
    private void Resynchronise(MapPoint observed)
    {
        for (int i = Math.Max(0, _next - 1); i < _path.Count; i++)
        {
            if (_path[i] != observed)
                continue;

            _next = i + 1;
            return;
        }
    }

    /// <summary>
    /// Resets the replan budget only on ground actually gained.
    /// </summary>
    /// <remarks>
    /// Strictly closer than the best this walk has ever been. Oscillating between two
    /// cells never satisfies it, which is the whole point.
    /// </remarks>
    private void NoteProgress(MapPoint observed)
    {
        int distance = Distance(observed, _destination);
        if (distance >= _bestDistance)
            return;

        _bestDistance = distance;
        _consecutiveReplans = 0;
    }

    private static int Distance(MapPoint a, MapPoint b) =>
        Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    /// <summary>
    /// Whether the walk has ended, and why — for a caller reporting an outcome it did not
    /// receive from <see cref="Next"/>, such as a displacement noted after the fact.
    /// </summary>
    public string? EndedBecause => _walking ? null : _abandonReason;

    /// <summary>Whether the last step stalled and the next decision will therefore re-route.</summary>
    public bool StallPending => _pendingStall;
}
