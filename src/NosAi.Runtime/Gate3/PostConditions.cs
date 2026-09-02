// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Gate 3 — What each action promises, and how that promise is checked
// ============================================================================
//
// docs/CATALOGO_AZIONI_E_POSTCONDIZIONI.md made normative in code. Its § 7 is the
// contract below; its § 4 is the eight cards; its § 2 is the nine VER rules, and
// each of them is enforced somewhere a test can reach rather than recommended in
// a comment.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NosAi.Runtime.Autonomy;
using NosAi.Runtime.Contracts;
using InventorySlotReading = NosAi.Runtime.Perception.Network.InventorySlotReading;
using NosAi.Runtime.Safety;

namespace NosAi.Runtime.Gate3
{
    /// <summary>What an action promises, and how that promise is checked.</summary>
    /// <remarks>
    /// <para>
    /// <b>VER-01 is enforced by this signature.</b> <see cref="Evaluate"/> never
    /// receives the prediction, so a post-condition cannot compare the world to
    /// what the simulator expected of it even by accident. That was the defect
    /// the whole catalogue was written against: one comparison, shared by all
    /// eight actions, of a string built from the prediction against a string
    /// built from the observation.
    /// </para>
    /// <para>
    /// <b>VER-06 is why <see cref="Window"/> is here</b> rather than on the cycle.
    /// A step and a potion are not checked over the same span, and a single loop
    /// interval would be wrong for both.
    /// </para>
    /// </remarks>
    public interface IPostCondition
    {
        /// <summary>The action this card belongs to.</summary>
        ActionType Action { get; }

        /// <summary>The action's own verification window (VER-06).</summary>
        TimeSpan Window { get; }

        /// <summary>
        /// Whether the window was measured against a recording or a live client,
        /// as opposed to declared for want of a measurement.
        /// </summary>
        /// <remarks>
        /// Every window in this file is declared today. The catalogue is explicit
        /// that a declared window must say so — the alternative is a number that
        /// reads like a measurement and is not — and names how each is measured:
        /// the attack cadence, for one, is the interval between two <c>su</c> with
        /// the player as attacker in <c>data/nostale_combat.noscap</c>.
        /// </remarks>
        bool WindowIsMeasured => false;

        /// <summary>
        /// Whether a failed or unverifiable verdict forbids repeating the action.
        /// </summary>
        /// <remarks>
        /// False for every card but <see cref="EmergencyFleePostCondition"/>. A
        /// flight whose verification failed is by construction a flight taken in a
        /// situation that has got worse, and a loop of unverified flights is what
        /// the recovery breaker exists to stop (§ 4.8).
        /// </remarks>
        bool RetryForbidden => false;

        /// <summary>Judges the action against what was observed, and nothing else.</summary>
        PostConditionVerdict Evaluate(in PostConditionInput input);
    }

