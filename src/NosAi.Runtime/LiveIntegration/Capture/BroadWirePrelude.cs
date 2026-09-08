using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices;

namespace NosAi.LiveIntegration.Capture;

/// <summary>
/// The packets of one endpoint, trimmed out of the wide prelude buffer, with the
/// counters that describe everything the prelude saw and dropped up to that point.
/// </summary>
/// <remarks>
/// <see cref="BroadWirePrelude.FocusOn"/> produces exactly one slice, and only then
/// does traffic become readable: the packets are the ones of the chosen endpoint,
/// in the chronological order they entered the prelude. The counters are a snapshot
/// taken at the moment of the trim; they keep counting afterwards, but this slice
/// does not move with them.
/// </remarks>
public readonly record struct PreludeSlice(
    IReadOnlyList<CapturedPacket> Packets,
    long PacketsSeen,
    long DroppedForCapacity,
    long DroppedForAge);

/// <summary>
/// Captures broadly before the game client's endpoint is known, keeps packets in an
/// in-memory circular buffer, and once the endpoint is known trims the buffer to
/// that endpoint's packets alone.
/// </summary>
/// <remarks>
/// <para>
/// The problem this class solves is ordering. Registration used to need an operator
/// because the capture filter was pinned to an endpoint that does not exist until
/// the client connects, by which time the handshake is already past. A prelude arms
/// itself first with a wide, passive filter, and the endpoint, once discovered, only
/// decides which buffered packets survive.
/// </para>
/// <para>
/// Privacy is structural, not a convention. While the prelude is wide, packets live
/// only in the private buffer below, and the only ways a packet leaves the object
/// are <see cref="FocusOn"/> (the trimmed packets of the chosen endpoint) and
/// <see cref="TryRead"/> (packets of that same endpoint after the trim). No member
/// of this class writes packets to disk, to a log, to the console or to a
/// diagnostics file, and the wide buffer is never exposed by any property or method.
/// </para>
/// <para>
/// Two feeding shapes exist. Constructed with an <see cref="IPacketSource"/>, the
/// class pumps that source on a background thread and the caller only reads.
/// Constructed without one, the caller pushes packets with <see cref="Accept"/>
/// itself — the shape tests and replays use. <see cref="TryOpen"/> is the third
/// door: it owns a WinDivert handle in sniff-only mode and needs no source object
/// at all.
/// </para>
/// </remarks>
public sealed class BroadWirePrelude : IDisposable
{
    /// <summary>The wide filter: every IPv4/TCP packet that is not loopback traffic.</summary>
    /// <remarks>
    /// Sniff-only by construction — the flags used with it never inject and never
    /// drop — so the prelude can observe a conversation it does not yet know the
    /// address of without changing what the machine sends or receives.
    /// </remarks>
    public const string BroadFilter = "ip and tcp and !loopback";

    /// <summary>Default budget for the wide in-memory buffer, 32 MiB.</summary>
    public const int DefaultCapacityBytes = 32 * 1024 * 1024;

    /// <summary>Default age window: a packet older than this is not worth keeping.</summary>
    public const int DefaultWindowSeconds = 180;

    /// <summary>Capacity of the post-trim queue handed to consumers via <see cref="TryRead"/>.</summary>
    public const int FocusedQueueCapacity = 8192;

    private const string PumpThreadName = "broad-wire-pump";
    private const string RecvThreadName = "broad-wire-recv";
    private static readonly TimeSpan PumpPollInterval = TimeSpan.FromMilliseconds(250);

    private const int RecvBufferBytes = 65535;
    private const int WinDivertAddressBytes = 64;

    private const short LayerNetwork = 0;
    private const ulong FlagSniff = 0x0001UL;
    private const ulong FlagRecvOnly = 0x0004UL;

    private readonly object _gate = new();
    private readonly Queue<CapturedPacket> _wideQueue = new();
    private readonly BlockingCollection<CapturedPacket> _focused;
    private readonly IPacketSource? _source;
    private readonly IntPtr _divertHandle;
    private readonly int _capacityBytes;
    private readonly TimeSpan _window;
    private readonly Func<DateTime> _clock;

