using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Security;

namespace NosAi.LiveIntegration;

/// <summary>The result of one memory read, classified by whether it can be trusted.</summary>
/// <remarks>
/// <para>
/// The classification is the whole point. ADR-0012 rejected memory reads as a first
/// provider for one reason that survived ADR-0014 lifting the prohibition: <b>a
/// wrong offset does not fail, it returns a plausible number</b>. So a read is only
/// <see cref="DataSourceKind.Live"/> when a validity check passed; otherwise it is
/// <see cref="DataSourceKind.Unknown"/>, never the last good value and never a
/// number that merely looks right.
/// </para>
/// </remarks>
public sealed record MemoryReadResult(
    DataSourceKind Source,
    byte[] Bytes,
    string? FailureReason)
{
    public bool Ok => Source == DataSourceKind.Live && FailureReason is null;

    public static MemoryReadResult Live(byte[] bytes) => new(DataSourceKind.Live, bytes, null);

    public static MemoryReadResult Unknown(string reason) =>
        new(DataSourceKind.Unknown, Array.Empty<byte>(), reason);
}

/// <summary>
/// Reads the game client's process memory (ADR-0014), for the principal allowed to.
/// </summary>
/// <remarks>
/// <para>
/// Available since ADR-0014, and gated the way that decision requires: Safety
/// remains the authority, so every read is authorised against
/// <see cref="RuntimeCapability.ReadProcessMemory"/> before a handle is opened.
/// The operator holds it; the paired phone does not, so a stolen device cannot
/// make the PC read another process.
/// </para>
/// <para>
/// <b>Account risk is real and the operator's.</b> Opening a handle to a game
/// client is what anti-cheat looks for. ADR-0014 records that the person carrying
/// the risk is the person who decided; nothing here reduces it or hides it.
/// </para>
/// <para>
/// Reads only. There is no write path in this class, and adding one would be a
/// different capability with a different decision behind it.
/// </para>
/// </remarks>
public sealed class ProcessMemoryReader : IDisposable
{
    /// <summary>PROCESS_VM_READ | PROCESS_QUERY_INFORMATION.</summary>
    private const int AccessVmReadAndQuery = 0x0010 | 0x0400;

    /// <summary>Largest single read. A cap keeps a bad length from allocating wildly.</summary>
    public const int MaxReadLength = 1 << 20;

    private readonly IntPtr _handle;
    private bool _disposed;

    private ProcessMemoryReader(IntPtr handle, int processId)
    {
        _handle = handle;
        ProcessId = processId;
    }

    public int ProcessId { get; }

    /// <summary>
    /// Opens a read handle to a process, if the principal may and the OS allows.
    /// </summary>
    /// <returns>Null with a reason rather than an exception: refused and impossible are both answers the caller acts on.</returns>
    public static ProcessMemoryReader? TryOpen(
        int processId,
        SecurityPrincipal principal,
        out string? failureReason,
        IRuntimeAuthorizationPolicy? authorization = null)
    {
        failureReason = null;

        if (processId <= 0)
        {
            failureReason = "invalid_process_id";
            return null;
        }

        // Authorised before anything is opened: the check is the gate, not a label
        // applied afterwards.
        var policy = authorization ?? new Gate1AuthorizationPolicy();
        var decision = policy.Evaluate(principal, RuntimeCapability.ReadProcessMemory, TrustTier.Tier1, TrustTier.Tier4);
        if (!decision.Allowed)
        {
            failureReason = $"not_authorized:{decision.Reason}";
            return null;
        }

        if (!OperatingSystem.IsWindows())
        {
            failureReason = "process_memory_unavailable_off_windows";
            return null;
        }

        IntPtr handle = OpenProcess(AccessVmReadAndQuery, false, processId);
        if (handle == IntPtr.Zero)
        {
            failureReason = Marshal.GetLastWin32Error() switch
            {
                5 => "access_denied_run_elevated_or_protected_process",
                87 => "process_not_found",
                int e => $"open_process_failed:{e}"
            };
            return null;
        }

        return new ProcessMemoryReader(handle, processId);
    }

    /// <summary>
    /// Reads <paramref name="length"/> bytes at <paramref name="address"/>.
    /// </summary>
    /// <remarks>
    /// A partial read is a failure, not a short success: half a value is not a
    /// value, and returning it would hand the caller a number built from whatever
    /// happened to be readable.
    /// </remarks>
    public MemoryReadResult Read(IntPtr address, int length)
    {
        if (_disposed)
            return MemoryReadResult.Unknown("reader_disposed");
        if (length <= 0 || length > MaxReadLength)
            return MemoryReadResult.Unknown($"invalid_length:{length}");
        if (address == IntPtr.Zero)
            return MemoryReadResult.Unknown("null_address");

        var buffer = new byte[length];
        if (!ReadProcessMemoryNative(_handle, address, buffer, length, out IntPtr read))
            return MemoryReadResult.Unknown($"read_failed:{Marshal.GetLastWin32Error()}");

        if ((int)read != length)
            return MemoryReadResult.Unknown($"partial_read:{(int)read}_of_{length}");

        return MemoryReadResult.Live(buffer);
    }

    /// <summary>
    /// Reads a 32-bit integer and checks it against what the caller knows must hold.
    /// </summary>
    /// <remarks>
    /// The validity check is not optional decoration — it is the difference between
    /// a reading and a guess. An offset that moved with a game patch still returns
    /// four readable bytes; only a bound the value must satisfy can tell the two
    /// apart, and without one the honest answer is <c>UNKNOWN</c>.
    /// </remarks>
    public ClassifiedValue<int?> ReadValidatedInt32(IntPtr address, Func<int, bool> isPlausible, DateTime observedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(isPlausible);

        var result = Read(address, sizeof(int));
        if (!result.Ok)
            return ClassifiedValue<int?>.Unknown(result.FailureReason ?? "memory_read_failed");

        int value = BitConverter.ToInt32(result.Bytes);
        if (!isPlausible(value))
            return ClassifiedValue<int?>.Unknown($"value_failed_validity_check:{value}");

        return ClassifiedValue<int?>.Live(value, observedAtUtc);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_handle != IntPtr.Zero)
            CloseHandle(_handle);
    }

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int desiredAccess, bool inheritHandle, int processId);

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "ReadProcessMemory")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemoryNative(
        IntPtr process, IntPtr address, [Out] byte[] buffer, int size, out IntPtr bytesRead);

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