    /// <summary>What a post-condition is given to judge on.</summary>
    /// <param name="Candidate">The action as it was authorised.</param>
    /// <param name="DispatchedAtUtc">
    /// When the act left the runtime. <b>VER-03</b> is measured from here: an
    /// observation stamped at or before this instant is not weak evidence, it is
    /// no evidence, because it describes a world the action had not yet touched.
    /// </param>
    /// <param name="States">
    /// The series observed around the window, not its two ends (<b>VER-09</b>).
    /// It may contain readings from before <paramref name="DispatchedAtUtc"/>:
    /// those are what the action was emitted on, and the card uses the latest of
    /// them as its baseline. Everything after is the window. Each element already
    /// carries the provenance and the instant of its own fields.
    /// </param>
    /// <param name="Sightings">
    /// Entity sightings across the window, each with its own instant. Kept beside
    /// <paramref name="States"/> because a state holds one list at one moment,
    /// while <b>VER-09</b> needs every sighting the window contained — a target's
    /// health can fall and be healed back inside one window.
    /// </param>
    /// <param name="Deaths">
    /// Entity ids the wire reported dead within the window. § 4.3 makes a death
    /// one of the two ways an attack is confirmed, and a death is an event that no
    /// snapshot of the world holds: an entity missing from a later list left the
    /// retention table, which is not the same fact. § 7's sketch of this type
    /// omitted it; without it half of § 4.3 cannot be written at all.
    /// </param>
    public readonly record struct PostConditionInput(
        ActionCandidate Candidate,
        DateTime DispatchedAtUtc,
        IReadOnlyList<Gate3WorldState> States,
        IReadOnlyList<SelectableEntity> Sightings,
        IReadOnlyList<long>? Deaths = null)
    {
        /// <summary>The readings taken after the act, in the order given.</summary>
        /// <remarks>
        /// Written as a loop rather than a query because a lambda inside a struct
        /// cannot close over <c>this</c>, and copying the instant into a local
        /// first would separate the filter from the field it filters on.
        /// </remarks>
        public IEnumerable<Gate3WorldState> AfterDispatch
        {
            get
            {
                DateTime dispatched = DispatchedAtUtc;
                foreach (Gate3WorldState state in States ?? Array.Empty<Gate3WorldState>())
                    if (state.ObservedAtUtc is { } at && at > dispatched)
                        yield return state;
            }
        }

        /// <summary>
        /// The reading the act was emitted on: the newest one not after dispatch,
        /// or null when the series holds none.
        /// </summary>
        public Gate3WorldState? AtDispatch
        {
            get
            {
                Gate3WorldState? baseline = null;
                DateTime newest = DateTime.MinValue;
                foreach (Gate3WorldState state in States ?? Array.Empty<Gate3WorldState>())
                {
                    if (state.ObservedAtUtc is not { } at || at > DispatchedAtUtc) continue;
                    if (baseline is null || at >= newest)
                    {
                        baseline = state;
                        newest = at;
                    }
                }
                return baseline;
            }
        }

        /// <summary>Sightings of one entity taken after the act.</summary>
        public IEnumerable<SelectableEntity> SightingsAfterDispatch(long entityId)
        {
            DateTime dispatched = DispatchedAtUtc;
            IReadOnlyList<SelectableEntity> sightings = Sightings ?? Array.Empty<SelectableEntity>();
            return sightings.Where(s => s.EntityId == entityId && s.ObservedAtUtc > dispatched);
        }

        /// <summary>Whether the wire reported this entity dead inside the window.</summary>
        public bool DiedInWindow(long entityId) => Deaths is { } deaths && deaths.Contains(entityId);
    }

    /// <summary>A card's judgement of one executed action.</summary>
    /// <param name="Divergence">
    /// In [0,1], where 0 is the promise kept and 1 the promise contradicted.
    /// Meaningful only for <see cref="VerificationOutcome.Confirmed"/> and
    /// <see cref="VerificationOutcome.Discrepant"/>: on
    /// <see cref="VerificationOutcome.Unverified"/> there is no distance to
    /// measure, and the field is zero because zero is not a measurement either.
    /// </param>
    /// <param name="Reason">
    /// An identifier, not prose: it is matched, logged and compared, so it stays
    /// invariant and lower-case with underscores. Where a number belongs in it,
    /// it is formatted with the invariant culture for the same reason
    /// <c>TargetSelector</c> does — otherwise the same refusal is two strings on
    /// two machines.
    /// </param>
    public readonly record struct PostConditionVerdict(
        VerificationOutcome Outcome,
        float Divergence,
        string Reason)
    {
        /// <summary>The promise was kept.</summary>
        public static PostConditionVerdict Confirmed(string reason) =>
            new(VerificationOutcome.Confirmed, 0.0f, reason);

        /// <summary>
        /// The world was observed and does not match the promise, at the measured
        /// distance. The band the divergence falls into decides what happens next
        /// (§ 5), and this type does not decide it.
        /// </summary>
        public static PostConditionVerdict Diverged(float divergence, string reason) =>
            new(
                DivergenceBands.Outcome(divergence),
                Math.Clamp(divergence, 0.0f, 1.0f),
                reason);

        /// <summary>
        /// Nothing could be observed, so the promise is neither kept nor broken
        /// (<b>VER-05</b>). Never a success, and the recovery breaker does not
        /// count it as a failure.
        /// </summary>
        public static PostConditionVerdict Unverified(string reason) =>
            new(VerificationOutcome.Unverified, 0.0f, reason);
    }

    /// <summary>
    /// The divergence bands of <c>ROADMAP_ESECUTIVA.md</c> § 8.3, and what each
    /// one means for the cycle (catalogue § 5).
    /// </summary>
    /// <remarks>
    /// They are not redefined here, only applied. What the catalogue adds — and
    /// what this type is — is the mapping from a band to the outcome Gate 3
    /// already produces and to the step that follows it.
    /// </remarks>
    public static class DivergenceBands
    {
        /// <summary>Below this the promise counts as kept.</summary>
        public const float Confirmed = 0.15f;

        /// <summary>At or above this, a replan is not enough.</summary>
        public const float Quarantine = 0.40f;

        /// <summary>At or above this the run stops.</summary>
        public const float HardStop = 0.70f;

