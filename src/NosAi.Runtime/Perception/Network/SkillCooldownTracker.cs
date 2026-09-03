using System.Globalization;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Perception.Network;

/// <summary>Whether one skill can be used, and how that was decided.</summary>
/// <param name="Remaining">
/// Time left before the skill returns, or null while the cooldown's length has
/// never been observed. Null is not zero: it means unknown.
/// </param>
/// <param name="Measured">
/// How long this skill's cooldown lasted the last time it was watched from use
/// to restoration. It is the wire's own answer, not a table.
/// </param>
public readonly record struct SkillCooldownReading(
    int Slot,
    bool Ready,
    TimeSpan? Remaining,
    TimeSpan? Measured,
    DataSourceKind Source,
    string? FailureReason)
{
    public static SkillCooldownReading Unknown(int slot, string reason) =>
        new(slot, false, null, null, DataSourceKind.Unknown, reason);

    public string Describe() => Source == DataSourceKind.Unknown
        ? string.Create(CultureInfo.InvariantCulture, $"slot {Slot}: UNKNOWN ({FailureReason})")
        : Ready
            ? string.Create(CultureInfo.InvariantCulture, $"slot {Slot}: pronta [{Source.ToWire()}]")
            : string.Create(CultureInfo.InvariantCulture,
                $"slot {Slot}: in ricarica, {(Remaining is { } r ? $"{r.TotalSeconds:F1}s" : "durata ignota")} [{Source.ToWire()}]");
}

/// <summary>
/// Skill readiness taken from the wire instead of from the client's memory.
/// </summary>
/// <remarks>
/// <para>
/// Phase 3 spent a day hunting a cooldown word through 196 MB of process memory,
/// using the wire's <c>sr</c> packet as the referee for every candidate. The
/// referee was the answer. <c>sr</c> announces that a skill has become available
/// again, and the runtime knows when it used one, so the two ends of a cooldown
/// are both observable without reading the client at all.
/// </para>
/// <para>
/// <b>Why this is better and not merely quicker.</b> There is no address to find,
/// nothing to re-verify after a client restart, and nothing to redo after the
/// client is patched — the three costs that make a memory offset expensive to
/// own. It is also observation only, which is where ADR-0014 wants anything that
/// can stay there.
/// </para>
/// <para>
/// <b>What it can and cannot say.</b> Ready or not ready is decided the moment
/// the wire speaks. A remaining time exists only after one full use-to-restore
/// has been watched, because the length is measured rather than assumed; until
/// then <see cref="SkillCooldownReading.Remaining"/> is null, which is unknown
/// and not zero. A slot nobody has used is <c>UNKNOWN</c> with its reason, never
/// "ready" by default: guessing ready is how a Ranking proposes an action the
/// Verify then discovers it could not take.
/// </para>
/// <para>
/// The classification is <see cref="DataSourceKind.Derived"/>. Two live
/// observations — the use, and the restoration — produce a third fact that
/// neither states on its own.
/// </para>
/// </remarks>
public sealed class SkillCooldownTracker
{
    public const string NeverObservedReason = "skill_never_observed";
    public const string SlotImplausiblePrefix = "skill_slot_implausible";

    /// <summary>The highest slot the world decoder will admit.</summary>
    public const int MaxSlot = 255;

    private sealed record SlotState(DateTime? UsedAtUtc, DateTime? ReadyAtUtc, TimeSpan? Measured);

    private readonly Dictionary<int, SlotState> _slots = new();
    private readonly object _sync = new();

    /// <summary>Records that the runtime used this skill.</summary>
    /// <remarks>
    /// Called by whoever emitted the act, because that is who knows the slot: the
    /// wire's own <c>su</c> carries the skill's vnum rather than its slot, and
    /// nothing observed so far maps one to the other.
    /// </remarks>
    public void NoteUsed(int slot, DateTime atUtc)
    {
        if (slot < 0 || slot > MaxSlot)
            return;

        lock (_sync)
        {
            _slots.TryGetValue(slot, out SlotState? previous);
            _slots[slot] = new SlotState(atUtc, null, previous?.Measured);
        }
    }

    /// <summary>Records the wire's announcement that this skill is available again.</summary>
    public void NoteReady(int slot, DateTime atUtc)
    {
        if (slot < 0 || slot > MaxSlot)
            return;

        lock (_sync)
        {
            _slots.TryGetValue(slot, out SlotState? previous);

            // The length is only learned when the restoration follows a use this
            // tracker saw. An sr on its own still makes the skill ready; it just
            // measures nothing, and reporting a duration from it would be an
            // invention.
            TimeSpan? measured = previous?.Measured;
            if (previous?.UsedAtUtc is { } usedAt && atUtc > usedAt)
                measured = atUtc - usedAt;

            _slots[slot] = new SlotState(previous?.UsedAtUtc, atUtc, measured);
        }
    }

    /// <summary>What is known about this slot right now.</summary>
    public SkillCooldownReading Read(int slot, DateTime nowUtc)
    {
        if (slot < 0 || slot > MaxSlot)
        {
            return SkillCooldownReading.Unknown(
                slot, string.Create(CultureInfo.InvariantCulture, $"{SlotImplausiblePrefix}:{slot}"));
        }

        SlotState? state;
        lock (_sync)
        {
            if (!_slots.TryGetValue(slot, out state))
                return SkillCooldownReading.Unknown(slot, NeverObservedReason);
        }

        // Never used as far as this tracker saw, but the wire said it is ready.
        if (state.UsedAtUtc is not { } used)
        {
            return state.ReadyAtUtc is null
                ? SkillCooldownReading.Unknown(slot, NeverObservedReason)
                : new SkillCooldownReading(slot, true, null, state.Measured, DataSourceKind.Derived, null);
        }

        bool ready = state.ReadyAtUtc is { } readyAt && readyAt > used;
        if (ready)
            return new SkillCooldownReading(slot, true, null, state.Measured, DataSourceKind.Derived, null);

        // In cooldown. A remaining time is only offered when the length has been
        // watched before; otherwise the honest answer is that it is not ready and
        // how long is unknown.
        TimeSpan? remaining = null;
        if (state.Measured is { } length)
        {
            TimeSpan elapsed = nowUtc - used;
            remaining = elapsed >= length ? TimeSpan.Zero : length - elapsed;
        }

        return new SkillCooldownReading(slot, false, remaining, state.Measured, DataSourceKind.Derived, null);
    }

    /// <summary>Every slot this tracker has seen, in slot order.</summary>
    public IReadOnlyList<SkillCooldownReading> ReadAll(DateTime nowUtc)
    {
        int[] slots;
        lock (_sync)
            slots = _slots.Keys.ToArray();

        Array.Sort(slots);
        var readings = new List<SkillCooldownReading>(slots.Length);
        foreach (int slot in slots)
            readings.Add(Read(slot, nowUtc));

        return readings;
    }
}
