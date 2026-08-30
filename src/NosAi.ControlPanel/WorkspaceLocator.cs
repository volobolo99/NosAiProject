using System.IO;

namespace NosAi.ControlPanel;

/// <summary>
/// Finds the repository root so relative paths (<c>data/</c>, phone tools) resolve
/// the same way as when the runtime is launched from the console.
/// </summary>
public static class WorkspaceLocator
{
    public const string MarkerFile = "NOSAI_MASTER_ROADMAP.md";

    public static string Find(string? start = null)
    {
        var current = new DirectoryInfo(start ?? AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, MarkerFile))
                && Directory.Exists(Path.Combine(current.FullName, "src", "NosAi.Runtime")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