        /// <summary>Which outcome a measured divergence carries.</summary>
        public static VerificationOutcome Outcome(float divergence) =>
            divergence < Confirmed ? VerificationOutcome.Confirmed : VerificationOutcome.Discrepant;

        /// <summary>
        /// What the runtime should do next, for a verdict that was measurable.
        /// </summary>
        /// <remarks>
        /// An unverified verdict never lands here: § 5 gives it <c>Replan</c> and
        /// <b>never</b> <c>Continue</c>, and that is decided by the caller, which
        /// is the only place that knows whether the action was executed at all.
        /// </remarks>
        public static RecoveryStrategy? Next(float divergence) => divergence switch
        {
            < Confirmed => null,
            < Quarantine => RecoveryStrategy.Replan,
            < HardStop => RecoveryStrategy.Cooling,
            _ => RecoveryStrategy.HaltAndAlert,
        };
    }

    /// <summary>The post-conditions, indexed by action type.</summary>
    /// <remarks>
    /// <para>
    /// <b>An absent entry is a refusal by name, never an action that executes and
    /// is then not verified.</b> That is the whole value of the table, and it is
    /// what makes <c>RestAndRecover</c>'s absence a decision rather than an
    /// oversight: the catalogue says its post-condition is to be written when a
    /// gesture exists and not before, so there is no card, so the action cannot
    /// be admitted.
    /// </para>
    /// <para>
    /// Indexed by the enum's own byte, populated once. A lookup is an array read,
    /// because it happens on the path of every action and a dictionary miss and a
    /// dictionary hit should not differ in cost on a safety check.
    /// </para>
    /// </remarks>
    public sealed class PostConditionTable
    {
        private readonly IPostCondition?[] _byAction;

        /// <summary>The eight cards of the catalogue, as they stand today.</summary>
        /// <remarks>
        /// Six are written. <c>RestAndRecover</c> and <see cref="ActionType.None"/>
        /// are absent on purpose — the first because § 4.7 says not to write it
        /// until a gesture exists, the second because it is not an action.
        /// </remarks>
        public static PostConditionTable Catalogue { get; } = new(
            new MoveToPositionPostCondition(),
            new TargetEntityPostCondition(),
            new UseBasicAttackPostCondition(),
            new UseSkillPostCondition(),
            new UseConsumablePostCondition(),
            new CollectGroundItemPostCondition(),
            new EmergencyFleePostCondition());

        public PostConditionTable(params IPostCondition[] postConditions)
        {
            ArgumentNullException.ThrowIfNull(postConditions);
            _byAction = new IPostCondition?[Enum.GetValues<ActionType>().Length];
            foreach (IPostCondition postCondition in postConditions)
            {
                ArgumentNullException.ThrowIfNull(postCondition);
                var index = (byte)postCondition.Action;
                if (index >= _byAction.Length)
                    throw new ArgumentOutOfRangeException(nameof(postConditions), $"Unknown action {postCondition.Action}.");
                if (_byAction[index] is not null)
                    throw new ArgumentException($"Two post-conditions declared for {postCondition.Action}.", nameof(postConditions));
                _byAction[index] = postCondition;
            }
        }

        /// <summary>The card for an action, or false when there is none.</summary>
        public bool TryGet(ActionType action, out IPostCondition postCondition)
        {
            var index = (byte)action;
            postCondition = index < _byAction.Length ? _byAction[index]! : null!;
            return postCondition is not null;
        }

        /// <summary>Whether an action may be admitted at all.</summary>
        /// <remarks>
        /// The property the catalogue asks the tests to fix: an action with no
        /// card is refused at admission, not executed and then left unverifiable.
        /// </remarks>
        public bool IsAdmissible(ActionType action) => TryGet(action, out _);

        /// <summary>The refusal an inadmissible action carries.</summary>
        public static string RefusalReason(ActionType action) =>
            string.Create(CultureInfo.InvariantCulture, $"no_post_condition:{action}");
    }

    // ------------------------------------------------------------------ § 4.1

    /// <summary>
    /// § 4.1 — the occupied cell moves toward the destination.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>HP and MP are not in this predicate.</b> A blow taken during a
    /// successful step does not make the step a failure, and believing otherwise
    /// is § 1.1 exactly: the old prediction for this action was <c>hpDelta = 0,
    /// mpDelta = 0</c>, so the check passed whenever nothing happened — including
    /// when the character had not moved at all — and failed whenever a monster
    /// hit during a move that worked.
    /// </para>
    /// <para>
    /// Three outcomes are kept apart because they call for different things:
    /// <b>arrived</b> (the distance shrank), <b>stalled</b> (the cell did not
    /// change, so something the grid does not know about is in the way, and
    /// replanning beats repeating), and <b>deviated</b> (the cell changed away
    /// from the destination, so the projection is aiming somewhere else and
    /// repeating is worse than stopping).
    /// </para>
    /// </remarks>
    public sealed class MoveToPositionPostCondition : IPostCondition
    {
        public ActionType Action => ActionType.MoveToPosition;

