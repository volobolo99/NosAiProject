using System.Globalization;

namespace NosAi.Runtime.Navigation;

/// <summary>One place that behaves the way a skill's cooldown behaves.</summary>
/// <param name="Anchor">The base the offset is measured from.</param>
/// <param name="Offset">Distance from that base; the address itself when heap.</param>
/// <param name="Slot">The skill slot this word tracked, as the wire numbers slots.</param>
public readonly record struct SkillCooldownHit(
    MapIdAnchorKind Anchor,
    int Offset,
    int Slot)
{
    /// <summary>Whether the offset means anything after the client is restarted.</summary>
    public bool IsDurable => Anchor is not MapIdAnchorKind.Heap;

    /// <summary>How the operator sees it.</summary>
    public string Describe() => Anchor is MapIdAnchorKind.Heap
        ? string.Create(CultureInfo.InvariantCulture, $"slot {Slot} @ 0x{Offset:X8} (heap)")
        : string.Create(CultureInfo.InvariantCulture, $"slot {Slot} @ {Anchor}+0x{Offset:X}");
}

/// <summary>Why a hunt for one slot ended where it did.</summary>
public enum SkillCooldownOutcome
{
    /// <summary>Not enough evidence has been fed yet to conclude anything.</summary>
    Undecided = 0,

    /// <summary>Exactly one word behaved like this slot's cooldown throughout.</summary>
    Established = 1,

    /// <summary>Several words survived. Honest, and not an answer.</summary>
    Ambiguous = 2,

    /// <summary>Nothing survived. Also honest, and the phase closes anyway.</summary>
    NoCandidate = 3
}

/// <summary>What the hunt concluded for one slot, with the reason attached.</summary>
/// <param name="Slot">The slot this verdict is about.</param>
/// <param name="Outcome">How it ended.</param>
/// <param name="Survivors">The words still standing, in offset order.</param>
/// <param name="Readies">Ready events this slot was narrowed against.</param>
/// <param name="Uses">Use events this slot was narrowed against.</param>
/// <param name="Reason">
/// Null when <see cref="SkillCooldownOutcome.Established"/>; otherwise names what is
/// missing or what went wrong, in the form the operator can act on.
/// </param>
public readonly record struct SkillCooldownVerdict(
    int Slot,
    SkillCooldownOutcome Outcome,
    IReadOnlyList<SkillCooldownHit> Survivors,
    int Readies,
    int Uses,
    string? Reason)
{
    /// <summary>The single surviving word, when there is exactly one.</summary>
    public SkillCooldownHit? Single =>
        Outcome is SkillCooldownOutcome.Established && Survivors.Count == 1 ? Survivors[0] : null;
}

/// <summary>
/// Finds where the client keeps a skill's remaining cooldown, by how that place
/// behaves rather than by what it contains (phase 3 of
/// <c>docs/SPEC_ESTENSIONE_LAYOUT_MEMORIA.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this phase does not start from the numbers.</b> It is the weakest of the
/// three and the spec says so: the two available sources disagree with each other
/// about the chain — <c>{…,0x0,0x24}</c> in one, <c>{…,0x0,0x8,0x14}</c> in the
/// other. Starting from either would be picking a winner by preference. The stride
/// of <c>0x48</c> between consecutive skills, and the claim that slots 1-4 and 5+
/// live in two separate tables, are hypotheses this oracle can <i>confirm</i>; they
/// are never premises, and nothing here reads them.
/// </para>
/// <para>
/// <b>The constraint, and why it needs both directions.</b> A word is a candidate
/// only if it falls to zero exactly when the skill becomes available and rises
/// exactly when the skill is used. Either half alone admits an ocean of words: a
/// word that is permanently zero satisfies "zero when ready" every single time, and
/// a word that is permanently non-zero satisfies nothing but would survive a rule
/// that only watched rises. Requiring both is what excludes them, and it is the same
/// shape of argument <see cref="TargetIdFinder"/> makes about its second clearing —
/// the first observation only records, the second is the one that proves.
/// </para>
/// <para>
/// <b>Where the ground truth comes from.</b> The wire announces the restoration:
/// <c>sr</c> names the slot that came off cooldown, and its arrival is the instant
/// the memory word must already have reached zero. That is the second, independent
/// source ADR-0014 asks for, and without it this hunt has no clock to check against.
/// </para>
/// <para>
/// <b>Non-convergence is a result.</b> If no word survives, the slot's cooldown stays
/// UNKNOWN and the phase closes anyway. An unknown cooldown is honest information; a
/// wrong one makes Ranking propose actions that Verify then discovers failed, one at
/// a time.
/// </para>
/// <para>
/// This type is pure: it is fed samples and events, and holds no handle on the
/// client. What reads the memory and what listens to the wire stay outside it, which
/// is what lets the whole rule be driven by a test.
/// </para>
/// </remarks>
public sealed class SkillCooldownFinder
{
    /// <summary>
    /// Times a word must fall to zero at a restoration before it is believed.
    /// </summary>
    /// <remarks>
    /// Two. The first fall only <i>records</i> that this word was non-zero and then
    /// was not; any word that happened to change at that moment would pass it. The
    /// second requires the same behaviour again, which coincidence does not repeat.
    /// </remarks>
    public const int RequiredReadies = 2;