    private long _packetsSeen;
    private long _droppedForCapacity;
    private long _droppedForAge;
    private long _bufferedBytes;

    private volatile bool _disposed;
    private volatile bool _isFocused;
    private volatile bool _sourceEnded;
    private volatile string? _pumpFailure;
    private volatile IPAddress? _focusAddress;
    private volatile int _focusPort;

    /// <summary>
    /// Creates a prelude fed by an optional packet source.
    /// </summary>
    /// <remarks>
    /// <paramref name="source"/> being null is deliberate: nothing starts, and the
    /// caller is the pump, calling <see cref="Accept"/> from its own loop. With a
    /// source, a background thread polls it and feeds <see cref="Accept"/>; the
    /// thread exists only to observe, and never outlives this object.
    /// </remarks>
    public BroadWirePrelude(
        IPacketSource? source = null,
        int capacityBytes = DefaultCapacityBytes,
        TimeSpan? window = null,
        Func<DateTime>? clock = null)
        : this(source, IntPtr.Zero, capacityBytes, window ?? TimeSpan.FromSeconds(DefaultWindowSeconds), clock ?? (static () => DateTime.UtcNow))
    {
    }

    private BroadWirePrelude(IPacketSource? source, IntPtr divertHandle, int capacityBytes, TimeSpan window, Func<DateTime> clock)
    {
        if (capacityBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacityBytes), capacityBytes, "Capacity must be greater than zero.");
        if (window <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(window), window, "Window must be greater than zero.");

        _source = source;
        _divertHandle = divertHandle;
        _capacityBytes = capacityBytes;
        _window = window;
        _clock = clock;
        _focused = new BlockingCollection<CapturedPacket>(FocusedQueueCapacity);

