namespace NosAi.Runtime.LowLevel;

/// <summary>What one confirmation attempt concluded, and why.</summary>
/// <param name="Confirmed">
/// True only when an observed effect matches what the intent claims. Never true
/// from the absence of a contrary signal — silence is <see cref="Confirmed"/> false
/// with a reason, not a pass.
/// </param>
/// <param name="Reason">
/// Null when confirmed. Otherwise names which of the two honest failures this is:
/// no source reported anything (<see cref="KeybindConfirmation.NoSourceReason"/>), or
/// every source reported and none of them moved
/// (<see cref="KeybindConfirmation.NoEffectReason"/>). The two are not the same
/// claim — one is "I don't know", the other is "I looked, and it did nothing" —
/// and treating them alike would let an unread instrument stand in for evidence.
/// </param>
public readonly record struct KeybindConfirmResult(bool Confirmed, string? Reason);

/// <summary>
/// Classifies a keybind's effect from what changed around one press, per
/// <c>docs/TASTI_E_BERSAGLIO.md</c> § 3. Pure: no client, no clock, no I/O — it is
/// handed the two readings a probe already took and says what they mean.
/// </summary>
/// <remarks>
/// <para>
/// <b>Confirmed follows from a value going the direction the intent claims, and
/// nothing else.</b> A consumable's own row in that table has two independent
/// tells — HP rising (a potion) and the inventory slot's count falling (any
/// consumable, and which one) — and either alone is proof, because they are
/// produced by different packets (<c>stat</c> and <c>ivn</c>) that do not always
/// arrive in the same window. A skill has one that is stronger than the other:
/// MP falling is consistent with a skill but the same packet reports every other
/// cause of MP loss, while <c>sr</c> naming a slot is a statement the client
/// makes about this specific key having done something, and this code treats it
/// that way — sufficient on its own, not merely corroborating.
/// </para>
/// <para>
/// <b>The two ways to fail are not the same failure, and a caller needs both
/// kept apart.</b> A reading that is <c>null</c> on either side of the press
/// means a source never spoke — <c>stat</c> did not arrive, <c>ivn</c> was not
/// decoded — and the honest answer is "unverified", the same as before the press
/// was attempted. A reading that is present on both sides and simply did not
/// move means the press was observed doing nothing: the slot may be empty, or
/// the catalogue's guess about this key may be wrong. Conflating the two would
/// let a probe that never actually watched anything report the same "nothing
/// happened" as one that watched carefully and saw nothing — which is exactly
/// the distinction <c>keybind_not_configured</c> and <c>keybind_not_confirmed</c>
/// already refuse to collapse one level up.
/// </para>
/// </remarks>
public static class KeybindConfirmation
{
    /// <summary>Neither source reported a usable pair of readings.</summary>
    public const string NoSourceReason = "keybind_confirm_no_source";

    /// <summary>Every available source reported, and none of them moved.</summary>
    public const string NoEffectReason = "keybind_confirm_no_effect_observed";

    /// <summary>
    /// A consumable is confirmed when HP rose (a potion) or the watched inventory
    /// slot's amount fell (any consumable). Either alone is enough; they come from
    /// different packets and are not expected to arrive together.
    /// </summary>
    public static KeybindConfirmResult ClassifyConsumable(
        int? beforeHp, int? afterHp, int? beforeSlotAmount, int? afterSlotAmount)
    {
        bool hpAvailable = beforeHp is not null && afterHp is not null;
        bool hpRose = hpAvailable && afterHp!.Value > beforeHp!.Value;

        bool slotAvailable = beforeSlotAmount is not null || afterSlotAmount is not null;
        bool slotFell = SlotFell(beforeSlotAmount, afterSlotAmount);

        if (hpRose || slotFell)
            return new KeybindConfirmResult(true, null);

        return new KeybindConfirmResult(
            false, hpAvailable || slotAvailable ? NoEffectReason : NoSourceReason);
    }

    /// <summary>
    /// Present-but-absent counts as a fall: the last unit of a consumable removes
    /// the slot from <c>ivn</c> entirely rather than reporting it at zero, and a
    /// rule that only compared two numbers would miss exactly the case where the
    /// press worked and used the last one.
    /// </summary>
    private static bool SlotFell(int? before, int? after)
    {
        if (before is { } b && after is { } a)
            return a < b;
        if (before is { } onlyBefore)
            return onlyBefore > 0; // present, then gone entirely.
        return false; // absent before: nothing to have fallen from.
    }

    /// <summary>
    /// A skill is confirmed when MP fell, or when the wire named any slot as
    /// having come off cooldown after the press — <c>sr</c> is the client's own
    /// statement that a skill went somewhere, and is trusted on its own rather
    /// than treated as needing the MP reading to agree with it.
    /// </summary>
    public static KeybindConfirmResult ClassifySkill(
        int? beforeMp, int? afterMp, bool skillReadyObservedAfterPress)
    {
        if (skillReadyObservedAfterPress)
            return new KeybindConfirmResult(true, null);

        bool mpAvailable = beforeMp is not null && afterMp is not null;
        if (mpAvailable && afterMp!.Value < beforeMp!.Value)
            return new KeybindConfirmResult(true, null);

        return new KeybindConfirmResult(false, mpAvailable ? NoEffectReason : NoSourceReason);
    }
}
