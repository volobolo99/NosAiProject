using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace NosAi.LiveIntegration;

/// <summary>TCP states as Windows reports them, in its own numbering.</summary>
public enum ClientTcpState
{
    Unknown = 0,
    Closed = 1,
    Listen = 2,
    SynSent = 3,
    SynReceived = 4,
    Established = 5,
    FinWait1 = 6,
    FinWait2 = 7,
    CloseWait = 8,
    Closing = 9,
    LastAck = 10,
    TimeWait = 11,
    DeleteTcb = 12
}

/// <summary>One TCP connection owned by the client process.</summary>
public sealed record ClientTcpConnection(IPEndPoint Local, IPEndPoint Remote, ClientTcpState State)
{
    /// <summary>Whether this is a live conversation with something off this machine.</summary>
    public bool IsRemoteSession =>
        State == ClientTcpState.Established &&
        !IPAddress.IsLoopback(Remote.Address) &&
        !Remote.Address.Equals(IPAddress.Any) &&
        !Remote.Address.Equals(IPAddress.IPv6Any);

    public override string ToString() => $"{Local} -> {Remote} [{State}]";
}

/// <summary>
/// What the client's network looks like from outside it.
/// </summary>
/// <param name="Primary">
/// The one remote session, when there is exactly one. Null when there are none
/// and null when there are several: picking the "game one" out of a crowd would
/// be a guess, and a guess presented as an observation is what the classification
/// discipline exists to stop.
/// </param>
/// <param name="FailureReason">
/// Why the observation is not trustworthy, or null when it is. Present means
/// `UNKNOWN`, not "no connections".
/// </param>
public sealed record ClientNetworkObservation(
    IReadOnlyList<ClientTcpConnection> Connections,
    ClientTcpConnection? Primary,
    string? FailureReason)
{
    public static ClientNetworkObservation Failed(string reason) =>
        new(Array.Empty<ClientTcpConnection>(), null, reason);

    public bool Observed => FailureReason is null;

    /// <summary>Remote sessions only: the ones that could be a server.</summary>
    public IReadOnlyList<ClientTcpConnection> RemoteSessions =>
        Connections.Where(c => c.IsRemoteSession).ToArray();
}

/// <summary>
/// Reads which TCP connections the game client holds, by asking Windows.
/// </summary>
/// <remarks>
/// <para>
/// This is the first thing the runtime needs to know about the client's network
/// and the one it has never had: the snapshot knows the process exists and the
/// window responds, and nothing about whether it is talking to a server at all.
/// A disconnection is currently invisible to it.
/// </para>
/// <para>
/// <b>What this is and is not.</b> It asks the operating system which sockets a
/// process owns — the same class of fact as its PID and window title, both
/// already `LIVE`. It reads no payload, opens no capture, touches no other
/// process. Seeing the bytes exchanged is a separate problem with a separate
/// answer (ADR-0014) and a driver requirement this does not have: nothing here
/// needs elevation, and it works while the client is running normally.
/// </para>
/// <para>
/// IPv4 and IPv6 are both read. Reading only one and reporting zero for the other
/// would be a false negative, which is worse than saying nothing.
/// </para>
/// </remarks>
public static class ClientNetworkObserver
{
    private const int AfInet = 2;
    private const int AfInet6 = 23;

    /// <summary>TCP_TABLE_OWNER_PID_ALL.</summary>
    private const int TableClassOwnerPidAll = 5;

    private const uint NoError = 0;
    private const uint ErrorInsufficientBuffer = 122;

    /// <summary>Stops a pathological table from being retried forever.</summary>
    private const int MaxSizeRetries = 5;

