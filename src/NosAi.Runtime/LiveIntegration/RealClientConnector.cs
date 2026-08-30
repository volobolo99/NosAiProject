using System.Diagnostics;
using System.Runtime.InteropServices;
using NosAi.Runtime.Gate1;

namespace NosAi.LiveIntegration;

public enum ClientBaselineAvailability
{
    Unavailable = 0,
    ProcessOnly = 1,
    WindowAttached = 2,
    BaselineReady = 3
}

public sealed record ClientBaselineSnapshot(
    bool ProcessDetected,
    bool WindowDetected,
    bool ClientAttached,
    int? ProcessId,
    nint WindowHandle,
    string Source,
    DateTime ObservedAtUtc,
    ClientBaselineAvailability Availability,
    string Status,
    string? Warning,
    string? FailureReason);

/// <summary>
/// Connects the runtime to the real NosTale process and exposes the existing
/// Gate 1 Guard AI transport. The connector deliberately does not inspect or
/// modify game memory and does not implement a second wire protocol.
/// </summary>
public sealed class RealClientConnector : IAsyncDisposable
{
    /// <summary>
    /// Candidate executable names, tried in order. The shipped client runs as
    /// NostaleClientX, so a lone "NosTale" entry matched nothing:
    /// Process.GetProcessesByName needs the exact name, not a prefix.
    /// NostaleLauncher is deliberately absent - it is not the game client.
    /// </summary>
    public static readonly string[] DefaultProcessNames = { "NostaleClientX", "NostaleClient", "NosTale" };

    private const string DefaultWindowTitle = "Nostale";

