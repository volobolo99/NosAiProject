using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using NosAi.Core;

namespace NosAi.Adapter;

/// <summary>
/// Real Win32 <see cref="IGameProcessAdapter"/>: <c>OpenProcess</c> +
/// <c>ReadProcessMemory</c> for raw bytes, <c>GetWindowRect</c> for geometry.
/// Black-box only (<c>.cursorrules</c> S:3, "Anti-Fingerprinting Memory
/// Isolation"): this reads the target's memory and window like any other
/// process is allowed to via public OS APIs, and does not hook, inject into,
/// or modify the target. Attach-time verification is fail-closed: any
/// mismatch between what was requested and what was actually found aborts
/// attach with a specific <see cref="FaultCode"/>, never a partial attach.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Win32ProcessAdapter : IGameProcessAdapter
{
    private const int ProcessQueryInformation = 0x0400;
    private const int ProcessVmRead = 0x0010;

    private nint _processHandle;
    private int _processId;
    private WindowGeometry _geometry;
    private bool _attached;
    private bool _disposed;

    public int ProcessId => _processId;

    public bool IsAttached => _attached;

    public WindowGeometry Geometry => _geometry;

    public bool TryAttach(in ProcessAttachOptions options, out FaultCode fault)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ProcessName);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ExpectedModule);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ModuleSha256);

        if (_attached)
        {
            // Re-attaching over a live handle would leak it; the caller must
            // Dispose and create a new adapter, which is exactly as cheap.
            fault = FaultCode.AttachFailed;
            return false;
        }

        var stopwatch = Stopwatch.StartNew();
        do
        {
            if (TryAttachOnce(options, out fault))
                return true;

            if (fault != FaultCode.AttachFailed)
                return false; // A definitive failure (bad module hash, denied handle): retrying will not help.

        } while (stopwatch.ElapsedMilliseconds < options.TimeoutMs);

        fault = FaultCode.AttachFailed;
        return false;
    }

    private bool TryAttachOnce(in ProcessAttachOptions options, out FaultCode fault)
    {
        Process[] candidates = Process.GetProcessesByName(options.ProcessName);
        try
        {
            if (candidates.Length == 0)
            {
                fault = FaultCode.AttachFailed;
                return false;
            }

            Process process = candidates[0];

            if (!TryVerifyModule(process, options, out fault))
                return false;

            nint handle = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, process.Id);
            if (handle == 0)
            {
                fault = FaultCode.AttachFailed;
                return false;
            }

            _processHandle = handle;
            _processId = process.Id;
            _geometry = TryReadWindowGeometry(process);
            _attached = true;
            fault = FaultCode.None;
            return true;
        }
        finally
        {
            foreach (Process process in candidates)
                process.Dispose();
        }
    }

    private static bool TryVerifyModule(Process process, in ProcessAttachOptions options, out FaultCode fault)
    {
        try
        {
            ProcessModule? module = FindModule(process, options.ExpectedModule);
            if (module?.FileName is null)
            {
                fault = FaultCode.AttachFailed;
                return false;
            }

            using FileStream stream = File.OpenRead(module.FileName);
            byte[] hash = SHA256.HashData(stream);
            string actual = Convert.ToHexString(hash);

            if (!string.Equals(actual, options.ModuleSha256, StringComparison.OrdinalIgnoreCase))
            {
                fault = FaultCode.AttachFailed;
                return false;
            }

            fault = FaultCode.None;
            return true;
        }
        catch (Win32Exception)
        {
            fault = FaultCode.AttachFailed;
            return false;
        }
        catch (IOException)
        {
            fault = FaultCode.AttachFailed;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            fault = FaultCode.AttachFailed;
            return false;
        }
    }

    private static ProcessModule? FindModule(Process process, string expectedModuleName)
    {
        foreach (ProcessModule module in process.Modules)
        {
            if (string.Equals(module.ModuleName, expectedModuleName, StringComparison.OrdinalIgnoreCase))
                return module;
        }

        return null;
    }

    private static WindowGeometry TryReadWindowGeometry(Process process)
    {
        nint handle = process.MainWindowHandle;
        if (handle == 0 || !GetWindowRect(handle, out RECT rect))
            return default;

        return new WindowGeometry(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    public unsafe int ReadRegion(nuint address, Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_attached)
            throw new InvalidOperationException("Adapter is not attached; call TryAttach first.");

        if (destination.IsEmpty)
            return 0;

        fixed (byte* buffer = destination)
        {
            // A failed read reports zero bytes, never a partial or stale buffer
            // presented as if it were complete (.cursor/rules/25-connection-and-ban-risk.mdc).
            if (!ReadProcessMemory(_processHandle, address, buffer, (nuint)destination.Length, out nuint bytesRead))
                return 0;

            return (int)bytesRead;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_processHandle != 0)
        {
            CloseHandle(_processHandle);
            _processHandle = 0;
        }

        _attached = false;
        _disposed = true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(int desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern unsafe bool ReadProcessMemory(nint hProcess, nuint baseAddress, byte* buffer, nuint size, out nuint bytesRead);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(nint hWnd, out RECT rect);
}