        /// <summary>
        /// The per-cell budget <c>P4</c> declares: 350 ms ± 20 ms.
        /// </summary>
        /// <remarks>
        /// Per cell, not per path. A route of many cells is revalidated segment by
        /// segment by navigation (C2-7); this card judges the change of position
        /// inside the window it is given, which is what makes it usable for a
        /// single step without pretending to time a walk.
        /// </remarks>
        public TimeSpan Window => TimeSpan.FromMilliseconds(350);

        public PostConditionVerdict Evaluate(in PostConditionInput input)
        {
            if (input.Candidate.Target is not ActionTarget.Position destination)
                return PostConditionVerdict.Unverified("move_without_a_destination");

            if (Position(input.AtDispatch) is not { } start)
                return PostConditionVerdict.Unverified("player_position_not_observed_at_dispatch");

            MapPoint? observed = null;
            foreach (Gate3WorldState state in input.AfterDispatch)
                if (Position(state) is { } point) observed = point;

            if (observed is not { } arrived)
                return PostConditionVerdict.Unverified("player_position_not_observed_after_dispatch");

            double planned = Distance(start, destination.At);
            double remaining = Distance(arrived, destination.At);
            var divergence = (float)Math.Clamp(remaining / Math.Max(1.0, planned), 0.0, 1.0);

            if (arrived == start)
            {
                // The cell did not change. An obstacle the grid does not carry is
                // the plausible cause, and repeating the same click would meet it
                // again.
                return PostConditionVerdict.Diverged(1.0f, "move_stalled_cell_unchanged");
            }

            if (remaining > planned)
                return PostConditionVerdict.Diverged(1.0f, "move_deviated_away_from_destination");

            return divergence < DivergenceBands.Confirmed
                ? PostConditionVerdict.Confirmed(Reason("move_arrived", divergence))
                : PostConditionVerdict.Diverged(divergence, Reason("move_short_of_destination", divergence));
        }

        private static MapPoint? Position(Gate3WorldState? state) =>
            state?.PlayerPosition is { HasValue: true } position ? position.Value : null;

        /// <summary>
        /// Straight-line distance, the same measure <see cref="TargetSelector"/>
        /// uses and for the same reason: how this client charges a diagonal step
        /// has not been measured, and a wrong movement metric would silently
        /// reorder what counts as progress.
        /// </summary>
        internal static double Distance(MapPoint a, MapPoint b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        internal static string Reason(string name, double divergence) =>
            string.Create(CultureInfo.InvariantCulture, $"{name}:d={divergence:F2}");
    }

    // ------------------------------------------------------------------ § 4.2

    /// <summary>
    /// § 4.2 — after the click a target exists, and <c>HasTarget</c> says so.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ADR-0018's three reader outcomes are reused unchanged and are not collapsed
    /// into two: present is <c>d = 0</c>, absent is <c>d = 1</c>, and unreadable
    /// is <see cref="VerificationOutcome.Unverified"/> — never <c>d = 1</c>. An
    /// unreadable frame turned into a failure would be a confident wrong answer
    /// about the one fact that decides whether the runtime is in a fight.
    /// </para>
    /// <para>
    /// The wire does not establish this and does not try. <c>ct</c> names
    /// <i>which</i> entity was acted on and has no observed counterpart that
    /// clears a selection, so a flag derived from it would go true once and stay
    /// true with nothing ever correcting it.
    /// </para>
    /// <para>
    /// <b>Under VER-08 this card is blind today</b>, because the target ROI has
    /// never been calibrated against a real client: <c>HasTarget</c> is UNKNOWN
    /// with <c>target_roi_not_calibrated</c>, so every verdict here is
    /// <c>Unverified</c> carrying that reason. That is the catalogue's own
    /// position — the reason is stated from the outcome's side rather than only
    /// from the planner's.
    /// </para>
    /// </remarks>
    public sealed class TargetEntityPostCondition : IPostCondition
    {
        public ActionType Action => ActionType.TargetEntity;

