using System.Diagnostics;
using System.IO;
using System.Text;

namespace NosAi.ControlPanel;

public sealed record ToolResult(int ExitCode, string Output);

/// <summary>Runs local operator tools (build, pairing, certification) as subprocesses.</summary>
public static class ToolRunner
{
    public static string? FindPython()
    {
        foreach (var candidate in new[] { "py", "python", "python3" })
        {
            try
            {
                var start = new ProcessStartInfo
                {
                    FileName = candidate,
                    Arguments = candidate == "py" ? "-3 -c \"import sys; print(sys.executable)\"" : "-c \"import sys; print(sys.executable)\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(start);
                if (process is null)
                    continue;
                var path = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(4000);
                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    return path;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
            }
        }

        return null;
    }

    public static async Task<ToolResult> RunAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        Action<string>? onLine = null,
        CancellationToken cancellationToken = default)
    {
        var start = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            output.AppendLine(e.Data);
            onLine?.Invoke(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            output.AppendLine(e.Data);
            onLine?.Invoke(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new ToolResult(process.ExitCode, output.ToString());
    }
}