    /// <summary>
    /// Times a word must rise from zero when the skill is used before it is believed.
    /// </summary>
    /// <remarks>
    /// One is enough, because this half is doing a different job from
    /// <see cref="RequiredReadies"/>: it is what excludes the words that are simply
    /// always zero, and a word that is always zero fails it the first time.
    /// </remarks>
    public const int RequiredUses = 1;

    /// <summary>Reported when a slot has seen no evidence at all.</summary>
    public const string NoEvidenceReason = "skill_cooldown_no_observation";

    /// <summary>Reported when the falls or rises seen so far are not yet enough.</summary>
    public const string NotEnoughEvidencePrefix = "skill_cooldown_evidence_short";

    /// <summary>Reported when every candidate was eliminated.</summary>
    public const string NoCandidateReason = "skill_cooldown_no_candidate";

    /// <summary>Reported when more than one word survived the whole hunt.</summary>
    public const string AmbiguousPrefix = "skill_cooldown_ambiguous";

    private readonly Dictionary<int, SlotState> _slots = [];

    /// <summary>
    /// Every offset any sample has ever carried: the universe a slot's first event
    /// narrows out of.
    /// </summary>
    /// <remarks>
    /// It has to be accumulated rather than assumed, because this type is never told
    /// what the process contains — it only ever sees the words it is handed. A word
    /// that appears for the first time after a slot's first event is not retro-fitted
    /// into that slot's candidates: it was not there to be eliminated, so believing
    /// it now would let a word join the set without ever facing the evidence that
    /// removed its peers.
    /// </remarks>
    private readonly HashSet<int> _seen = [];

    /// <summary>The slots this hunt has been given any evidence about, in order.</summary>
    public IReadOnlyList<int> Slots
    {
        get
        {
            var slots = new List<int>(_slots.Keys);
            slots.Sort();
            return slots;
        }
    }

    /// <summary>
    /// Offers a memory sample: what each candidate word held at one instant.
    /// </summary>
    /// <remarks>
    /// Kept as the <i>previous</i> reading, and consumed by the next event. The order
    /// that matters is the one the constraint is written in — a word must have been
    /// non-zero <b>before</b> the restoration and zero <b>at</b> it — so a sample
    /// taken after the event can say nothing about the transition, and a hunt fed
    /// only post-event samples ends with no candidate rather than a wrong one.
    /// </remarks>
    public void Observe(IReadOnlyDictionary<int, uint> wordsByOffset)
    {
        ArgumentNullException.ThrowIfNull(wordsByOffset);
        Remember(wordsByOffset);
    }

    private Dictionary<int, uint>? _previous;

    private void Remember(IReadOnlyDictionary<int, uint> wordsByOffset)
    {
        foreach (int offset in wordsByOffset.Keys)
            _seen.Add(offset);

        _previous = new Dictionary<int, uint>(wordsByOffset);
    }

    /// <summary>
    /// The wire said this slot came off cooldown. Every candidate that was not
    /// non-zero before and zero now is eliminated.
    /// </summary>
    /// <param name="slot">The slot <c>sr</c> named.</param>
    /// <param name="wordsByOffset">The memory sample taken at that moment.</param>
    public void NoteReady(int slot, IReadOnlyDictionary<int, uint> wordsByOffset)
    {
        ArgumentNullException.ThrowIfNull(wordsByOffset);
        SlotState state = StateFor(slot);

        // Without a prior sample there is no transition to judge, so this event
        // narrows nothing rather than narrowing wrongly.
        if (_previous is { } before)
        {
            Narrow(state, offset =>
                before.TryGetValue(offset, out uint was)
                && was != 0
                && wordsByOffset.TryGetValue(offset, out uint now)
                && now == 0);

            state.Readies++;
        }

        Remember(wordsByOffset);
    }

