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

        // Le due letture arrivano da thread diversi e StringBuilder non e'
        // sincronizzato: senza il lock, stdout e stderr che escono insieme
        // possono intrecciarsi dentro la stessa riga.
        var gate = new object();
        void Collect(string line)
        {
            lock (gate)
                output.AppendLine(line);
            onLine?.Invoke(line);
        }

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            Collect(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            Collect(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        // WaitForExitAsync torna quando il processo e' uscito, non quando le due
        // letture asincrone hanno finito di consegnare: le ultime righe possono
        // essere ancora in volo, e sono proprio quelle che contano, perche' ogni
        // comando del runtime stampa il riepilogo alla fine. Il 2026-09-08 e'
        // costato una diagnosi: la card T-12 leggeva zero righe da una
        // registrazione che ne aveva, mentre il Diario -- che riceve le stesse
        // righe piu' tardi, per un'altra strada -- le mostrava tutte.
        // WaitForExit() senza timeout e' il modo documentato di aspettare che
        // gli handler abbiano consegnato tutto; qui non blocca l'interfaccia,
        // perche' si arriva su un thread di pool dopo ConfigureAwait(false).
        process.WaitForExit();

        lock (gate)
            return new ToolResult(process.ExitCode, output.ToString());
    }
}