        /// <summary>250 ms, declared. Nobody has measured how fast the frame appears.</summary>
        public TimeSpan Window => TimeSpan.FromMilliseconds(250);

        public PostConditionVerdict Evaluate(in PostConditionInput input)
        {
            ClassifiedValue<bool>? latest = null;
            foreach (Gate3WorldState state in input.AfterDispatch)
                latest = state.HasTarget;

            if (latest is not { } hasTarget)
                return PostConditionVerdict.Unverified("target_frame_not_observed_after_dispatch");

            if (!hasTarget.HasValue)
            {
                // The reader's own reason, carried out rather than replaced: the
                // operator needs to know whether this is an uncalibrated region or
                // a frame that could not be read.
                return PostConditionVerdict.Unverified(
                    hasTarget.FailureReason ?? "target_frame_unreadable");
            }

            return hasTarget.Value
                ? PostConditionVerdict.Confirmed("target_frame_present")
                : PostConditionVerdict.Diverged(1.0f, "target_frame_absent");
        }
    }

    // ------------------------------------------------------------------ § 4.3

    /// <summary>
    /// § 4.3 — the <b>target's</b> health falls, or the target dies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>VER-07 made concrete.</b> The subject is the target's entity id and
    /// never the character. The prediction this replaces expected the
    /// <i>player</i> to lose 15 HP — a retaliation nobody measured, and not the
    /// effect of the action at all.
    /// </para>
    /// <para>
    /// A sighting carries a nullable health, and a sighting without one is by far
    /// the common case: 7 685 of 8 211 packets in the capture are moves, and a
    /// move states a position and nothing about condition. That is <b>not</b> an
    /// unchanged health. With no health reading of the target inside the window
    /// the verdict is <c>Unverified</c>, which is the difference between "it did
    /// not work" and "nobody looked".
    /// </para>
    /// </remarks>
    public sealed class UseBasicAttackPostCondition : IPostCondition
    {
        public ActionType Action => ActionType.UseBasicAttack;

        /// <summary>
        /// 1 200 ms, <b>declared and not measured</b>.
        /// </summary>
        /// <remarks>
        /// The measurement is available and has not been taken: the interval
        /// between two <c>su</c> with the player as attacker in
        /// <c>data/nostale_combat.noscap</c> is the real cadence, and 117 such
        /// packets are in that recording. Until somebody takes it this number is
        /// a placeholder that says so.
        /// </remarks>
        public TimeSpan Window => TimeSpan.FromMilliseconds(1200);

        public PostConditionVerdict Evaluate(in PostConditionInput input)
        {
            if (input.Candidate.Target is not ActionTarget.Entity { IsResolved: true } target)
                return PostConditionVerdict.Unverified("attack_without_a_resolved_target");

            // A death settles it whatever the health readings did or did not say.
            if (input.DiedInWindow(target.EntityId))
                return PostConditionVerdict.Confirmed("target_died");

            SelectableEntity[] after = input.SightingsAfterDispatch(target.EntityId).ToArray();
            if (after.Length == 0)
                return PostConditionVerdict.Unverified("target_not_sighted_in_window");

            double[] healths = after.Where(s => s.HpRatio is not null).Select(s => s.HpRatio!.Value).ToArray();
            if (healths.Length == 0)
            {
                // Located and never described. The move packets say where it is
                // and nothing about how it is, and reading that as unchanged
                // health would be the invented observation this card exists
                // against.
                return PostConditionVerdict.Unverified("target_health_not_observed_in_window");
            }

            if (BaselineHealth(input, target.EntityId) is not { } baseline)
                return PostConditionVerdict.Unverified("target_health_not_observed_at_dispatch");

            // VER-09: the minimum across the window, not the last reading. A
            // target healed back up inside the window was still hit.
            double lowest = healths.Min();
            return lowest < baseline
                ? PostConditionVerdict.Confirmed(
                    string.Create(CultureInfo.InvariantCulture, $"target_health_fell:{baseline:F2}->{lowest:F2}"))
                : PostConditionVerdict.Diverged(1.0f, "target_health_did_not_fall");
        }

