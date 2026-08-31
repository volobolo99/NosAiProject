namespace NosAi.LiveIntegration.Capture;

/// <summary>Which side of a conversation a segment travelled.</summary>
public enum StreamDirection
{
    /// <summary>PC to server: what the client sent.</summary>
    Outbound = 0,

    /// <summary>Server to PC: what the client received.</summary>
    Inbound = 1
}

/// <summary>
/// One TCP segment, reduced to what reassembly needs.
/// </summary>
/// <remarks>
/// <para>
/// This is the boundary between "a captured packet" and "the byte stream an
/// application saw". Everything above it works in application bytes and never
/// touches a packet again; everything below it — WinDivert, a pcap file — only
/// has to produce these.
/// </para>
/// <para>
/// <see cref="SequenceNumber"/> is the raw 32-bit TCP sequence, wrap included.
/// The reassembler does the modular arithmetic; carrying the raw value keeps this
/// type a faithful record of the wire rather than an interpretation of it.
/// </para>
/// </remarks>
public readonly record struct TcpSegment(
    StreamDirection Direction,
    uint SequenceNumber,
    ReadOnlyMemory<byte> Payload,
    bool Syn,
    bool Fin,
    bool Reset)
{
    /// <summary>Bytes this segment occupies in sequence space.</summary>
    /// <remarks>
    /// SYN and FIN each consume one sequence number even though they carry no
    /// payload byte. Ignoring that would put every following byte one position
    /// wrong, which is the classic off-by-one that makes a decoder read garbage.
    /// </remarks>
    public uint SequenceLength => (uint)Payload.Length + (Syn ? 1u : 0u) + (Fin ? 1u : 0u);

    /// <summary>The sequence number the first payload byte sits at.</summary>
    /// <remarks>SYN occupies the sequence before the payload, so payload starts after it.</remarks>
    public uint PayloadStartSequence => Syn ? SequenceNumber + 1u : SequenceNumber;
}
