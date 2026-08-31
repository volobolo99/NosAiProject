using System.IO;
using NosAi.Runtime.Gate1;
using Xunit;

namespace NosAi.ControlPanel.Tests;

public sealed class SecurityInspectTests
{
    [Fact]
    public void Missing_files_are_unknown_not_invented()
    {
        var root = Path.Combine(Path.GetTempPath(), "nosai-panel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var fields = SecurityInspect.Inspect(root);
            Assert.Contains(fields, f => f.Label == "Identità DPAPI" && f.Source == "UNKNOWN");
            Assert.Contains(fields, f => f.Label.StartsWith("Pin") && f.Source == "UNKNOWN");
            Assert.DoesNotContain(fields, f => f.Value.Contains("TPM", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Protected_path_constant_is_what_we_observe()
    {
        Assert.Equal("data/runtime_identity.dpapi", RuntimeIdentity.DefaultProtectedPath);
    }
}