        /// <summary>The target's health as the act was emitted, from either source.</summary>
        private static double? BaselineHealth(in PostConditionInput input, long entityId)
        {
            double? baseline = null;
            DateTime newest = DateTime.MinValue;

            foreach (SelectableEntity sighting in input.Sightings ?? Array.Empty<SelectableEntity>())
            {
                if (sighting.EntityId != entityId
                    || sighting.HpRatio is not { } health
                    || sighting.ObservedAtUtc > input.DispatchedAtUtc)
                    continue;
                if (baseline is null || sighting.ObservedAtUtc >= newest)
                {
                    baseline = health;
                    newest = sighting.ObservedAtUtc;
                }
            }

            if (baseline is not null) return baseline;

            // The state the act was emitted on carries the same entity list, and
            // for a single-cycle input that is the only place the baseline is.
            if (input.AtDispatch?.Entities is not { } entities) return null;
            foreach (SelectableEntity entity in entities)
                if (entity.EntityId == entityId && entity.HpRatio is { } health)
                    return health;

            return null;
        }
    }

    // ------------------------------------------------------------------ § 4.4

    /// <summary>
    /// § 4.4 — the MP fall, and the skill goes on cooldown. The second half is
    /// declared unobservable rather than assumed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first half distinguishes a skill that fired from a key pressed into
    /// nothing, which is why <b>VER-08</b> leaves the action executable.
    /// </para>
    /// <para>
    /// The second half stays open even now that <c>sr</c> is decoded, and the
    /// reason is worth stating because it looks closed. <c>sr</c> reports a slot
    /// becoming <i>ready</i> — the cooldown <i>ending</i> — and no packet in
    /// either capture reports one <i>beginning</i>. Reading a readiness message as
    /// evidence of entry would invert it. So the verdict carries the first half's
    /// divergence and the reason <c>cooldown_not_observable</c>, and the planner
    /// may not conclude from a <c>Confirmed</c> that the skill is now recharging.
    /// </para>
    /// <para>
    /// The prohibition that follows: no automatic retry from the backend
    /// (<c>P7</c>). With the cooldown blind, a repeated attempt is not a retry —
    /// it is a second action taken against an unknown state.
    /// </para>
    /// </remarks>
    public sealed class UseSkillPostCondition : IPostCondition
    {
        /// <summary>The half of the promise no packet establishes.</summary>
        public const string CooldownNotObservable = "cooldown_not_observable";

        public ActionType Action => ActionType.UseSkill;

        /// <summary>250 ms, the budget <c>P7</c> declares for the MP to move.</summary>
        public TimeSpan Window => TimeSpan.FromMilliseconds(250);

        public PostConditionVerdict Evaluate(in PostConditionInput input)
        {
            if (input.AtDispatch is not { Mp.HasValue: true } baseline)
                return PostConditionVerdict.Unverified($"mp_not_observed_at_dispatch;{CooldownNotObservable}");

            int[] observed = input.AfterDispatch
                .Where(s => s.Mp.HasValue)
                .Select(s => s.Mp.Value)
                .ToArray();

            if (observed.Length == 0)
                return PostConditionVerdict.Unverified($"mp_not_observed_in_window;{CooldownNotObservable}");

            // VER-09: the minimum across the window. MP spent and regenerated
            // inside the window was still spent.
            int lowest = observed.Min();
            int before = baseline.Mp.Value;

            return lowest < before
                ? PostConditionVerdict.Confirmed(
                    string.Create(CultureInfo.InvariantCulture, $"mp_fell:{before}->{lowest};{CooldownNotObservable}"))
                : PostConditionVerdict.Diverged(1.0f, $"mp_did_not_fall;{CooldownNotObservable}");
        }
    }

    // ------------------------------------------------------------------ § 4.5

    /// <summary>
    /// § 4.5 — the HP or the MP rise above what they were when the act was sent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The card that pays <b>VER-09</b> in full. The direction is what is checked;
    /// the amount is not. A potion's yield sits in the reference catalogue among
    /// 7 726 items, and which <c>ATTRIB</c> slot carries which meaning is not
    /// established anywhere — so the exact <c>+300</c> the old prediction demanded
    /// was a number nobody had, and it made the predicate false in both directions
    /// (§ 1.3).
    /// </para>
    /// <para>
    /// <b>The ambiguity is named, not resolved.</b> No maximum above the emission
    /// value has two causes this source cannot separate: the slot was empty, or
    /// the healing was entirely undone by blows inside the same window. The
    /// verdict says <c>heal_not_observed_ambiguous</c>, and <c>ivn</c> is what will
    /// separate them.
    /// </para>
    /// </remarks>
    public sealed class UseConsumablePostCondition : IPostCondition
    {
        /// <summary>The two causes this source cannot tell apart.</summary>
        public const string Ambiguous = "heal_not_observed_ambiguous";

        public ActionType Action => ActionType.UseConsumable;

        /// <summary>600 ms, declared.</summary>
        public TimeSpan Window => TimeSpan.FromMilliseconds(600);

