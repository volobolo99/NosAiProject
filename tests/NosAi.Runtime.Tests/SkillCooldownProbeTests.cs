using NosAi.Runtime.Navigation;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The parts of the phase 3 probe that do not need a client: reading the wire's
/// announcement out of the runtime's own JSON, and turning a window of bytes into
/// the word map the finder consumes.
/// </summary>
public sealed class SkillCooldownProbeTests
{
    private static string Wire(string skillsReady) =>
        $$"""{ "gameplayBaseline": { "skillsReady": {{skillsReady}} } }""";

    [Fact]
    public void The_slot_the_wire_named_is_read_with_its_moment()
    {
        string json = Wire("""
            { "value": [ { "slot": 2, "observedAtUtc": "2026-09-03T08:15:04.5000000Z" } ] }
            """);

        Assert.True(SkillCooldownProbe.TryReadSkillReady(json, 2, out DateTime at, out string? reason));
        Assert.Null(reason);
        Assert.Equal(DateTimeKind.Utc, at.Kind);
        Assert.Equal(new DateTime(2026, 9, 3, 8, 15, 4, 500, DateTimeKind.Utc), at);
    }

    /// <summary>
    /// The hunt waits for an announcement <i>later</i> than the last one it saw, so
    /// the latest moment is the only one that can end a wait. Returning the first
    /// entry would let a stale announcement close a round that never happened.
    /// </summary>
    [Fact]
    public void When_a_slot_was_announced_more_than_once_the_latest_moment_wins()
    {
        string json = Wire("""
            { "value": [
                { "slot": 2, "observedAtUtc": "2026-09-03T08:15:04.0000000Z" },
                { "slot": 2, "observedAtUtc": "2026-09-03T08:19:41.0000000Z" },
                { "slot": 2, "observedAtUtc": "2026-09-03T08:17:12.0000000Z" }
            ] }
            """);

        Assert.True(SkillCooldownProbe.TryReadSkillReady(json, 2, out DateTime at, out _));
        Assert.Equal(new DateTime(2026, 9, 3, 8, 19, 41, DateTimeKind.Utc), at);
    }

    [Fact]
    public void Another_slots_announcement_is_not_this_slots()
    {
        string json = Wire("""
            { "value": [ { "slot": 6, "observedAtUtc": "2026-09-03T08:15:04.0000000Z" } ] }
            """);

        Assert.False(SkillCooldownProbe.TryReadSkillReady(json, 2, out _, out string? reason));
        Assert.Equal(SkillCooldownProbe.SlotNeverAnnouncedReason, reason);
    }

    /// <summary>
    /// No provider is a different condition from a provider that has not heard this
    /// slot, and the operator acts on them differently: one means start the observer,
    /// the other means use the skill.
    /// </summary>
    [Fact]
    public void A_runtime_without_a_gameplay_provider_says_so_by_its_own_name()
    {
        Assert.False(SkillCooldownProbe.TryReadSkillReady("""{ "runtimeStatus": "ok" }""", 2, out _, out string? reason));
        Assert.Equal("gameplay_provider_not_available", reason);
    }

    [Fact]
    public void An_unreadable_body_is_a_named_failure_not_an_exception()
    {
        Assert.False(SkillCooldownProbe.TryReadSkillReady("{ not json", 2, out _, out string? reason));
        Assert.Equal("wire_json_malformed", reason);
    }

    /// <summary>
    /// UNKNOWN on the wire side is not an announcement. A provider that publishes
    /// skillsReady with no value has heard nothing, and reading that as a moment
    /// would end a wait the client never satisfied.
    /// </summary>
    [Fact]
    public void An_unknown_skills_ready_is_not_an_announcement()
    {
        string json = Wire("""{ "failureReason": "no_skill_ready_observed" }""");

        Assert.False(SkillCooldownProbe.TryReadSkillReady(json, 2, out _, out string? reason));
        Assert.Equal(SkillCooldownProbe.SlotNeverAnnouncedReason, reason);
    }

    [Fact]
    public void Bytes_become_one_word_every_four_at_their_own_distance()
    {
        byte[] bytes = [0x01, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00];

        Dictionary<int, uint> words = SkillCooldownProbe.WordsFrom(bytes);

        Assert.Equal(2, words.Count);
        Assert.Equal(1u, words[0]);
        Assert.Equal(0xFFFFu, words[4]);
    }

    /// <summary>
    /// Two windows read from two different bases go into one map, so their offsets
    /// must not collide — the second is shifted past the first.
    /// </summary>
    [Fact]
    public void A_second_window_is_shifted_so_the_two_cannot_collide()
    {
        byte[] bytes = [0x07, 0x00, 0x00, 0x00];

        Dictionary<int, uint> first = SkillCooldownProbe.WordsFrom(bytes);
        Dictionary<int, uint> second = SkillCooldownProbe.WordsFrom(bytes, SkillCooldownProbe.WindowBytes);

        Assert.Equal(7u, first[0]);
        Assert.Equal(7u, second[SkillCooldownProbe.WindowBytes]);
        Assert.DoesNotContain(0, second.Keys);
    }

    /// <summary>
    /// A trailing byte that cannot make a whole word is dropped rather than padded.
    /// Padding it would invent three zero bytes and hand the finder a word the client
    /// never held.
    /// </summary>
    [Fact]
    public void A_partial_trailing_word_is_dropped_not_padded()
    {
        byte[] bytes = [0x01, 0x00, 0x00, 0x00, 0xAA];

        Dictionary<int, uint> words = SkillCooldownProbe.WordsFrom(bytes);

        Assert.Single(words);
        Assert.Equal(1u, words[0]);
    }
}
