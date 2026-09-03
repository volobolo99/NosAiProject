using NosAi.Runtime.LowLevel;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The pure classification from <c>docs/TASTI_E_BERSAGLIO.md</c> § 3. The tests
/// that matter here are the ones that separate "I looked and nothing moved" from
/// "nothing ever reported" — both read as not-confirmed, but under two different,
/// nameable reasons.
/// </summary>
public sealed class KeybindConfirmationTests
{
    [Fact]
    public void A_falling_inventory_slot_confirms_a_consumable()
    {
        KeybindConfirmResult result = KeybindConfirmation.ClassifyConsumable(
            beforeHp: 5000, afterHp: 5000, beforeSlotAmount: 3, afterSlotAmount: 2);

        Assert.True(result.Confirmed);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void Rising_hp_alone_confirms_a_consumable_even_with_no_inventory_reading()
    {
        KeybindConfirmResult result = KeybindConfirmation.ClassifyConsumable(
            beforeHp: 5000, afterHp: 5300, beforeSlotAmount: null, afterSlotAmount: null);

        Assert.True(result.Confirmed);
    }

    /// <summary>
    /// The last unit does not report the slot at zero — <c>ivn</c> drops it
    /// entirely — so "present, then absent" has to read as a fall, not as two
    /// unrelated readings that happen not to compare.
    /// </summary>
    [Fact]
    public void A_slot_that_disappears_entirely_counts_as_the_last_one_being_used()
    {
        KeybindConfirmResult result = KeybindConfirmation.ClassifyConsumable(
            beforeHp: null, afterHp: null, beforeSlotAmount: 1, afterSlotAmount: null);

        Assert.True(result.Confirmed);
    }

    [Fact]
    public void A_slot_absent_both_times_is_not_evidence_of_anything()
    {
        KeybindConfirmResult result = KeybindConfirmation.ClassifyConsumable(
            beforeHp: null, afterHp: null, beforeSlotAmount: null, afterSlotAmount: null);

        Assert.False(result.Confirmed);
        Assert.Equal(KeybindConfirmation.NoSourceReason, result.Reason);
    }

    /// <summary>
    /// Both sources answered, and neither moved: the press was watched and did
    /// nothing, which is a different claim from never having watched at all.
    /// </summary>
    [Fact]
    public void Unchanged_readings_from_a_reporting_source_are_no_effect_not_no_source()
    {
        KeybindConfirmResult result = KeybindConfirmation.ClassifyConsumable(
            beforeHp: 5000, afterHp: 5000, beforeSlotAmount: 3, afterSlotAmount: 3);

        Assert.False(result.Confirmed);
        Assert.Equal(KeybindConfirmation.NoEffectReason, result.Reason);
    }

    [Fact]
    public void An_inventory_slot_that_rose_is_not_a_consumable_press()
    {
        KeybindConfirmResult result = KeybindConfirmation.ClassifyConsumable(
            beforeHp: null, afterHp: null, beforeSlotAmount: 2, afterSlotAmount: 3);

        Assert.False(result.Confirmed);
        Assert.Equal(KeybindConfirmation.NoEffectReason, result.Reason);
    }

    [Fact]
    public void Falling_mp_confirms_a_skill()
    {
        KeybindConfirmResult result = KeybindConfirmation.ClassifySkill(
            beforeMp: 1400, afterMp: 1120, skillReadyObservedAfterPress: false);

        Assert.True(result.Confirmed);
        Assert.Null(result.Reason);
    }

    /// <summary>
    /// <c>sr</c> is trusted alone. It is the client's own statement that this key
    /// put something on cooldown, so it does not need the MP reading to agree —
    /// unlike <see cref="SkillCooldownFinder"/>, which needs both directions to
    /// separate a cooldown from noise, this only needs one positive statement to
    /// confirm the key itself does something skill-shaped.
    /// </summary>
    [Fact]
    public void A_skill_ready_event_confirms_a_skill_even_with_mp_unavailable()
    {
        KeybindConfirmResult result = KeybindConfirmation.ClassifySkill(
            beforeMp: null, afterMp: null, skillReadyObservedAfterPress: true);

        Assert.True(result.Confirmed);
    }

    [Fact]
    public void No_mp_reading_and_no_skill_ready_event_is_no_source()
    {
        KeybindConfirmResult result = KeybindConfirmation.ClassifySkill(
            beforeMp: null, afterMp: null, skillReadyObservedAfterPress: false);

        Assert.False(result.Confirmed);
        Assert.Equal(KeybindConfirmation.NoSourceReason, result.Reason);
    }

    [Fact]
    public void Steady_mp_with_no_skill_ready_event_is_no_effect()
    {
        KeybindConfirmResult result = KeybindConfirmation.ClassifySkill(
            beforeMp: 1400, afterMp: 1400, skillReadyObservedAfterPress: false);

        Assert.False(result.Confirmed);
        Assert.Equal(KeybindConfirmation.NoEffectReason, result.Reason);
    }

    [Fact]
    public void Rising_mp_is_not_a_skill_press()
    {
        KeybindConfirmResult result = KeybindConfirmation.ClassifySkill(
            beforeMp: 1000, afterMp: 1200, skillReadyObservedAfterPress: false);

        Assert.False(result.Confirmed);
        Assert.Equal(KeybindConfirmation.NoEffectReason, result.Reason);
    }
}