        public PostConditionVerdict Evaluate(in PostConditionInput input)
        {
            if (input.AtDispatch is not { } baseline || !(baseline.Hp.HasValue || baseline.Mp.HasValue))
                return PostConditionVerdict.Unverified("vitals_not_observed_at_dispatch");

            Gate3WorldState[] after = input.AfterDispatch.ToArray();
            if (after.Length == 0)
                return PostConditionVerdict.Unverified("vitals_not_observed_in_window");

            if (Rose(baseline.Hp, after.Select(s => s.Hp), out int fromHp, out int toHp))
            {
                return PostConditionVerdict.Confirmed(
                    string.Create(CultureInfo.InvariantCulture, $"hp_rose:{fromHp}->{toHp}"));
            }

            if (Rose(baseline.Mp, after.Select(s => s.Mp), out int fromMp, out int toMp))
            {
                return PostConditionVerdict.Confirmed(
                    string.Create(CultureInfo.InvariantCulture, $"mp_rose:{fromMp}->{toMp}"));
            }

            // Neither rose. That the window carried no reading at all is a
            // different fact, and it was answered above.
            bool looked = after.Any(s => s.Hp.HasValue || s.Mp.HasValue);
            return looked
                ? PostConditionVerdict.Diverged(1.0f, Ambiguous)
                : PostConditionVerdict.Unverified("vitals_not_observed_in_window");
        }

        /// <summary>Whether the maximum across the window beats the emission value.</summary>
        private static bool Rose(
            ClassifiedValue<int> baseline, IEnumerable<ClassifiedValue<int>> window, out int from, out int to)
        {
            from = 0;
            to = 0;
            if (!baseline.HasValue) return false;

            int[] observed = window.Where(v => v.HasValue).Select(v => v.Value).ToArray();
            if (observed.Length == 0) return false;

            from = baseline.Value;
            to = observed.Max();
            return to > from;
        }
    }

    // ------------------------------------------------------------------ § 4.6

    /// <summary>
    /// § 4.6 — an inventory slot gains what was picked up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The natural predicate is "the inventory slot for the collected vnum went
    /// up", and until C1-3 neither of its two sources — <c>get</c> and <c>ivn</c>
    /// — was decoded, which is why the catalogue recorded the card as blind. Both
    /// are decoded now and reach the state, so the predicate is writable: a slot
    /// whose amount strictly increased, or a slot that appears holding something
    /// where nothing was held, inside the window.
    /// </para>
    /// <para>
    /// <b>It is still not executable, and for a different reason.</b>
    /// <c>InputActionEffector</c> has no gesture for it and refuses by name with
    /// <c>action_not_implemented</c>. That refusal is correct and is unchanged
    /// here; what changed is that the blindness is no longer the reason.
    /// </para>
    /// <para>
    /// The predicate is keyed on any slot rather than on the collected vnum,
    /// because the candidate names a place or an entity and not a vnum. That is
    /// the honest reading of what the runtime holds today, and the tightening is
    /// named: once the candidate carries the drop's id, the <c>get</c> for that id
    /// pins which vnum to expect.
    /// </para>
    /// </remarks>
    public sealed class CollectGroundItemPostCondition : IPostCondition
    {
        public ActionType Action => ActionType.CollectGroundItem;

        /// <summary>600 ms, declared. No collection has ever been timed.</summary>
        public TimeSpan Window => TimeSpan.FromMilliseconds(600);

        public PostConditionVerdict Evaluate(in PostConditionInput input)
        {
            if (Slots(input.AtDispatch) is not { } before)
                return PostConditionVerdict.Unverified("inventory_not_observed_at_dispatch");

            IReadOnlyList<InventorySlotReading>? latest = null;
            foreach (Gate3WorldState state in input.AfterDispatch)
                if (Slots(state) is { } slots) latest = slots;

            if (latest is not { } after)
                return PostConditionVerdict.Unverified("inventory_not_observed_in_window");

            Dictionary<(int Kind, int Slot), int> held = before
                .GroupBy(s => (s.InventoryKind, s.Slot))
                .ToDictionary(g => g.Key, g => g.Max(s => s.Amount));

            foreach (InventorySlotReading slot in after)
            {
                var key = (slot.InventoryKind, slot.Slot);
                // A slot nobody had read before now holding something is a gain:
                // an unread slot is not a slot known to be empty, but a slot that
                // now reports an item where the runtime had no reading is the only
                // observation a first collection into an empty bag can produce.
                if (!held.TryGetValue(key, out int previous))
                {
                    return PostConditionVerdict.Confirmed(
                        string.Create(CultureInfo.InvariantCulture, $"inventory_slot_appeared:{slot.Slot}x{slot.Amount}"));
                }

                if (slot.Amount > previous)
                {
                    return PostConditionVerdict.Confirmed(
                        string.Create(CultureInfo.InvariantCulture, $"inventory_slot_rose:{slot.Slot}:{previous}->{slot.Amount}"));
                }
            }

            return PostConditionVerdict.Diverged(1.0f, "inventory_did_not_gain");
        }

