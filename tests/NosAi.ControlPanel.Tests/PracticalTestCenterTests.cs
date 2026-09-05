using System;
using System.IO;
using Xunit;

namespace NosAi.ControlPanel.Tests;

public sealed class PracticalTestCenterTests
{
    private static string RepositoryRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void TestCenterWindowAndShortcutArePresent()
    {
        string root = RepositoryRoot();
        string xaml = File.ReadAllText(Path.Combine(root, "src", "NosAi.ControlPanel", "PracticalTestCenterWindow.xaml"));
        string code = File.ReadAllText(Path.Combine(root, "src", "NosAi.ControlPanel", "PracticalTestCenterWindow.xaml.cs"));
        string app = File.ReadAllText(Path.Combine(root, "src", "NosAi.ControlPanel", "App.xaml.cs"));

        Assert.Contains("Live Test Center", xaml, StringComparison.Ordinal);
        Assert.Contains("250", code, StringComparison.Ordinal);
        Assert.Contains("PracticalTestCatalog.All", code, StringComparison.Ordinal);
        Assert.Contains("Key.F9", app, StringComparison.Ordinal);
        Assert.Contains("ModifierKeys.Control", app, StringComparison.Ordinal);
    }

    [Fact]
    public void TestCatalogContainsAllTenPracticalPillars()
    {
        Assert.Equal(10, NosAi.Core.Testing.PracticalTestCatalog.All.Count);
        Assert.Equal("T1", NosAi.Core.Testing.PracticalTestCatalog.All[0].Id);
        Assert.Equal("T10", NosAi.Core.Testing.PracticalTestCatalog.All[^1].Id);
    }
}
