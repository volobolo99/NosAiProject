using NosAi.Runtime.Navigation;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Phase 3 of <c>docs/SPEC_ESTENSIONE_LAYOUT_MEMORIA.md</c>. The interesting tests
/// here are the ones about words that are <i>not</i> cooldowns: finding the right
/// word is easy, and excluding the words that merely look right is the whole job.
/// </summary>
public sealed class SkillCooldownFinderTests
{
    private const int Cooldown = 0x1000;   // the real one
    private const int AlwaysZero = 0x2000; // a word that is always zero
    private const int AlwaysBusy = 0x3000; // a word that is never zero
    private const int Noisy = 0x4000;      // a word that changes for its own reasons

    private static Dictionary<int, uint> Sample(uint cooldown, uint noisy = 1) => new()
    {
        [Cooldown] = cooldown,
        [AlwaysZero] = 0,
        [AlwaysBusy] = 77,
        [Noisy] = noisy
    };

    /// <summary>
    /// The happy path, and the shape every other test is a deviation from: running,
    /// zero at the restoration the wire announced, non-zero again when used, zero
    /// again at the second restoration.
    /// </summary>
    [Fact]
    public void A_word_that_falls_at_the_ready_and_rises_at_the_use_is_established()
    {
        var finder = new SkillCooldownFinder();

        finder.Observe(Sample(cooldown: 5000));
        finder.NoteReady(2, Sample(cooldown: 0, noisy: 9));
        finder.NoteUsed(2, Sample(cooldown: 4800, noisy: 3));
        finder.NoteReady(2, Sample(cooldown: 0, noisy: 6));

        SkillCooldownVerdict verdict = finder.Verdict(2);

        Assert.Equal(SkillCooldownOutcome.Established, verdict.Outcome);
        Assert.Null(verdict.Reason);
        SkillCooldownHit hit = Assert.Single(verdict.Survivors);
        Assert.Equal(Cooldown, hit.Offset);
        Assert.Equal(2, hit.Slot);
    }

    /// <summary>
    /// The word this oracle exists to exclude. It satisfies « zero when the skill is
    /// ready » at every single restoration, for free, forever — which is why a rule
    /// that only watched the falls would report it as the answer.
    /// </summary>
    [Fact]
    public void A_permanently_zero_word_is_excluded_by_the_rise_it_never_makes()
    {
        var finder = new SkillCooldownFinder();

        finder.Observe(Sample(cooldown: 5000));
        finder.NoteReady(2, Sample(cooldown: 0));
        finder.NoteUsed(2, Sample(cooldown: 4800));
        finder.NoteReady(2, Sample(cooldown: 0));

        SkillCooldownVerdict verdict = finder.Verdict(2);

        Assert.DoesNotContain(verdict.Survivors, h => h.Offset == AlwaysZero);
    }

    [Fact]
    public void A_word_that_is_never_zero_is_excluded_by_the_fall_it_never_makes()
    {
        var finder = new SkillCooldownFinder();

        finder.Observe(Sample(cooldown: 5000));
        finder.NoteReady(2, Sample(cooldown: 0));
        finder.NoteUsed(2, Sample(cooldown: 4800));
        finder.NoteReady(2, Sample(cooldown: 0));

        Assert.DoesNotContain(finder.Verdict(2).Survivors, h => h.Offset == AlwaysBusy);
    }

    /// <summary>
    /// One coincidence is not evidence. A word that happens to be non-zero and then
    /// zero across a single restoration passes the first event; the second is the one
    /// it cannot repeat.
    /// </summary>
    [Fact]
    public void A_word_that_matched_once_by_coincidence_does_not_survive_the_second_ready()
    {
        var finder = new SkillCooldownFinder();

        finder.Observe(Sample(cooldown: 5000, noisy: 42));
        finder.NoteReady(2, Sample(cooldown: 0, noisy: 0));   // Noisy also fell to zero
        finder.NoteUsed(2, Sample(cooldown: 4800, noisy: 7)); // and also rose
        finder.NoteReady(2, Sample(cooldown: 0, noisy: 5));   // but not this time

        SkillCooldownVerdict verdict = finder.Verdict(2);

        Assert.Equal(SkillCooldownOutcome.Established, verdict.Outcome);
        Assert.Equal(Cooldown, Assert.Single(verdict.Survivors).Offset);
    }

    [Fact]
    public void With_no_evidence_at_all_the_slot_is_undecided_and_says_so()
    {
        SkillCooldownVerdict verdict = new SkillCooldownFinder().Verdict(2);

        Assert.Equal(SkillCooldownOutcome.Undecided, verdict.Outcome);
        Assert.Equal(SkillCooldownFinder.NoEvidenceReason, verdict.Reason);
        Assert.Empty(verdict.Survivors);
    }