        private static IReadOnlyList<InventorySlotReading>? Slots(Gate3WorldState? state) =>
            state?.Inventory is { HasValue: true } inventory ? inventory.Value : null;
    }

    // ------------------------------------------------------------------ § 4.8

    /// <summary>
    /// § 4.8 — the distance from the nearest observed hostile grows.
    /// </summary>
    /// <remarks>
    /// <b>The rule this card adds to the others:</b> a flight that was not verified
    /// is not repeated. Every other action may fall back to a replan; this one may
    /// not, because the case in which the check fails is by construction the case
    /// in which the situation has got worse, and a loop of unverified flights is
    /// precisely the behaviour the recovery breaker exists to stop. A
    /// <c>Discrepant</c> or <c>Unverified</c> flight escalates straight to a hard
    /// stop with an alarm to the operator, which is what
    /// <see cref="IPostCondition.RetryForbidden"/> carries.
    /// </remarks>
    public sealed class EmergencyFleePostCondition : IPostCondition
    {
        public ActionType Action => ActionType.EmergencyFlee;

        /// <summary>500 ms plus a cell's budget, declared.</summary>
        public TimeSpan Window => TimeSpan.FromMilliseconds(500 + 350);

        /// <summary>An unverified flight is never repeated.</summary>
        public bool RetryForbidden => true;

        public PostConditionVerdict Evaluate(in PostConditionInput input)
        {
            if (input.Candidate.Target is not ActionTarget.Position destination)
                return PostConditionVerdict.Unverified("flee_without_a_destination");

            if (input.AtDispatch is not { PlayerPosition.HasValue: true } baseline)
                return PostConditionVerdict.Unverified("player_position_not_observed_at_dispatch");

            MapPoint start = baseline.PlayerPosition!.Value;
            if (NearestHostile(baseline, start) is not { } before)
                return PostConditionVerdict.Unverified("no_hostile_observed_at_dispatch");

            Gate3WorldState? last = null;
            foreach (Gate3WorldState state in input.AfterDispatch)
                if (state.PlayerPosition is { HasValue: true }) last = state;

            if (last is not { } arrivedState)
                return PostConditionVerdict.Unverified("player_position_not_observed_after_dispatch");

            MapPoint arrived = arrivedState.PlayerPosition!.Value;
            if (NearestHostile(arrivedState, arrived) is not { } after)
                return PostConditionVerdict.Unverified("no_hostile_observed_after_dispatch");

            double expected = Math.Max(1.0, MoveToPositionPostCondition.Distance(start, destination.At));
            var divergence = (float)Math.Clamp(1.0 - ((after - before) / expected), 0.0, 1.0);

            return divergence < DivergenceBands.Confirmed
                ? PostConditionVerdict.Confirmed(
                    string.Create(CultureInfo.InvariantCulture, $"fled:{before:F1}->{after:F1}_tiles"))
                : PostConditionVerdict.Diverged(
                    divergence,
                    string.Create(CultureInfo.InvariantCulture, $"flee_did_not_open_distance:{before:F1}->{after:F1}_tiles"));
        }

        /// <summary>
        /// The distance to the nearest entity the state carries, or null when it
        /// carries none.
        /// </summary>
        /// <remarks>
        /// "Hostile" is as far as the observation goes: the wire's type 3 is
        /// monster and NPC together, and this card does not classify. Measuring
        /// against the nearest observed entity is the conservative reading — a
        /// merchant standing where a monster was makes the flight look less
        /// successful, never more.
        /// </remarks>
        private static double? NearestHostile(Gate3WorldState state, MapPoint from)
        {
            if (state.Entities is not { Count: > 0 } entities) return null;

            double nearest = double.MaxValue;
            foreach (SelectableEntity entity in entities)
            {
                // A known-dead entity is not something to flee from.
                if (entity.HpRatio is <= 0) continue;
                nearest = Math.Min(nearest, MoveToPositionPostCondition.Distance(from, entity.At));
            }

            return nearest == double.MaxValue ? null : nearest;
        }
    }
}