        if (source is not null)
            new Thread(PumpLoop) { IsBackground = true, Name = PumpThreadName }.Start();
        else if (divertHandle != IntPtr.Zero)
            new Thread(RecvLoop) { IsBackground = true, Name = RecvThreadName }.Start();
    }

    /// <summary>
    /// Opens the wide WinDivert filter, or explains why that was not possible.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The filter is open in sniff-only mode, exactly the pair of flags that lets
    /// the prelude see traffic without intercepting it. A missing or refused driver
    /// is a reason, not an exception: every failure path returns null with
    /// <paramref name="failureReason"/> set, and no exception escapes this method.
    /// </para>
    /// <para>
    /// The <paramref name="capacityBytes"/> and <paramref name="window"/> arguments
    /// keep the same meaning and validation as the constructor, and are checked
    /// before the handle is opened so that invalid arguments never leak one.
    /// </para>
    /// </remarks>
    public static BroadWirePrelude? TryOpen(
        out string? failureReason,
        int capacityBytes = DefaultCapacityBytes,
        TimeSpan? window = null)
    {
        failureReason = null;

        if (capacityBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacityBytes), capacityBytes, "Capacity must be greater than zero.");
        TimeSpan resolvedWindow = window ?? TimeSpan.FromSeconds(DefaultWindowSeconds);
        if (resolvedWindow <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(window), resolvedWindow, "Window must be greater than zero.");

        if (!OperatingSystem.IsWindows())
        {
            failureReason = "windivert_unavailable_off_windows";
            return null;
        }

        IntPtr handle;
        try
        {
            handle = WinDivertOpen(BroadFilter, LayerNetwork, 0, FlagSniff | FlagRecvOnly);
        }
        catch (DllNotFoundException)
        {
            failureReason = "windivert_dll_not_found";
            return null;
        }
        catch (BadImageFormatException)
        {
            failureReason = "windivert_dll_wrong_architecture";
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            failureReason = "windivert_entrypoint_missing";
            return null;
        }

        if (handle == new IntPtr(-1))
        {
            int error = Marshal.GetLastWin32Error();
            failureReason = error switch
            {
                5 => "access_denied_run_elevated",
                2 => "windivert_driver_not_found",
                577 => "driver_signature_rejected",
                1275 => "driver_blocked",
                _ => $"windivert_open_failed:{error}",
            };
            return null;
        }

        return new BroadWirePrelude(null, handle, capacityBytes, resolvedWindow, static () => DateTime.UtcNow);
    }

    /// <summary>True once <see cref="FocusOn"/> has trimmed the prelude to one endpoint.</summary>
    public bool IsFocused => _isFocused;

    /// <summary>The endpoint the prelude is trimmed to, once it is focused.</summary>
    public IPAddress? FocusAddress => _focusAddress;

    /// <summary>The server port of the focused endpoint.</summary>
    public int FocusPort => _focusPort;

    /// <summary>How many packets the wide buffer currently holds.</summary>
    public int BufferedPackets
    {
        get
        {
            lock (_gate)
            {
                return _wideQueue.Count;
            }
        }
    }

    /// <summary>How many raw bytes the wide buffer currently holds.</summary>
    public long BufferedBytes
    {
        get
        {
            lock (_gate)
            {
                return _bufferedBytes;
            }
        }
    }

    /// <summary>Total packets handed to <see cref="Accept"/>, before and after the trim.</summary>
    public long PacketsSeen
    {
        get
        {
            lock (_gate)
            {
                return _packetsSeen;
            }
        }
    }

    /// <summary>Packets dropped because the wide buffer, or the focused queue, was full.</summary>
    public long DroppedForCapacity
    {
        get
        {
            lock (_gate)
            {
                return _droppedForCapacity;
            }
        }
    }

    /// <summary>Packets dropped from the wide buffer because they outlived the age window.</summary>
    public long DroppedForAge
    {
        get
        {
            lock (_gate)
            {
                return _droppedForAge;
            }
        }
    }

    /// <summary>True when the feeding source has ended, or when <see cref="CompleteInput"/> was called.</summary>
    public bool SourceEnded => _sourceEnded;

    /// <summary>Set when the background feeder died on an exception; null while it is healthy.</summary>
    /// <remarks>
    /// A background thread that throws unhandled kills the process, and an observer
    /// must never be able to kill what it observes. So the feeder catches everything
    /// and reports here instead, in a stable machine-readable shape.
    /// </remarks>
    public string? PumpFailure => _pumpFailure;

    /// <summary>
    /// Feeds one captured packet into the prelude.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Before <see cref="FocusOn"/> the packet is parked in the wide circular
    /// buffer: the oldest packets leave when the byte budget is exceeded, and
    /// packets older than the age window leave regardless. After the trim the same
    /// call is the filter: a packet that does not match the focused endpoint is
    /// discarded immediately, without being buffered anywhere and without counting
    /// as dropped.
    /// </para>
    /// <para>
    /// Safe to call from the pumping thread while another thread trims or reads.
    /// After disposal it is a silent no-op, so a feeder racing the end of its
    /// observer cannot crash it.
    /// </para>
    /// </remarks>
    public void Accept(CapturedPacket packet)
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _packetsSeen++;

            if (!_isFocused)
            {
                _wideQueue.Enqueue(packet);
                _bufferedBytes += packet.Raw.Length;

                // Capacity first: the budget is on bytes, so the head leaves until
                // the newest packet fits again.
                while (_bufferedBytes > _capacityBytes && _wideQueue.Count > 0)
                {
                    CapturedPacket oldest = _wideQueue.Dequeue();
                    _bufferedBytes -= oldest.Raw.Length;
                    _droppedForCapacity++;
                }

                // Age second: anything the cutoff says is stale is not worth the
                // bytes it still occupies.
                DateTime cutoff = _clock().ToUniversalTime() - _window;
                while (_wideQueue.Count > 0 && _wideQueue.Peek().TimestampUtc < cutoff)
                {
                    CapturedPacket oldest = _wideQueue.Dequeue();
                    _bufferedBytes -= oldest.Raw.Length;
                    _droppedForAge++;
                }

                return;
            }

            // Focused phase: the endpoint is known, so only its traffic exists here.
            // The injectable clock of the wide phase has nothing to say anymore;
            // the bounded queue below is the only policy left, and it is counted as
            // capacity loss when it fills.
            if (!MatchesEndpoint(Ipv4TcpParser.Parse(packet.Raw.Span), _focusAddress!, _focusPort))
                return;

            bool added;
            try
            {
                added = _focused.TryAdd(packet);
            }
            catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
            {
                return;
            }

            if (!added)
                _droppedForCapacity++;
        }
    }

    /// <summary>
    /// Trims the wide buffer to one endpoint and returns that endpoint's packets.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A packet survives when the IPv4/TCP parse succeeds and the endpoint appears
    /// as its source or its destination; everything else — all the unrelated
    /// applications that share the wire — is thrown away. Clearing the whole wide
    /// buffer is not an optimization: traffic that does not concern the game must
    /// not outlive the trim, and after this call no packet enters the wide buffer
    /// again.
    /// </para>
    /// <para>
    /// Can only be called once; a second call is a programming error and throws.
    /// The returned slice is a snapshot of the counters at the moment of the trim.
    /// </para>
    /// </remarks>
    public PreludeSlice FocusOn(IPAddress serverAddress, int serverPort)
    {
        ArgumentNullException.ThrowIfNull(serverAddress);
        if (serverPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(serverPort), serverPort, "Port must be in 1..65535.");

        PreludeSlice slice;
        lock (_gate)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BroadWirePrelude));
            if (_isFocused)
                throw new InvalidOperationException("prelude_already_focused");

            // Scan in insertion order: the queue head is the oldest packet, so
            // walking it front to back yields the chronological order promised.
            var kept = new List<CapturedPacket>();
            foreach (CapturedPacket packet in _wideQueue)
            {
                if (MatchesEndpoint(Ipv4TcpParser.Parse(packet.Raw.Span), serverAddress, serverPort))
                    kept.Add(packet);
            }

            // The wide buffer dies here, completely and forever: focused traffic
            // goes through the bounded queue instead, so nothing is left behind.
            _wideQueue.Clear();
            _bufferedBytes = 0;

            _focusAddress = serverAddress;
            _focusPort = serverPort;
            _isFocused = true;

            slice = new PreludeSlice(kept, _packetsSeen, _droppedForCapacity, _droppedForAge);
        }

        return slice;
    }

    /// <summary>
    /// Takes one focused packet, waiting up to <paramref name="timeout"/>.
    /// </summary>
    /// <remarks>
    /// Before <see cref="FocusOn"/> this always returns false, unconditionally and
    /// without waiting: the wide traffic is never handed to anyone, which is the
    /// structural guarantee that it cannot leak out of this object. After the trim
    /// it waits like a bounded queue, and reports false on timeout.
    /// </remarks>
    public bool TryRead(TimeSpan timeout, out CapturedPacket packet)
    {
        packet = default;

        bool open;
        lock (_gate)
        {
            open = !_disposed && _isFocused;
        }

        if (!open)
            return false;

        if (timeout < TimeSpan.Zero)
            timeout = TimeSpan.Zero;

        try
        {
            return _focused.TryTake(out packet, timeout);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Declares that no more packets will be fed, so readers can finish instead of
    /// waiting out their timeout.
    /// </summary>
    /// <remarks>
    /// Also how a finite source's end is surfaced to whoever pumps this prelude:
    /// after this, a draining reader observes the queue empty and returns false.
    /// Calling it again, or calling it after disposal, is harmless.
    /// </remarks>
    public void CompleteInput()
    {
        _sourceEnded = true;
        CompleteAddingOnce();
    }

    /// <summary>
    /// Stops the feeder, closes the capture handle, and releases the source.
    /// </summary>
    /// <remarks>
    /// Order matters: the disposed flag goes first so no feeder can enqueue
    /// anything new, then the WinDivert handle is closed — closing it is what makes
    /// a pending receive return — then readers are told no more packets will come,
    /// the wide buffer is emptied, and the source is released. Safe to call more
    /// than once.
    /// </remarks>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        if (_divertHandle != IntPtr.Zero && _divertHandle != new IntPtr(-1))
            WinDivertClose(_divertHandle);

        CompleteAddingOnce();

        lock (_gate)
        {
            _wideQueue.Clear();
            _bufferedBytes = 0;
        }

        _source?.Dispose();
    }

    /// <summary>
    /// Marks the focused queue as complete, tolerating both a disposal and an
    /// already-completed collection, so a second call never throws.
    /// </summary>
    private void CompleteAddingOnce()
    {
        try
        {
            _focused.CompleteAdding();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    /// <summary>
    /// Pumps an <see cref="IPacketSource"/> into <see cref="Accept"/> until it ends.
    /// </summary>
    /// <remarks>
    /// The 250 ms poll is the compromise between waking up for a packet promptly
    /// and not burning a core spinning on an idle wire. A finite source that has
    /// ended is surfaced through <see cref="SourceEnded"/> and the thread stops; a
    /// throwing source is surfaced through <see cref="PumpFailure"/> and the thread
    /// stops too, because an unhandled exception on a background thread would kill
    /// the whole process.
    /// </remarks>
    private void PumpLoop()
    {
        try
        {
            IPacketSource? source = _source;
            if (source is null)
                return;

            while (!_disposed)
            {
                if (!source.TryRead(PumpPollInterval, out CapturedPacket packet))
                {
                    if (source is IFinitePacketSource { Ended: true })
                    {
                        _sourceEnded = true;
                        return;
                    }

                    continue;
                }

                Accept(packet);
            }
        }
        catch (Exception ex)
        {
            _pumpFailure = $"broad_prelude_pump_failed:{ex.GetType().Name}";
        }
    }

    /// <summary>
    /// Receives from the WinDivert handle into <see cref="Accept"/> until disposed.
    /// </summary>
    /// <remarks>
    /// The receive buffer is created once, for this thread alone, and nothing else
    /// ever touches it; each packet is copied into its own array before it is
    /// handed over, so the buffer can be reused for the next receive immediately.
    /// When the handle is closed from <see cref="Dispose"/>, the pending receive
    /// returns false and the thread ends.
    /// </remarks>
    private void RecvLoop()
    {
        try
        {
            var buffer = new byte[RecvBufferBytes];
            var address = new byte[WinDivertAddressBytes];

            while (!_disposed)
            {
                if (!WinDivertRecv(_divertHandle, buffer, (uint)buffer.Length, out uint received, address))
                    return;

                if (received == 0)
                    continue;

                var raw = new byte[(int)received];
                Buffer.BlockCopy(buffer, 0, raw, 0, (int)received);
                Accept(new CapturedPacket(DateTime.UtcNow, raw));
            }
        }
        catch (Exception ex)
        {
            _pumpFailure = $"broad_prelude_recv_failed:{ex.GetType().Name}";
        }
    }

    /// <summary>
    /// True when a parsed packet has the endpoint on either side of the conversation.
    /// </summary>
    private static bool MatchesEndpoint(ParsedPacket parsed, IPAddress address, int port)
    {
        if (!parsed.Ok)
            return false;
        if (parsed.Source is not null && parsed.Source.Equals(address) && parsed.SourcePort == port)
            return true;
        if (parsed.Destination is not null && parsed.Destination.Equals(address) && parsed.DestinationPort == port)
            return true;
        return false;
    }

    [DllImport("WinDivert.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr WinDivertOpen(string filter, short layer, short priority, ulong flags);

    [DllImport("WinDivert.dll", SetLastError = true)]
    private static extern bool WinDivertRecv(IntPtr handle, byte[] packet, uint packetLen, out uint recvLen, byte[] address);

    [DllImport("WinDivert.dll", SetLastError = true)]
    private static extern bool WinDivertClose(IntPtr handle);
}
