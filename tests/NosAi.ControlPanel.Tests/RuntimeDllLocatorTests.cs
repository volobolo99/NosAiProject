using System.IO;
using Xunit;

namespace NosAi.ControlPanel.Tests;

/// <summary>
/// The panel used to launch whichever copy of the runtime was nearest. The copy
/// next to a WPF panel has no <c>System.Security.Cryptography.ProtectedData</c>,
/// so Gate 1 reported four failures on a runtime that was healthy.
/// </summary>
public sealed class RuntimeDllLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nosai-locator-" + Guid.NewGuid().ToString("N"));

    private string BuiltDirectory => Path.Combine(_root, "repo", "src", "NosAi.Runtime", "bin", "Release", "net8.0-windows");
    private string PanelDirectory => Path.Combine(_root, "panel");
    private string RepoRoot => Path.Combine(_root, "repo");

    private void Place(string directory, params string[] files)
    {
        Directory.CreateDirectory(directory);
        foreach (var file in files)
            File.WriteAllText(Path.Combine(directory, file), "");
    }

    [Fact]
    public void An_incomplete_copy_next_to_the_panel_is_passed_over_with_a_reason()
    {
        Place(PanelDirectory, "NosAi.Runtime.dll");
        Place(BuiltDirectory, "NosAi.Runtime.dll", RuntimeDllLocator.RequiredCompanion);

        var resolved = RuntimeDllLocator.Resolve(RepoRoot, PanelDirectory, out string? reason);

        Assert.Equal(Path.Combine(BuiltDirectory, "NosAi.Runtime.dll"), resolved);
        Assert.NotNull(reason);
        Assert.Contains(RuntimeDllLocator.RequiredCompanion, reason);
    }

    [Fact]
    public void A_complete_copy_next_to_the_panel_is_used_when_nothing_is_built()
    {
        Place(PanelDirectory, "NosAi.Runtime.dll", RuntimeDllLocator.RequiredCompanion);

        var resolved = RuntimeDllLocator.Resolve(RepoRoot, PanelDirectory, out string? reason);

        Assert.Equal(Path.Combine(PanelDirectory, "NosAi.Runtime.dll"), resolved);
        Assert.Null(reason);
    }

    [Fact]
    public void An_incomplete_copy_alone_resolves_to_nothing_and_says_why()
    {
        Place(PanelDirectory, "NosAi.Runtime.dll");

        var resolved = RuntimeDllLocator.Resolve(RepoRoot, PanelDirectory, out string? reason);

        Assert.Null(resolved);
        Assert.NotNull(reason);
        Assert.Contains(RuntimeDllLocator.RequiredCompanion, reason);
    }

    [Fact]
    public void No_runtime_at_all_says_it_is_not_built()
    {
        Directory.CreateDirectory(PanelDirectory);

        var resolved = RuntimeDllLocator.Resolve(RepoRoot, PanelDirectory, out string? reason);

        Assert.Null(resolved);
        Assert.Contains("non compilato", reason);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
