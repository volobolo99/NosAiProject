using System.Net;

namespace NosAi.LiveIntegration.Capture;

/// <summary>
/// A finite packet source over a fixed list, for tests and synthetic sessions.
/// </summary>
/// <remarks>
/// The engine cannot tell this from a live capture or a recording, which is the
/// whole reason the source is an interface: reassembly and framing get exercised
/// against a scripted session — out of order, retransmitted, gapped — with no
/// driver and no game running.
/// </remarks>
public sealed class InMemoryPacketSource : IPacketSource, IFinitePacketSource
{
    private readonly IReadOnlyList<CapturedPacket> _packets;
    private int _index;

    public InMemoryPacketSource(IPAddress serverAddress, int serverPort, IReadOnlyList<CapturedPacket> packets)
    {
        ServerAddress = serverAddress ?? throw new ArgumentNullException(nameof(serverAddress));
        ServerPort = serverPort;
        _packets = packets ?? throw new ArgumentNullException(nameof(packets));
    }

    public IPAddress ServerAddress { get; }
    public int ServerPort { get; }
    public bool Ended => _index >= _packets.Count;

    public bool TryRead(TimeSpan timeout, out CapturedPacket packet)
    {
        if (Ended)
        {
            packet = default;
            return false;
        }
        packet = _packets[_index++];
        return true;
    }

    public void Dispose() { }
}
