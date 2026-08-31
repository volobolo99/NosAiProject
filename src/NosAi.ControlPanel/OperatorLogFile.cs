using System.IO;

namespace NosAi.ControlPanel;

/// <summary>Append-only operator diary on disk. Not an audit log of Safety.</summary>
internal static class OperatorLogFile
{
    public const string RelativePath = "data/operator.log";

    public static void Append(string repoRoot, LogEntry entry)
    {
        try
        {
            var path = Path.Combine(repoRoot, RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"[{entry.At:yyyy-MM-dd HH:mm:ss}] {entry.Level,-5} {entry.Message}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // The on-screen diary still works; a locked log must not take down the panel.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
