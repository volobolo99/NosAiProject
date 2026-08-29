using System.Diagnostics;
using System.Runtime.InteropServices;
using NosAi.Runtime.Gate1;

namespace NosAi.LiveIntegration;

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

        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(TargetProcessName);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Console.WriteLine($"[RealClientConnector] ERRORE: impossibile enumerare '{TargetProcessName}': {ex.Message}");
            return false;
        }

        if (processes.Length == 0)
        {
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

        Console.WriteLine("[RealClientConnector] ERRORE: finestra di gioco non rilevata nonostante il processo sia attivo.");
        return false;
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