    /// <summary>
    /// Observes the connections owned by <paramref name="processId"/>.
    /// </summary>
    /// <remarks>
    /// A failure returns a reason rather than an empty list: "the client has no
    /// connections" and "we could not look" are different facts, and conflating
    /// them would let a broken probe read as a disconnected game.
    /// </remarks>
    public static ClientNetworkObservation Observe(int processId)
    {
        if (processId <= 0)
            return ClientNetworkObservation.Failed("invalid_process_id");

        if (!OperatingSystem.IsWindows())
            return ClientNetworkObservation.Failed("tcp_table_unavailable_off_windows");

        List<ClientTcpConnection> connections;
        try
        {
            connections = new List<ClientTcpConnection>();
            connections.AddRange(ReadIPv4(processId));
            connections.AddRange(ReadIPv6(processId));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return ClientNetworkObservation.Failed("iphlpapi_unavailable");
        }
        catch (Exception ex) when (ex is InvalidOperationException or OutOfMemoryException)
        {
            return ClientNetworkObservation.Failed($"tcp_table_read_failed:{ex.GetType().Name}");
        }

        var remote = connections.Where(c => c.IsRemoteSession).ToList();

        // Exactly one remote session is identifiable. Several are not: an
        // updater, a launcher and the game itself look alike from here, and
        // naming one of them "the server" would be an invention.
        var primary = remote.Count == 1 ? remote[0] : null;

        return new ClientNetworkObservation(connections, primary, null);
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<ClientTcpConnection> ReadIPv4(int processId)
    {
        foreach (var buffer in ReadTable(AfInet))
        {
            int count = Marshal.ReadInt32(buffer);
            int rowSize = Marshal.SizeOf<TcpRowOwnerPid>();
            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<TcpRowOwnerPid>(buffer + 4 + (i * rowSize));
                if (row.OwningPid != (uint)processId)
                    continue;

                yield return new ClientTcpConnection(
                    new IPEndPoint(new IPAddress(BitConverter.GetBytes(row.LocalAddr)), NetworkPort(row.LocalPort)),
                    new IPEndPoint(new IPAddress(BitConverter.GetBytes(row.RemoteAddr)), NetworkPort(row.RemotePort)),
                    ToState(row.State));
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<ClientTcpConnection> ReadIPv6(int processId)
    {
        foreach (var buffer in ReadTable(AfInet6))
        {
            int count = Marshal.ReadInt32(buffer);
            int rowSize = Marshal.SizeOf<Tcp6RowOwnerPid>();
            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<Tcp6RowOwnerPid>(buffer + 4 + (i * rowSize));
                if (row.OwningPid != (uint)processId)
                    continue;

                yield return new ClientTcpConnection(
                    new IPEndPoint(new IPAddress(row.LocalAddr, row.LocalScopeId), NetworkPort(row.LocalPort)),
                    new IPEndPoint(new IPAddress(row.RemoteAddr, row.RemoteScopeId), NetworkPort(row.RemotePort)),
                    ToState(row.State));
            }
        }
    }

    /// <summary>
    /// Allocates and fills one connection table, then frees it.
    /// </summary>
    /// <remarks>
    /// The size is asked for first and can change between the two calls — a
    /// connection opening in that window grows the table — so an insufficient
    /// buffer is retried rather than treated as an error. Bounded, because a
    /// machine that keeps growing the table faster than we can read it should
    /// report a failure rather than spin.
    /// </remarks>
    [SupportedOSPlatform("windows")]
    private static IEnumerable<IntPtr> ReadTable(int addressFamily)
    {
        int size = 0;
        for (int attempt = 0; attempt < MaxSizeRetries; attempt++)
        {
            uint status = GetExtendedTcpTable(IntPtr.Zero, ref size, false, addressFamily, TableClassOwnerPidAll, 0);
            if (status != ErrorInsufficientBuffer && status != NoError)
                yield break; // No table for this family: report nothing rather than guess.

            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                status = GetExtendedTcpTable(buffer, ref size, false, addressFamily, TableClassOwnerPidAll, 0);
                if (status == ErrorInsufficientBuffer)
                    continue; // It grew; ask again with the new size.
                if (status != NoError)
                    yield break;

                yield return buffer;
                yield break;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    /// <summary>Ports arrive in network byte order in the low two bytes.</summary>
    private static int NetworkPort(uint value) => (int)(((value & 0xFF) << 8) | ((value >> 8) & 0xFF));

    private static ClientTcpState ToState(uint state) =>
        state is >= 1 and <= 12 ? (ClientTcpState)state : ClientTcpState.Unknown;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr pTcpTable, ref int pdwSize, bool bOrder, int ulAf, int tableClass, int reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    /// <summary>
    /// The IPv6 row. Its fields are <b>not</b> in the same order as the IPv4 one:
    /// the state sits near the end, after both addresses. Copying the v4 layout
    /// here would produce addresses and states that look plausible and are wrong.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Tcp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddr;
        public uint LocalScopeId;
        public uint LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] RemoteAddr;
        public uint RemoteScopeId;
        public uint RemotePort;
        public uint State;
        public uint OwningPid;
    }
}
