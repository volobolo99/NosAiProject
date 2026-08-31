using System.Net;

namespace NosAi.LiveIntegration.Capture;

/// <summary>One raw packet as a source handed it over, with when and where.</summary>
/// <remarks>
/// The raw IPv4 bytes, not a parsed segment: parsing lives in one place
/// (<see cref="Ipv4TcpParser"/>), so a source only has to deliver bytes and a
/// timestamp. Direction is left to the parser against the known endpoint, which
/// is the single fact that decides it.
/// </remarks>
public readonly record struct CapturedPacket(DateTime TimestampUtc, ReadOnlyMemory<byte> Raw);

/// <summary>
/// Where captured packets come from.
/// </summary>
/// <remarks>
/// <para>
/// The seam that keeps the capture engine testable without a kernel driver.
/// WinDivert is one implementation; a recorded file and an in-memory list are
/// others, and the engine cannot tell them apart. That is the point: the
/// reassembly and framing logic is exercised against synthetic and recorded
/// traffic in CI, and only the source itself needs a real device.
/// </para>
/// <para>
/// Pull, not push: the caller drives the pace, so a replay can run as fast as it
/// likes and a live capture blocks until a packet or the timeout. A source that
/// has ended returns false forever rather than throwing.
/// </para>
/// </remarks>
public interface IPacketSource : IDisposable
{
    /// <summary>The server side of the conversation, used to label direction.</summary>
    IPAddress ServerAddress { get; }

    /// <summary>The server port.</summary>
    int ServerPort { get; }

    /// <summary>
    /// Tries to read the next packet within <paramref name="timeout"/>.
    /// </summary>
    /// <returns>
    /// True with a packet; false on timeout or end of source. A false is not an
    /// error — a live source simply had nothing to hand over in the window.
    /// </returns>
    bool TryRead(TimeSpan timeout, out CapturedPacket packet);
}
