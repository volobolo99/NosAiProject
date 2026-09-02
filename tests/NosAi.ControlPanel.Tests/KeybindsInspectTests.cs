using System.IO;
using NosAi.ControlPanel;
using Xunit;

namespace NosAi.ControlPanel.Tests;

/// <summary>
/// Keybind row: a covered file, an empty map, and an unreadable file. A missing
/// intent is named; the inspect never writes the operator's file.
/// </summary>
public sealed class KeybindsInspectTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "nosai-panel-keybinds-" + Guid.NewGuid().ToString("N"));

    public KeybindsInspectTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void ACoveredFileShowsConfiguredIntentsAndNoMissingRows()
    {
        string path = Write("""
            {
              "version": 1,
              "binds": {
                "consumable.0": { "virtualKey": 49, "label": "1" },
                "skill.0": { "virtualKey": 112, "label": "F1" }
              }
            }
            """);

        KeybindsView view = KeybindsInspect.Inspect(path);

        Assert.True(view.FileExists);
        Assert.Null(view.LoadFailure);
        Assert.Contains(view.Fields, f => f.Label == "consumable.0" && f.Value.Contains("vk=49", StringComparison.Ordinal));
        Assert.Contains(view.Fields, f => f.Label == "skill.0" && f.Value.Contains("vk=112", StringComparison.Ordinal));
        Assert.DoesNotContain(view.Fields, f => f.Value == KeybindsInspect.MissingLabel);
        Assert.DoesNotContain(view.Fields, f => string.IsNullOrWhiteSpace(f.Label) || string.IsNullOrWhiteSpace(f.Value));
    }

    [Fact]
    public void AnEmptyMapNamesTheUncoveredPrefixesAsMissing()
    {
        string path = Write("""
            {
              "version": 1,
              "binds": {}
            }
            """);

        KeybindsView view = KeybindsInspect.Inspect(path);

        Assert.True(view.FileExists);
        Assert.Null(view.LoadFailure);
        DisplayField consumable = view.Fields.Single(f => f.Label == "consumable.*");
        DisplayField skill = view.Fields.Single(f => f.Label == "skill.*");
        Assert.Equal(KeybindsInspect.MissingLabel, consumable.Value);
        Assert.Equal(KeybindsInspect.MissingLabel, skill.Value);
        Assert.Equal("DERIVED", consumable.Source);
        Assert.DoesNotContain("UNKNOWN", consumable.Value, StringComparison.Ordinal);
        Assert.DoesNotContain(view.Fields, f => string.IsNullOrWhiteSpace(f.Value));
    }

    [Fact]
    public void AMissingFileIsUnknownAndStillNamesTheMissingIntents()
    {
        string path = Path.Combine(_dir, "absent.json");
        KeybindsView view = KeybindsInspect.Inspect(path);

        Assert.False(view.FileExists);
        Assert.Equal("file_not_found", view.LoadFailure);
        Assert.Contains(view.Fields, f => f.Label == "File tasti" && f.Source == "UNKNOWN"
            && f.Value.Contains("file_not_found", StringComparison.Ordinal));
        Assert.Contains(view.Fields, f => f.Label == "consumable.*" && f.Value == KeybindsInspect.MissingLabel);
        Assert.Contains(view.Fields, f => f.Label == "skill.*" && f.Value == KeybindsInspect.MissingLabel);
        Assert.DoesNotContain(view.Fields, f => string.IsNullOrWhiteSpace(f.Label) || string.IsNullOrWhiteSpace(f.Value));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void AMalformedFileIsUnknownAndNamesTheMissingIntents()
    {
        string path = Write("{");
        KeybindsView view = KeybindsInspect.Inspect(path);

        Assert.True(view.FileExists);
        Assert.Equal("json_malformed", view.LoadFailure);
        Assert.Equal("UNKNOWN", view.Fields.Single(f => f.Label == "File tasti").Source);
        Assert.Contains("json_malformed", view.Fields.Single(f => f.Label == "File tasti").Value, StringComparison.Ordinal);
        Assert.Contains(view.Fields, f => f.Label == "consumable.*" && f.Value == KeybindsInspect.MissingLabel && f.Source == "UNKNOWN");
        Assert.Contains(view.Fields, f => f.Label == "skill.*" && f.Value == KeybindsInspect.MissingLabel);
    }

    [Fact]
    public void InspectDoesNotCreateTheFile()
    {
        string path = Path.Combine(_dir, "uncreated.json");
        KeybindsInspect.Inspect(path);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void TheViewHasNoWritePathIntoTheRuntime()
    {
        string source = File.ReadAllText(Path.Combine(
            SurroundingsInspectTests.RepositoryRoot(), "src", "NosAi.ControlPanel", "KeybindsInspect.cs"));
        SurroundingsInspectTests.AssertNoWrite(source);
        Assert.DoesNotContain("File.Create", source, StringComparison.Ordinal);
        Assert.DoesNotContain("KeybindMap.TryLoad", source, StringComparison.Ordinal);
    }

    private string Write(string json)
    {
        string path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        return path;
    }
}