    private readonly GuardAiNetworkChannel _networkChannel;
    private readonly string[] _processNames;
    private readonly string _windowTitle;
    private Process? _gameProcess;
    private IntPtr _gameWindowHandle = IntPtr.Zero;
    private DateTime _lastObservedAtUtc = DateTime.MinValue;
    private string? _lastFailureReason;
    private bool _disposed;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    /// <param name="clientProcessNames">
    /// Comma-separated executable names to look for, without the extension.
    /// Null or blank keeps <see cref="DefaultProcessNames"/>.
    /// </param>
    public RealClientConnector(
        GuardAiNetworkChannel networkChannel,
        string? clientProcessNames = null,
        string? windowTitle = null)
    {
        _networkChannel = networkChannel ?? throw new ArgumentNullException(nameof(networkChannel));
        var configured = (clientProcessNames ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _processNames = configured.Length > 0 ? configured : DefaultProcessNames;
        _windowTitle = string.IsNullOrWhiteSpace(windowTitle) ? DefaultWindowTitle : windowTitle;
    }

    /// <summary>Executable names this connector looks for.</summary>
    public IReadOnlyList<string> ClientProcessNames => _processNames;

    public bool IsClientAttached => _gameProcess is { HasExited: false } && _gameWindowHandle != IntPtr.Zero;

    public int? AttachedProcessId => _gameProcess is null || _gameProcess.HasExited ? null : _gameProcess.Id;

    public IntPtr GameWindowHandle => _gameWindowHandle;

    /// <summary>
    /// Re-observes the client. A process that has exited is detached instead of
    /// leaving a stale attached=true snapshot, and whenever nothing is attached
    /// the scan is retried so a client started after the runtime is still found.
    /// </summary>
    public ClientBaselineSnapshot Observe()
    {
        ThrowIfDisposed();
        _lastObservedAtUtc = DateTime.UtcNow;

        if (AttachedProcessHasExited())
        {
            _lastFailureReason = "client_process_exited";
            DetachCurrentProcess();
        }

        // Rescan on every unattached observation. Matching only on an already
        // held process meant a client absent at startup was never picked up
        // again, because _gameProcess stayed null forever.
        if (!IsClientAttached && OperatingSystem.IsWindows())
        {
            VerifyAndAttachClient();
        }

        return CaptureBaselineSnapshot();
    }

    /// <summary>
    /// Whether the attached process is gone. A handle that can no longer answer
    /// counts as gone: reporting a client we cannot confirm is the unsafe answer.
    /// </summary>
    private bool AttachedProcessHasExited()
    {
        if (_gameProcess is null)
            return false;
        try
        {
            return _gameProcess.HasExited;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return true;
        }
    }

    /// <summary>
    /// Finds the live NosTale process and its main window without opening a
    /// process handle with write/debug privileges.
    /// </summary>
    public bool VerifyAndAttachClient()
    {
        ThrowIfDisposed();

        DetachCurrentProcess();
        _lastObservedAtUtc = DateTime.UtcNow;
        _lastFailureReason = null;

        if (!OperatingSystem.IsWindows())
        {
            _lastFailureReason = "unsupported_platform";
            Console.WriteLine("[RealClientConnector] ERRORE: il rilevamento della finestra NosTale è supportato solo su Windows.");
            return false;
        }

        var processes = new List<Process>();
        foreach (var candidate in _processNames)
        {
            try
            {
                processes.AddRange(Process.GetProcessesByName(candidate));
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                _lastFailureReason = $"process_enumeration_failed:{ex.GetType().Name}";
                Console.WriteLine($"[RealClientConnector] ERRORE: impossibile enumerare '{candidate}': {ex.Message}");
                foreach (var opened in processes)
                    opened.Dispose();
                return false;
            }
        }

        if (processes.Count == 0)
        {
            _lastFailureReason = "process_not_found";
            Console.WriteLine($"[RealClientConnector] ERRORE: nessun processo client trovato tra: {string.Join(", ", _processNames)}.");
            return false;
        }

        try
        {
            // The client runs several processes under the same executable name and
            // only one owns the game window, so the windowed one wins rather than
            // whichever the OS happened to list first.
            foreach (var process in processes)
            {
                try
                {
                    var windowHandle = process.MainWindowHandle;
                    if (windowHandle == IntPtr.Zero)
                        continue;

                    _gameProcess = process;
                    _gameWindowHandle = windowHandle;
                    _lastObservedAtUtc = DateTime.UtcNow;
                    LogAttachmentSuccess(process.ProcessName, process.Id, windowHandle);
                    return true;
                }
                catch (InvalidOperationException)
                {
                }
            }

            var titledWindow = FindWindow(null, _windowTitle);
            if (titledWindow != IntPtr.Zero)
            {
                GetWindowThreadProcessId(titledWindow, out var processId);
                foreach (var process in processes)
                {
                    if (process.Id != processId)
                        continue;

                    _gameProcess = process;
                    _gameWindowHandle = titledWindow;
                    _lastObservedAtUtc = DateTime.UtcNow;
                    LogAttachmentSuccess(process.ProcessName, process.Id, titledWindow);
                    return true;
                }
            }

            // The process is running even though no window could be matched.
            // Keeping it makes ProcessDetected true and the ProcessOnly state
            // reachable; dropping it reported the client as entirely absent.
            foreach (var process in processes)
            {
                try
                {
                    if (process.HasExited)
                        continue;
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    continue;
                }

                _gameProcess = process;
                break;
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                if (!ReferenceEquals(process, _gameProcess))
                    process.Dispose();
            }
        }

        _lastFailureReason = "window_not_found";
        Console.WriteLine("[RealClientConnector] ERRORE: finestra di gioco non rilevata nonostante il processo sia attivo.");
        return false;
    }

    /// <summary>
    /// Returns the current client baseline snapshot for Gate 1.
    /// This baseline intentionally exposes attachment/readiness status only.
    /// It does not claim gameplay data extraction until a real provider exists.
    /// </summary>
    public ClientBaselineSnapshot CaptureBaselineSnapshot()
    {
        ThrowIfDisposed();

        var processDetected = _gameProcess is { HasExited: false };
        var windowDetected = _gameWindowHandle != IntPtr.Zero;
        var attached = processDetected && windowDetected;
        var observedAt = _lastObservedAtUtc == DateTime.MinValue ? DateTime.UtcNow : _lastObservedAtUtc;

        var availability = attached
            ? ClientBaselineAvailability.WindowAttached
            : processDetected
                ? ClientBaselineAvailability.ProcessOnly
                : ClientBaselineAvailability.Unavailable;

        var status = availability switch
        {
            ClientBaselineAvailability.WindowAttached => "attached_window_only",
            ClientBaselineAvailability.ProcessOnly => "process_detected_window_missing",
            _ => "client_unavailable"
        };

        var warning = attached
            ? "Gameplay baseline data not yet available: only process/window attachment is currently verified."
            : null;

        return new ClientBaselineSnapshot(
            ProcessDetected: processDetected,
            WindowDetected: windowDetected,
            ClientAttached: attached,
            ProcessId: AttachedProcessId,
            WindowHandle: _gameWindowHandle,
            Source: "live_process_attach",
            ObservedAtUtc: observedAt,
            Availability: availability,
            Status: status,
            Warning: warning,
            FailureReason: _lastFailureReason);
    }

    /// <summary>
    /// Starts the canonical Gate 1 transport used by the smartphone Guard AI.
    /// Authentication, NOSA framing, sequence guards and heartbeat fail-closed
    /// remain owned by GuardAiNetworkChannel.
    /// </summary>
    public Task StartRealNetworkTransportAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();

        _networkChannel.Start();
        Console.WriteLine($"[RealClientConnector] Gate 1 transport avviato sulla porta {_networkChannel.LocalPort}. Framing NOSA/auth/heartbeat gestiti dal canale canonico.");
        return Task.CompletedTask;
    }

    public Gate1ConnectionSnapshot GetNetworkSnapshot()
    {
        ThrowIfDisposed();
        return _networkChannel.GetSnapshot();
    }

    private static void LogAttachmentSuccess(string processName, int processId, IntPtr windowHandle)
    {
        Console.WriteLine($"[RealClientConnector] SUCCESSO: connesso al processo '{processName}' (PID: {processId}), Window Handle: {windowHandle}");
    }

    private void DetachCurrentProcess()
    {
        _gameWindowHandle = IntPtr.Zero;
        _gameProcess?.Dispose();
        _gameProcess = null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        DetachCurrentProcess();
        await _networkChannel.DisposeAsync().ConfigureAwait(false);
    }
}
