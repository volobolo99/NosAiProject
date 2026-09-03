using NosAi.Runtime.LowLevel;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The parts of the keybind-confirmation probe that need no client: reading HP,
/// MP and one inventory slot out of the runtime's own JSON, spotting a skillsReady
/// event after a given instant, and rewriting <c>keybinds.json</c> as an
/// all-or-nothing operation.
/// </summary>
public sealed class KeybindConfirmProbeTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "nosai-keybind-confirm-" + Guid.NewGuid().ToString("N"));

    public KeybindConfirmProbeTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static string Wire(string body) => $$"""{ "gameplayBaseline": {{body}} }""";

    [Fact]
    public void Hp_mp_and_the_wanted_slot_are_read_from_one_body()
    {
        string json = Wire("""
            {
              "hp": { "value": 4200 },
              "mp": { "value": 900 },
              "inventory": { "value": [
                { "slot": 1, "amount": 3 },
                { "slot": 4, "amount": 7 }
              ] }
            }
            """);

        KeybindConfirmProbe.TryReadVitalsAndSlot(json, slot: 1, out int? hp, out int? mp, out int? slotAmount);

        Assert.Equal(4200, hp);
        Assert.Equal(900, mp);
        Assert.Equal(3, slotAmount);
    }

    [Fact]
    public void A_slot_not_present_in_inventory_reads_as_no_reading()
    {
        string json = Wire("""{ "inventory": { "value": [ { "slot": 4, "amount": 7 } ] } }""");

        KeybindConfirmProbe.TryReadVitalsAndSlot(json, slot: 1, out _, out _, out int? slotAmount);

        Assert.Null(slotAmount);
    }

    /// <summary>
    /// UNKNOWN on the wire is not zero. A vital the wire has not classified reads
    /// as no reading, never as the number that happens to sit in a default.
    /// </summary>
    [Fact]
    public void An_unknown_hp_reads_as_no_reading_not_as_zero()
    {
        string json = Wire("""{ "hp": { "value": null, "failureReason": "no_stat_observed" } }""");

        KeybindConfirmProbe.TryReadVitalsAndSlot(json, slot: null, out int? hp, out _, out _);

        Assert.Null(hp);
    }

    [Fact]
    public void No_slot_requested_means_inventory_is_not_read_at_all()
    {
        string json = Wire("""{ "inventory": { "value": [ { "slot": 1, "amount": 3 } ] } }""");

        KeybindConfirmProbe.TryReadVitalsAndSlot(json, slot: null, out _, out _, out int? slotAmount);

        Assert.Null(slotAmount);
    }

    [Fact]
    public void A_skill_ready_event_strictly_after_the_instant_counts()
    {
        var pressedAt = new DateTime(2026, 9, 3, 10, 0, 0, DateTimeKind.Utc);
        string json = Wire("""
            { "skillsReady": { "value": [ { "slot": 2, "observedAtUtc": "2026-09-03T10:00:01.000Z" } ] } }
            """);

        Assert.True(KeybindConfirmProbe.AnySkillReadyAfter(json, pressedAt));
    }

    /// <summary>
    /// A cooldown seen before the press proves nothing about this press — it is
    /// the same trap a stale sample would be in <see cref="Navigation.SkillCooldownFinder"/>.
    /// </summary>
    [Fact]
    public void A_skill_ready_event_at_or_before_the_instant_does_not_count()
    {
        var pressedAt = new DateTime(2026, 9, 3, 10, 0, 0, DateTimeKind.Utc);
        string json = Wire("""
            { "skillsReady": { "value": [ { "slot": 2, "observedAtUtc": "2026-09-03T10:00:00.000Z" } ] } }
            """);

        Assert.False(KeybindConfirmProbe.AnySkillReadyAfter(json, pressedAt));
    }

    [Fact]
    public void No_skills_ready_published_at_all_is_false_not_a_throw()
    {
        Assert.False(KeybindConfirmProbe.AnySkillReadyAfter(Wire("{}"), DateTime.UtcNow));
        Assert.False(KeybindConfirmProbe.AnySkillReadyAfter("{ not json", DateTime.UtcNow));
    }

    private const string TwoBinds = """
        {
          "version": 1,
          "_readme": ["kept as-is"],
          "binds": {
            "consumable.1": { "virtualKey": 49, "label": "1", "confirmed": false },
            "skill.201":    { "virtualKey": 50, "label": "2", "confirmed": false }
          }
        }
        """;

    private string WriteKeybinds(string json)
    {
        string path = Path.Combine(_dir, "keybinds.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Confirming_one_intent_leaves_the_other_and_the_readme_untouched()
    {
        string path = WriteKeybinds(TwoBinds);

        Assert.True(KeybindConfirmProbe.TryWriteConfirmed(path, ["consumable.1"], out string? failure));
        Assert.Null(failure);

        Assert.True(KeybindMap.TryLoad(path, out KeybindMap map, out _));
        Assert.True(map.TryGet("consumable.1", out Keybind consumable));
        Assert.True(consumable.Confirmed);
        Assert.True(map.TryGet("skill.201", out Keybind skill));
        Assert.False(skill.Confirmed);
        Assert.Contains("kept as-is", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void Confirming_several_intents_at_once_sets_all_of_them()
    {
        string path = WriteKeybinds(TwoBinds);

        Assert.True(KeybindProbeWriteBoth(path));

        Assert.True(KeybindMap.TryLoad(path, out KeybindMap map, out _));
        Assert.True(map.TryGet("consumable.1", out Keybind consumable));
        Assert.True(map.TryGet("skill.201", out Keybind skill));
        Assert.True(consumable.Confirmed);
        Assert.True(skill.Confirmed);
    }

    private static bool KeybindProbeWriteBoth(string path)
        => KeybindConfirmProbe.TryWriteConfirmed(path, ["consumable.1", "skill.201"], out _);

    /// <summary>
    /// One missing intent refuses the whole write. Confirming three of four and
    /// silently dropping the fourth would report success for something that only
    /// partly happened.
    /// </summary>
    [Fact]
    public void An_intent_absent_from_the_file_refuses_the_whole_write()
    {
        string path = WriteKeybinds(TwoBinds);

        Assert.False(KeybindConfirmProbe.TryWriteConfirmed(
            path, ["consumable.1", "skill.999"], out string? failure));
        Assert.Contains("skill.999", failure, StringComparison.Ordinal);

        Assert.True(KeybindMap.TryLoad(path, out KeybindMap map, out _));
        Assert.True(map.TryGet("consumable.1", out Keybind consumable));
        Assert.False(consumable.Confirmed);
    }
}
