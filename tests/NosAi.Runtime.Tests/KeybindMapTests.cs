using NosAi.Runtime.LowLevel;
using Xunit;

namespace NosAi.Runtime.Tests;

public sealed class KeybindMapTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "nosai-keybinds-" + Guid.NewGuid().ToString("N"));

    public KeybindMapTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Version_other_than_one_is_refused()
    {
        string path = Write("""
            { "version": 2, "binds": { "potion.hp": { "virtualKey": 49, "label": "1" } } }
            """);

        Assert.False(KeybindMap.TryLoad(path, out _, out string? reason));
        Assert.False(string.IsNullOrWhiteSpace(reason));
        Assert.Contains("2", reason, StringComparison.Ordinal);
    }

    /// <remarks>
    /// The name breaks this file's convention on purpose:
    /// <c>scripts/verifica-obiettivo.ps1</c> searches for it literally to know
    /// that C3 was done.
    /// </remarks>
    [Fact]
    public void MissingFileIsRefusedNotEmpty()
    {
        string path = Path.Combine(_dir, "absent.json");

        Assert.False(KeybindMap.TryLoad(path, out KeybindMap map, out string? reason));
        Assert.Equal("file_not_found", reason);
        Assert.False(map.TryGet("potion.hp", out _));
        Assert.Empty(map.ConfiguredIntents);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Empty_file_is_not_a_missing_file()
    {
        string path = Write("");

        Assert.False(KeybindMap.TryLoad(path, out _, out string? reason));
        Assert.Equal("file_empty", reason);
        Assert.NotEqual("file_not_found", reason);
    }

    [Fact]
    public void Malformed_json_is_refused_without_throwing()
    {
        string path = Write("{ version: 1,");

        Assert.False(KeybindMap.TryLoad(path, out _, out string? reason));
        Assert.Equal("json_malformed", reason);
    }

    [Fact]
    public void Virtual_key_outside_one_to_254_fails_the_whole_load()
    {
        string path = Write("""
            { "version": 1, "binds": { "potion.hp": { "virtualKey": 0, "label": "1" } } }
            """);

        Assert.False(KeybindMap.TryLoad(path, out KeybindMap map, out string? reason));
        Assert.Contains("virtual_key_out_of_range", reason, StringComparison.Ordinal);
        Assert.False(map.TryGet("potion.hp", out _));
    }

    [Fact]
    public void Duplicate_intent_is_refused_rather_than_keeping_the_last()
    {
        string path = Write("""
            {
              "version": 1,
              "binds": {
                "potion.hp": { "virtualKey": 49, "label": "1" },
                "potion.hp": { "virtualKey": 50, "label": "2" }
              }
            }
            """);

        Assert.False(KeybindMap.TryLoad(path, out KeybindMap map, out string? reason));
        Assert.Contains("duplicate_intent", reason, StringComparison.Ordinal);
        Assert.False(map.TryGet("potion.hp", out _));
    }

    [Fact]
    public void TryGet_on_an_unconfigured_intent_returns_false_and_default()
    {
        Assert.True(KeybindMap.TryLoad(Write(ValidFourBinds), out KeybindMap map, out _));

        Assert.False(map.TryGet("skill.9", out Keybind bind));
        Assert.Equal(default, bind);
    }

    [Fact]
    public void Empty_has_no_intents_and_never_resolves()
    {
        Assert.Empty(KeybindMap.Empty.ConfiguredIntents);
        Assert.False(KeybindMap.Empty.TryGet("potion.hp", out Keybind bind));
        Assert.Equal(default, bind);
    }

    [Fact]
    public void TryLoad_does_not_write_the_file()
    {
        string path = Path.Combine(_dir, "uncreated.json");
        KeybindMap.TryLoad(path, out _, out _);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Valid_file_with_four_binds_is_readable()
    {
        Assert.True(KeybindMap.TryLoad(Write(ValidFourBinds), out KeybindMap map, out string? reason));
        Assert.Null(reason);

        Assert.Equal(4, map.ConfiguredIntents.Count);
        Assert.Equal(["attack.basic", "potion.hp", "potion.mp", "skill.1"], map.ConfiguredIntents);

        Assert.True(map.TryGet("potion.hp", out Keybind hp));
        Assert.Equal(49, hp.VirtualKey);
        Assert.Equal("1", hp.Label);
        Assert.True(map.TryGet("potion.mp", out Keybind mp));
        Assert.Equal(50, mp.VirtualKey);
        Assert.True(map.TryGet("attack.basic", out Keybind attack));
        Assert.Equal(32, attack.VirtualKey);
        Assert.True(map.TryGet("skill.1", out Keybind skill));
        Assert.Equal(112, skill.VirtualKey);
        Assert.Equal("F1", skill.Label);
    }

    private string Write(string json)
    {
        string path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        return path;
    }

    private const string ValidFourBinds = """
        {
          "version": 1,
          "binds": {
            "potion.hp":    { "virtualKey": 49, "label": "1" },
            "potion.mp":    { "virtualKey": 50, "label": "2" },
            "attack.basic": { "virtualKey": 32, "label": "Space" },
            "skill.1":      { "virtualKey": 112, "label": "F1" }
          }
        }
        """;
}
