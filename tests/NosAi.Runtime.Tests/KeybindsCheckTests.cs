using NosAi.Runtime.Gate3;
using NosAi.Runtime.LowLevel;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// <c>--keybinds-check</c> (C3-1): the expected path, whether the file is there,
/// the configured binds, and the runtime prefixes that are not covered.
/// </summary>
public sealed class KeybindsCheckTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(),
        "nosai-keybinds-check-" + Guid.NewGuid().ToString("N"));

    public KeybindsCheckTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void TheRuntimeWiresTheKeybindsCheckFlag()
    {
        string root = RepositoryRoot();
        string program = File.ReadAllText(Path.Combine(root, "src", "NosAi.Runtime", "Program.cs"));
        Assert.Contains("--keybinds-check", program, StringComparison.Ordinal);
        Assert.Contains("KeybindsCheck.Run", program, StringComparison.Ordinal);
        Assert.Contains("\"--keybinds-check\"", program, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRelativePathMatchesWhatTheEffectorLoads()
    {
        Assert.Equal(InputActionEffector.KeybindsRelativePath, KeybindMap.RelativePath);
    }

    [Fact]
    public void ThePrefixesAreTheOnesTheEffectorConstructs()
    {
        string effector = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "NosAi.Runtime", "Gate3", "InputActionEffector.cs"));
        Assert.Contains("$\"consumable.{slot.Slot}\"", effector, StringComparison.Ordinal);
        Assert.Contains("$\"skill.{candidate.SkillOrItemId}\"", effector, StringComparison.Ordinal);
        Assert.Equal("consumable.", KeybindsCheck.ConsumablePrefix);
        Assert.Equal("skill.", KeybindsCheck.SkillPrefix);
    }

    [Fact]
    public void MissingFileIsNonZeroAndListsBothPrefixesUncovered()
    {
        string path = Path.Combine(_dir, "absent.json");
        KeybindsCheckReport report = KeybindsCheck.Inspect(path);
        string text = KeybindsCheck.Format(report);

        Assert.False(report.Ok);
        Assert.False(report.Exists);
        Assert.Equal("file_not_found", report.LoadFailure);
        Assert.Empty(report.Configured);
        Assert.Equal(["consumable.*", "skill.*"], report.UncoveredPrefixes);
        Assert.Contains(path, text, StringComparison.Ordinal);
        Assert.Contains("exists:  false", text, StringComparison.Ordinal);
        Assert.Contains("configured: (none)", text, StringComparison.Ordinal);
        Assert.Contains("uncovered (heal then attack):", text, StringComparison.Ordinal);
        int heal = text.IndexOf("consumable.*", StringComparison.Ordinal);
        int attack = text.IndexOf("skill.*", StringComparison.Ordinal);
        Assert.True(heal >= 0 && attack > heal, "planner order is heal then attack");
        Assert.Equal(1, KeybindsCheck.Run(path));
    }

    [Fact]
    public void ValidFileCoveringBothPrefixesIsZero()
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

        KeybindsCheckReport report = KeybindsCheck.Inspect(path);
        string text = KeybindsCheck.Format(report);

        Assert.True(report.Ok);
        Assert.True(report.Exists);
        Assert.Null(report.LoadFailure);
        Assert.Equal(2, report.Configured.Count);
        Assert.Empty(report.UncoveredPrefixes);
        Assert.Contains("consumable.0  vk=49  label=1", text, StringComparison.Ordinal);
        Assert.Contains("skill.0  vk=112  label=F1", text, StringComparison.Ordinal);
        Assert.Contains("uncovered: (none)", text, StringComparison.Ordinal);
        Assert.Equal(0, KeybindsCheck.Run(path));
    }

    [Fact]
    public void AMissingRequiredPrefixIsNonZero()
    {
        string path = Write("""
            {
              "version": 1,
              "binds": {
                "consumable.4": { "virtualKey": 49, "label": "1" }
              }
            }
            """);

        KeybindsCheckReport report = KeybindsCheck.Inspect(path);
        string text = KeybindsCheck.Format(report);

        Assert.False(report.Ok);
        Assert.True(report.Exists);
        Assert.Single(report.Configured);
        Assert.Equal(["skill.*"], report.UncoveredPrefixes);
        Assert.Contains("consumable.4", text, StringComparison.Ordinal);
        Assert.Contains("skill.*", text, StringComparison.Ordinal);
        Assert.DoesNotContain("consumable.*", text, StringComparison.Ordinal);
        Assert.Equal(1, KeybindsCheck.Run(path));
    }

    [Fact]
    public void AnExtraIntentDoesNotCoverARuntimePrefix()
    {
        string path = Write("""
            {
              "version": 1,
              "binds": {
                "potion.hp": { "virtualKey": 49, "label": "1" },
                "attack.basic": { "virtualKey": 32, "label": "Space" }
              }
            }
            """);

        KeybindsCheckReport report = KeybindsCheck.Inspect(path);

        Assert.False(report.Ok);
        Assert.Equal(2, report.Configured.Count);
        Assert.Equal(["consumable.*", "skill.*"], report.UncoveredPrefixes);
    }

    [Fact]
    public void TheExampleFileInTheRepositoryParsesAndCoversTheRuntimePrefixes()
    {
        string path = Path.Combine(RepositoryRoot(), "data", "keybinds.example.json");
        Assert.True(File.Exists(path));

        KeybindsCheckReport report = KeybindsCheck.Inspect(path);
        Assert.True(report.Ok);
        Assert.True(KeybindMap.TryLoad(path, out _, out string? reason));
        Assert.Null(reason);
    }

    [Fact]
    public void InspectDoesNotCreateTheFile()
    {
        string path = Path.Combine(_dir, "uncreated.json");
        KeybindsCheck.Inspect(path);
        Assert.False(File.Exists(path));
    }

    private string Write(string json)
    {
        string path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        return path;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NosAi.sln")))
            directory = directory.Parent;
        Assert.True(directory is not null, "Repository root not found.");
        return directory!.FullName;
    }
}
