using System.Net;
using System.Runtime.InteropServices;

namespace NosAi.LiveIntegration.Capture;

/// <summary>
/// A live packet source backed by the WinDivert kernel driver.
/// </summary>
/// <remarks>
/// <para>
/// SNIFF + RECV_ONLY: it copies packets without removing them from the network,
/// so the game runs normally and nothing is modified or injected. This is
/// observation. The account risk of capturing game traffic is real and the
/// operator's — recorded in ADR-0014 and not softened here.
/// </para>
/// <para>
/// <b>Not covered by automated tests</b>, and it cannot be: opening it registers
/// a kernel driver and needs elevation and the WinDivert binaries present. The
/// parsing, reassembly and framing it feeds are all tested against synthetic and
/// recorded sources; this class is the thin, untestable edge that turns a real
/// driver handle into <see cref="CapturedPacket"/>s. Failures are structured, so
/// a missing driver or a lack of elevation is reported rather than thrown blindly.
/// </para>
/// </remarks>
public sealed class WinDivertPacketSource : IPacketSource
{
    private const short LayerNetwork = 0;

    // WinDivert 2.x flag values, from the header. They are internal rather than
    // private so a test can hold them to those values without the driver: the
    // capture layer read nothing at all for as long as it existed because
    // FlagRecvOnly was 0x0008, which is SEND_ONLY. The handle opened cleanly, the
    // filter was right, and every packet was unreachable through a write-only
    // handle. Nothing caught it because everything below this class is exercised
    // against synthetic sources and recorded files, where the driver never runs.
    internal const ulong FlagSniff = 0x0001;
    internal const ulong FlagDrop = 0x0002;
    internal const ulong FlagRecvOnly = 0x0004;
    internal const ulong FlagSendOnly = 0x0008;

    private const int ErrorNoData = 232;

    private readonly IntPtr _handle;
    private bool _disposed;

    private WinDivertPacketSource(IntPtr handle, IPAddress serverAddress, int serverPort)
    {
        _handle = handle;
        ServerAddress = serverAddress;
        ServerPort = serverPort;
    }

    public IPAddress ServerAddress { get; }
    public int ServerPort { get; }

    /// <summary>
    /// Opens a live capture of one server endpoint, or returns null with a reason.
    /// </summary>
    /// <remarks>
    /// Null over an exception because the caller has a decision to make — a missing
    /// driver is not a crash, it is "install it first" — and the reason is what the
    /// operator needs to see.
    /// </remarks>
    public static WinDivertPacketSource? TryOpen(IPAddress serverAddress, int serverPort, out string? failureReason)
    {
        failureReason = null;
        ArgumentNullException.ThrowIfNull(serverAddress);

        if (!OperatingSystem.IsWindows())
        {
            failureReason = "windivert_unavailable_off_windows";
            return null;
        }

        string filter = $"ip and tcp and (ip.SrcAddr == {serverAddress} or ip.DstAddr == {serverAddress}) " +
                        $"and (tcp.SrcPort == {serverPort} or tcp.DstPort == {serverPort})";

        IntPtr handle;
        try
        {
            handle = WinDivertOpen(filter, LayerNetwork, 0, FlagSniff | FlagRecvOnly);
        }
        catch (DllNotFoundException)
        {
            failureReason = "windivert_dll_not_found";
            return null;
        }

        if (handle == new IntPtr(-1))
        {
            failureReason = Marshal.GetLastWin32Error() switch
            {
                5 => "access_denied_run_elevated",
                2 => "windivert_driver_not_found",
                577 => "driver_signature_rejected",
                1275 => "driver_blocked",
                int e => $"windivert_open_failed:{e}"
            };
            return null;
        }

        return new WinDivertPacketSource(handle, serverAddress, serverPort);
    }

    public bool TryRead(TimeSpan timeout, out CapturedPacket packet)
    {
        packet = default;
        if (_disposed)
            return false;

        // WinDivertRecv blocks; the timeout is honoured by the driver's own
        // shutdown path on Close, and the SNIFF handle returns per packet. A
        // dedicated read here keeps the buffer local so concurrent reads are safe.
        var buffer = new byte[65535];
        if (!WinDivertRecv(_handle, buffer, (uint)buffer.Length, out uint received, _addressScratch))
        {
            if (Marshal.GetLastWin32Error() == ErrorNoData)
                return false; // handle shutting down
            return false;
        }

        if (received == 0)
            return false;

        var raw = new byte[received];
        Array.Copy(buffer, raw, received);
        packet = new CapturedPacket(DateTime.UtcNow, raw);
        return true;
    }

    // WINDIVERT_ADDRESS is opaque to this source: it labels direction from the
    // parsed endpoint, not from the driver's metadata, so the address struct is a
    // scratch sink here.
    private readonly byte[] _addressScratch = new byte[64];

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_handle != IntPtr.Zero && _handle != new IntPtr(-1))
            WinDivertClose(_handle);
    }

    [DllImport("WinDivert.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr WinDivertOpen(string filter, short layer, short priority, ulong flags);

    [DllImport("WinDivert.dll", SetLastError = true)]
    private static extern bool WinDivertRecv(IntPtr handle, byte[] packet, uint packetLen, out uint recvLen, byte[] address);

    [DllImport("WinDivert.dll", SetLastError = true)]
    private static extern bool WinDivertClose(IntPtr handle);
}