    /// <summary>
    /// The skill was used. Every candidate that did not rise from zero is eliminated.
    /// </summary>
    /// <param name="slot">The slot that was used.</param>
    /// <param name="wordsByOffset">The memory sample taken just after the use.</param>
    public void NoteUsed(int slot, IReadOnlyDictionary<int, uint> wordsByOffset)
    {
        ArgumentNullException.ThrowIfNull(wordsByOffset);
        SlotState state = StateFor(slot);

        if (_previous is { } before)
        {
            Narrow(state, offset =>
                before.TryGetValue(offset, out uint was)
                && was == 0
                && wordsByOffset.TryGetValue(offset, out uint now)
                && now != 0);

            state.Uses++;
        }

        Remember(wordsByOffset);
    }

    /// <summary>What the hunt can say about one slot right now.</summary>
    public SkillCooldownVerdict Verdict(int slot, MapIdAnchorKind anchor = MapIdAnchorKind.Heap)
    {
        if (!_slots.TryGetValue(slot, out SlotState? state))
            return new SkillCooldownVerdict(slot, SkillCooldownOutcome.Undecided, [], 0, 0, NoEvidenceReason);

        var survivors = new List<SkillCooldownHit>();
        if (state.Candidates is { } alive)
        {
            var offsets = new List<int>(alive);
            offsets.Sort();
            foreach (int offset in offsets)
                survivors.Add(new SkillCooldownHit(anchor, offset, slot));
        }

        if (state.Readies < RequiredReadies || state.Uses < RequiredUses)
        {
            string reason = string.Create(CultureInfo.InvariantCulture,
                $"{NotEnoughEvidencePrefix}:readies={state.Readies}/{RequiredReadies}:uses={state.Uses}/{RequiredUses}");
            return new SkillCooldownVerdict(
                slot, SkillCooldownOutcome.Undecided, survivors, state.Readies, state.Uses, reason);
        }

        if (survivors.Count == 0)
        {
            return new SkillCooldownVerdict(
                slot, SkillCooldownOutcome.NoCandidate, survivors, state.Readies, state.Uses, NoCandidateReason);
        }

        if (survivors.Count > 1)
        {
            string reason = string.Create(CultureInfo.InvariantCulture,
                $"{AmbiguousPrefix}:{survivors.Count}");
            return new SkillCooldownVerdict(
                slot, SkillCooldownOutcome.Ambiguous, survivors, state.Readies, state.Uses, reason);
        }

        return new SkillCooldownVerdict(
            slot, SkillCooldownOutcome.Established, survivors, state.Readies, state.Uses, null);
    }

    /// <summary>
    /// The distance between consecutive established slots, when every gap is the same.
    /// </summary>
    /// <remarks>
    /// <b>Reported, never assumed.</b> The candidate map claims a stride of
    /// <c>0x48</c>; this returns what the surviving offsets actually show, so the
    /// claim can be checked against a measurement instead of standing in for one. It
    /// is null whenever fewer than two slots are established or the gaps disagree —
    /// a stride that holds for some pairs and not others is not a stride, and
    /// averaging it would manufacture a number nothing observed.
    /// </remarks>
    public int? ObservedStride(MapIdAnchorKind anchor = MapIdAnchorKind.Heap)
    {
        var offsets = new List<int>();
        foreach (int slot in Slots)
        {
            if (Verdict(slot, anchor).Single is { } hit)
                offsets.Add(hit.Offset);
        }

        if (offsets.Count < 2)
            return null;

        offsets.Sort();
        int stride = offsets[1] - offsets[0];
        for (var i = 2; i < offsets.Count; i++)
        {
            if (offsets[i] - offsets[i - 1] != stride)
                return null;
        }

        return stride;
    }

    private SlotState StateFor(int slot)
    {
        if (!_slots.TryGetValue(slot, out SlotState? state))
        {
            state = new SlotState();
            _slots[slot] = state;
        }

        return state;
    }

    /// <summary>
    /// Intersects the surviving set with the words that satisfy one transition.
    /// </summary>
    /// <remarks>
    /// The first event for a slot <i>establishes</i> the set rather than intersecting
    /// with everything, because "every word in the process" is not a set this type is
    /// given. From the second event on it can only shrink, which is what makes the
    /// hunt monotone: no evidence ever revives a word an earlier event eliminated.
    /// </remarks>
    private void Narrow(SlotState state, Func<int, bool> satisfies)
    {
        if (state.Candidates is null)
        {
            state.Candidates = [];
            foreach (int offset in _seen)
            {
                if (satisfies(offset))
                    state.Candidates.Add(offset);
            }

            return;
        }

        var doomed = new List<int>();
        foreach (int offset in state.Candidates)
        {
            if (!satisfies(offset))
                doomed.Add(offset);
        }

        foreach (int offset in doomed)
            state.Candidates.Remove(offset);
    }

    private sealed class SlotState
    {
        public HashSet<int>? Candidates;
        public int Readies;
        public int Uses;
    }
}
