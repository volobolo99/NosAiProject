using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NosAi.Runtime.Testing;

namespace NosAi.ControlPanel;

/// <summary>
/// Result of a startup round launch: the freshly written report when the round
/// ran, otherwise the reason the round could not run or produced no report.
/// </summary>
public sealed record StartupRoundOutcome(StartupRoundReport? Report, string? Failure);

/// <summary>
/// Launches the startup round as a child "dotnet" process so that the checks
/// never run inside the WPF process, then reads back the JSON report written
/// by the runtime.
/// </summary>
public sealed class StartupRoundClient
{
    private readonly string _repoRoot;
    private int _running;

    public StartupRoundClient(string repoRoot)
    {
        if (string.IsNullOrEmpty(repoRoot))
        {
            throw new ArgumentException("Repo root must not be null or empty.", nameof(repoRoot));
        }

        _repoRoot = repoRoot;
    }

    /// <summary>
    /// Path of the JSON report written by the most recent startup round.
    /// </summary>
    public string LatestPath => Path.Combine(_repoRoot, "data", "health", StartupRoundReport.LatestFileName);

    /// <summary>
    /// True while a startup round is being launched or awaited by this client.
    /// </summary>
    public bool IsRunning => Volatile.Read(ref _running) != 0;

    /// <summary>
    /// Reads the latest report from disk. Returns null, without throwing, when
    /// the report is missing, unreadable or corrupted.
    /// </summary>
    public StartupRoundReport? ReadLatest()
    {
        return StartupRoundReport.TryRead(LatestPath);
    }

    /// <summary>
    /// Runs the startup round with the requested mode and waits for its report.
    /// Never throws except for OperationCanceledException.
    /// </summary>
    public async Task<StartupRoundOutcome> RunAsync(StartupRoundMode mode, CancellationToken cancellationToken)
    {
        // Only one round at a time: a concurrent caller gets an immediate failure.
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            return new StartupRoundOutcome(null, "Ronda già in corso.");
        }

        try
        {
            string? dll = ResolveRuntimeDll();
            if (dll is null)
            {
                return new StartupRoundOutcome(null, "Runtime non compilato: premere Compila runtime nella pagina Certificazione.");
            }

            string arguments = $"\"{dll}\" {StartupRound.Flag}";
            if (mode == StartupRoundMode.Quick)
            {
                arguments += " " + StartupRound.QuickFlag;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = arguments,
                WorkingDirectory = _repoRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            Process? started;
            try
            {
                started = Process.Start(startInfo);
            }
            catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
            {
                return new StartupRoundOutcome(null, ex.Message);
            }

            if (started is null)
            {
                return new StartupRoundOutcome(null, "Impossibile avviare il processo 'dotnet'.");
            }

            Process process = started;

            // Start draining both pipes before waiting for exit, otherwise a full
            // pipe would block the child process forever.
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            Task<string> standardError = process.StandardError.ReadToEndAsync();

            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is not an outcome: kill the child and surface it.
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // The child had already exited; there is nothing left to kill.
                }
                catch (Win32Exception)
                {
                    // The operating system rejected the kill request; the
                    // cancellation is still reported to the caller.
                }

                throw;
            }

            string stderr = await standardError.ConfigureAwait(false);
            await standardOutput.ConfigureAwait(false);

            // A report proves the round ran, whatever the exit code: exit code 1
            // only means that some checks failed.
            StartupRoundReport? report = ReadLatest();
            if (report is not null)
            {
                return new StartupRoundOutcome(report, null);
            }

            if (!string.IsNullOrEmpty(stderr))
            {
                string reason = stderr.Length > 300 ? stderr[..300] : stderr;
                return new StartupRoundOutcome(null, reason);
            }

            return new StartupRoundOutcome(null, "La ronda non ha prodotto un report.");
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            return new StartupRoundOutcome(null, ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    private string? ResolveRuntimeDll() => RuntimeDllLocator.Resolve(_repoRoot);
}