    /// <summary>
    /// A single restoration leaves the right word standing, and the verdict still
    /// refuses to call it established: the reason names exactly what is missing so
    /// the operator knows to use the skill again rather than to give up.
    /// </summary>
    [Fact]
    public void One_ready_is_not_enough_and_the_reason_counts_what_is_missing()
    {
        var finder = new SkillCooldownFinder();

        finder.Observe(Sample(cooldown: 5000));
        finder.NoteReady(2, Sample(cooldown: 0));

        SkillCooldownVerdict verdict = finder.Verdict(2);

        Assert.Equal(SkillCooldownOutcome.Undecided, verdict.Outcome);
        Assert.Contains(SkillCooldownFinder.NotEnoughEvidencePrefix, verdict.Reason!, StringComparison.Ordinal);
        Assert.Contains("readies=1/2", verdict.Reason!, StringComparison.Ordinal);
        Assert.Contains("uses=0/1", verdict.Reason!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Non-convergence is a result, not a failure to report. The phase closes with
    /// the cooldown UNKNOWN, which is honest; a wrong cooldown would make Ranking
    /// propose actions Verify then discovers failed one at a time.
    /// </summary>
    [Fact]
    public void When_nothing_behaves_like_a_cooldown_the_answer_is_no_candidate()
    {
        var finder = new SkillCooldownFinder();

        // The real word never falls: nothing in this run behaves like a cooldown.
        finder.Observe(Sample(cooldown: 5000));
        finder.NoteReady(2, Sample(cooldown: 4900));
        finder.NoteUsed(2, Sample(cooldown: 4800));
        finder.NoteReady(2, Sample(cooldown: 4700));

        SkillCooldownVerdict verdict = finder.Verdict(2);

        Assert.Equal(SkillCooldownOutcome.NoCandidate, verdict.Outcome);
        Assert.Equal(SkillCooldownFinder.NoCandidateReason, verdict.Reason);
        Assert.Empty(verdict.Survivors);
        Assert.Null(verdict.Single);
    }

    /// <summary>
    /// Two words that behave identically are two words, not an answer. Reporting
    /// either would be picking one by preference, which is what the disagreeing
    /// candidate chains already did.
    /// </summary>
    [Fact]
    public void Two_words_behaving_alike_are_ambiguous_and_counted()
    {
        const int Twin = 0x5000;
        var finder = new SkillCooldownFinder();

        Dictionary<int, uint> WithTwin(uint value) => new()
        {
            [Cooldown] = value,
            [Twin] = value,
            [AlwaysBusy] = 77
        };

        finder.Observe(WithTwin(5000));
        finder.NoteReady(2, WithTwin(0));
        finder.NoteUsed(2, WithTwin(4800));
        finder.NoteReady(2, WithTwin(0));

        SkillCooldownVerdict verdict = finder.Verdict(2);

        Assert.Equal(SkillCooldownOutcome.Ambiguous, verdict.Outcome);
        Assert.Equal(2, verdict.Survivors.Count);
        Assert.Contains("2", verdict.Reason!, StringComparison.Ordinal);
        Assert.Null(verdict.Single);
    }

    /// <summary>
    /// A restoration with no sample before it describes no transition, so it must
    /// narrow nothing rather than eliminate everything. Getting this wrong would
    /// turn a missing observation into a confident « no candidate ».
    /// </summary>
    [Fact]
    public void A_ready_with_no_prior_sample_narrows_nothing()
    {
        var finder = new SkillCooldownFinder();

        finder.NoteReady(2, Sample(cooldown: 0));

        SkillCooldownVerdict verdict = finder.Verdict(2);

        Assert.Equal(SkillCooldownOutcome.Undecided, verdict.Outcome);
        Assert.Contains("readies=0/2", verdict.Reason!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The stride the candidate map claims is 0x48. This reports what the surviving
    /// offsets actually show, so the claim can be checked against a measurement
    /// instead of standing in for one.
    /// </summary>
    [Fact]
    public void The_stride_between_established_slots_is_measured_not_assumed()
    {
        var finder = new SkillCooldownFinder();
        int[] offsets = [0x1000, 0x1048, 0x1090];

        RunSlots(finder, offsets);

        Assert.Equal(0x48, finder.ObservedStride());
    }

    /// <summary>
    /// Gaps that disagree are not a stride, and averaging them would manufacture a
    /// number nothing observed.
    /// </summary>
    [Fact]
    public void Disagreeing_gaps_produce_no_stride_rather_than_an_average()
    {
        var finder = new SkillCooldownFinder();
        int[] offsets = [0x1000, 0x1048, 0x1100];

        RunSlots(finder, offsets);

        Assert.Null(finder.ObservedStride());
    }

    [Fact]
    public void A_single_established_slot_is_not_a_stride()
    {
        var finder = new SkillCooldownFinder();
        RunSlots(finder, [0x1000]);

        Assert.Null(finder.ObservedStride());
    }

    /// <summary>
    /// Drives one full hunt per slot, where slot i's cooldown lives at offsets[i] and
    /// every other word stays put — so each slot establishes its own offset and the
    /// stride between them is whatever the offsets were.
    /// </summary>
    private static void RunSlots(SkillCooldownFinder finder, int[] offsets)
    {
        for (var slot = 0; slot < offsets.Length; slot++)
        {
            Dictionary<int, uint> At(uint value)
            {
                var sample = new Dictionary<int, uint>();
                for (var i = 0; i < offsets.Length; i++)
                    sample[offsets[i]] = i == slot ? value : 77;
                return sample;
            }

            finder.Observe(At(5000));
            finder.NoteReady(slot, At(0));
            finder.NoteUsed(slot, At(4800));
            finder.NoteReady(slot, At(0));
        }
    }
}
