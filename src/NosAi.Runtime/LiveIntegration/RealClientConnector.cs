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
    private const string TargetProcessName = "NosTale";
    private const string DefaultWindowTitle = "NosTale";

    private readonly GuardAiNetworkChannel _networkChannel;
    private Process? _gameProcess;
    private IntPtr _gameWindowHandle = IntPtr.Zero;
    private DateTime _lastObservedAtUtc = DateTime.MinValue;
    private string? _lastFailureReason;
    private bool _disposed;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    public RealClientConnector(GuardAiNetworkChannel networkChannel)
    {
        _networkChannel = networkChannel ?? throw new ArgumentNullException(nameof(networkChannel));
    }

    public bool IsClientAttached => _gameProcess is { HasExited: false } && _gameWindowHandle != IntPtr.Zero;

    public int? AttachedProcessId => _gameProcess is null || _gameProcess.HasExited ? null : _gameProcess.Id;

    public IntPtr GameWindowHandle => _gameWindowHandle;

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

        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(TargetProcessName);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _lastFailureReason = $"process_enumeration_failed:{ex.GetType().Name}";
            Console.WriteLine($"[RealClientConnector] ERRORE: impossibile enumerare '{TargetProcessName}': {ex.Message}");
            return false;
        }

        if (processes.Length == 0)
        {
            _lastFailureReason = "process_not_found";
            Console.WriteLine($"[RealClientConnector] ERRORE: processo '{TargetProcessName}' non trovato sul sistema.");
            return false;
        }

        try
        {
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
                    LogAttachmentSuccess(process.Id, windowHandle);
                    return true;
                }
                catch (InvalidOperationException)
                {
                    process.Dispose();
                }
            }

            var titledWindow = FindWindow(null, DefaultWindowTitle);
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
                    LogAttachmentSuccess(process.Id, titledWindow);
                    return true;
                }
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

    private void LogAttachmentSuccess(int processId, IntPtr windowHandle)
    {
        Console.WriteLine($"[RealClientConnector] SUCCESSO: connesso al processo '{TargetProcessName}' (PID: {processId}), Window Handle: {windowHandle}");
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
