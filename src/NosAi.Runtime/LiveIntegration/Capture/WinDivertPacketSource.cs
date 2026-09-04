using System.Collections.Concurrent;
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

    /// <summary>How many packets may wait for a reader before the oldest are lost.</summary>
    /// <remarks>
    /// A live capture cannot pause the network. If nobody reads for a while the
    /// choice is to buffer without bound or to lose packets, and losing them
    /// while <see cref="Dropped"/> says so is the honest half of it. 4096 is two
    /// orders above what a busy world channel produces between two polls.
    /// </remarks>
    public const int QueueCapacity = 4096;

    private readonly IntPtr _handle;
    private readonly BlockingCollection<CapturedPacket> _queue = new(QueueCapacity);
    private readonly Thread _pump;
    private long _dropped;
    private bool _disposed;

    private WinDivertPacketSource(IntPtr handle, IPAddress serverAddress, int serverPort)
    {
        _handle = handle;
        ServerAddress = serverAddress;
        ServerPort = serverPort;

        // The blocking recv happens here and not on the caller's thread. The
        // interface promises TryRead honours a timeout, every consumer is written
        // against that promise, and WinDivertRecv cannot keep it: it returns when
        // a packet matches the filter or when the handle closes, and nothing else.
        // The operator API hung on every JSON route because a snapshot ended up
        // waiting inside it, and the recorder could not be stopped with Ctrl+C for
        // the same reason.
        _pump = new Thread(Pump)
        {
            IsBackground = true,
            Name = "windivert-recv",
        };
        _pump.Start();
    }

    public IPAddress ServerAddress { get; }
    public int ServerPort { get; }

    /// <summary>Packets the driver handed over that no reader took in time.</summary>
    /// <remarks>
    /// Non-zero means the capture is incomplete, which a caller reporting on what
    /// it observed has to be able to say. Silent loss would make a quiet session
    /// and a starved reader look identical.
    /// </remarks>
    public long Dropped => Interlocked.Read(ref _dropped);

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

    /// <summary>
    /// Takes the next captured packet, waiting no longer than
    /// <paramref name="timeout"/>.
    /// </summary>
    /// <remarks>
    /// The timeout is real. It used to be documented and ignored, which is worse
    /// than not offering one: every caller was written to rely on it, and each of
    /// them inherited a hang instead of a false.
    /// </remarks>
    public bool TryRead(TimeSpan timeout, out CapturedPacket packet)
    {
        packet = default;
        if (_disposed)
            return false;

        TimeSpan wait = timeout < TimeSpan.Zero ? TimeSpan.Zero : timeout;
        try
        {
            return _queue.TryTake(out packet, wait);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            // Disposed underneath the caller, or the pump completed the queue.
            // Both mean there is nothing more to read, which is a false.
            return false;
        }
    }

    /// <summary>Why the pump stopped, or null while it is running.</summary>
    /// <remarks>
    /// A capture that died has to be able to say so. Without this the queue simply
    /// stops filling and every reader sees a quiet wire, which is the same shape
    /// as a quiet game.
    /// </remarks>
    public string? PumpFailure { get; private set; }

    /// <summary>The blocking recv, kept on its own thread so callers get a timeout.</summary>
    /// <remarks>
    /// Nothing here is allowed to escape. An unhandled exception on any thread
    /// ends the process in .NET, so a capture thread that threw would take the
    /// whole runtime with it — silently, from the operator's side, because the
    /// window just closes. Observation must not be able to kill the thing it is
    /// observing for.
    /// </remarks>
    private void Pump()
    {
        try
        {
            PumpLoop();
        }
        catch (Exception ex)
        {
            PumpFailure = $"windivert_pump_failed:{ex.GetType().Name}";
        }
    }

    private void PumpLoop()
    {
        // WINDIVERT_ADDRESS is opaque to this source: direction is labelled from
        // the parsed endpoint, not from the driver's metadata, so the address
        // struct is a scratch sink. One buffer for the life of the thread —
        // nothing else touches it.
        var buffer = new byte[65535];
        var address = new byte[64];

        while (!_disposed)
        {
            if (!WinDivertRecv(_handle, buffer, (uint)buffer.Length, out uint received, address))
            {
                // The handle was closed, or the driver is shutting down
                // (ERROR_NO_DATA). Either way there is nothing further to read.
                return;
            }

            if (received == 0)
                continue;

            var raw = new byte[received];
            Array.Copy(buffer, raw, received);

            try
            {
                if (!_queue.TryAdd(new CapturedPacket(DateTime.UtcNow, raw)))
                    Interlocked.Increment(ref _dropped);
            }
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
            {
                return; // disposed while this packet was in hand
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        // Closing the handle is what makes the pump's pending recv return; the
        // queue is completed afterwards so a reader waiting on it gets a false
        // rather than the full timeout.
        if (_handle != IntPtr.Zero && _handle != new IntPtr(-1))
            WinDivertClose(_handle);

        try
        {
            _queue.CompleteAdding();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    [DllImport("WinDivert.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr WinDivertOpen(string filter, short layer, short priority, ulong flags);

    [DllImport("WinDivert.dll", SetLastError = true)]
    private static extern bool WinDivertRecv(IntPtr handle, byte[] packet, uint packetLen, out uint recvLen, byte[] address);

    [DllImport("WinDivert.dll", SetLastError = true)]
    private static extern bool WinDivertClose(IntPtr handle);
}
