namespace NosAi.LiveIntegration.Capture;

/// <summary>
/// Rebuilds one direction of a TCP conversation into the ordered byte stream the
/// application actually received.
/// </summary>
/// <remarks>
/// <para>
/// This is the layer everything above the network depends on and the one most
/// easily got wrong. TCP does not hand a receiver clean bytes: segments arrive
/// out of order, get retransmitted whole or partially overlapping, and the
/// sequence number wraps through zero every 4 GB. A framer that read raw payloads
/// in arrival order would decode nonsense the first time any of that happened,
/// and — worse — it would look like it was working until then.
/// </para>
/// <para>
/// The rule this class holds to is the one the classification discipline needs:
/// it emits only bytes it can place <b>contiguously</b> from where it last was.
/// A gap in the stream stops output at the gap; the bytes past it are held, not
/// skipped, until the missing piece arrives. Skipping a gap would splice
/// unrelated bytes together and present the result as a continuous stream, which
/// is the network-level version of inventing a value.
/// </para>
/// <para>
/// Sequence comparison is serial (RFC 1982): <c>(int)(a - b)</c> is negative when
/// <c>a</c> precedes <c>b</c>, which is correct across the wrap because the
/// subtraction wraps with it. Comparing the raw uint32s would misorder every
/// segment near the wrap boundary.
/// </para>
/// </remarks>
public sealed class TcpStreamReassembler
{
    // Held out-of-order data, keyed by the sequence of its first byte. Overlaps
    // are trimmed before insertion, so no two entries cover the same byte.
    private readonly SortedDictionary<uint, byte[]> _pending = new(SerialComparer.Instance);
    private uint _nextSequence;
    private bool _anchored;
    private long _deliveredBytes;

    /// <summary>Whether the first byte position has been fixed yet.</summary>
    public bool IsAnchored => _anchored;

    /// <summary>Total bytes handed out in order so far.</summary>
    public long DeliveredBytes => _deliveredBytes;

    /// <summary>Bytes waiting past a gap, not yet deliverable.</summary>
    public int PendingBytes
    {
        get
        {
            var total = 0;
            foreach (var block in _pending.Values)
                total += block.Length;
            return total;
        }
    }

    /// <summary>
    /// Fixes the sequence number the stream starts at.
    /// </summary>
    /// <remarks>
    /// A SYN is the honest anchor: it marks the byte before the first data byte,
    /// so capture caught the connection from its start. Absent a SYN — capture
    /// began mid-conversation — the first payload seen becomes the anchor, and the
    /// stream is what followed from there. It is never pretended to start earlier
    /// than what was seen.
    /// </remarks>
    public void Anchor(uint firstSequence)
    {
        _nextSequence = firstSequence;
        _anchored = true;
    }

    /// <summary>
    /// Adds one segment and returns whatever became deliverable because of it.
    /// </summary>
    /// <remarks>
    /// The return is only the bytes that are now contiguous from the last
    /// position — often empty (an out-of-order segment fills a hole later), often
    /// several segments' worth at once (the missing piece finally arrived and
    /// unblocks everything queued behind it).
    /// </remarks>
    public byte[] Accept(TcpSegment segment)
    {
        if (!_anchored)
            Anchor(segment.PayloadStartSequence);

        var payload = segment.Payload;
        if (payload.Length == 0)
            return Drain();

        uint start = segment.PayloadStartSequence;
        uint end = start + (uint)payload.Length;

        // Wholly old data — a pure retransmission of bytes already delivered.
        if (SerialLessOrEqual(end, _nextSequence))
            return Array.Empty<byte>();

        // Partly old: trim the already-delivered prefix, keep the new tail. This
        // is the overlapping-retransmission case, where a resend starts before the
        // current position but carries bytes past it.
        ReadOnlySpan<byte> fresh = payload.Span;
        if (SerialLess(start, _nextSequence))
        {
            uint drop = _nextSequence - start;
            fresh = fresh[(int)drop..];
            start = _nextSequence;
        }

        StorePending(start, fresh);
        return Drain();
    }

    /// <summary>
    /// Stores a run of bytes, trimming any overlap with what is already pending.
    /// </summary>
    /// <remarks>
    /// Retransmissions can overlap held data as well as delivered data. Trimming
    /// on insert keeps the pending set non-overlapping, so draining never has to
    /// reconcile two versions of the same byte — and a retransmission that
    /// disagreed with held data (which TCP forbids but a capture might still show)
    /// does not silently overwrite what was seen first.
    /// </remarks>
    private void StorePending(uint start, ReadOnlySpan<byte> data)
    {
        uint end = start + (uint)data.Length;

        foreach (var (existingStart, existingBytes) in _pending)
        {
            uint existingEnd = existingStart + (uint)existingBytes.Length;
            // No overlap with this block.
            if (SerialLessOrEqual(end, existingStart) || SerialLessOrEqual(existingEnd, start))
                continue;

            // Overlap: keep only the part of the newcomer before the existing block.
            // Anything the existing block already covers is dropped, first-seen wins.
            if (SerialLess(start, existingStart))
            {
                uint keep = existingStart - start;
                StorePending(start, data[..(int)keep]);
            }
            uint tailStart = existingEnd;
            if (SerialLess(tailStart, end))
            {
                uint offset = tailStart - start;
                StorePending(tailStart, data[(int)offset..]);
            }
            return;
        }

        _pending[start] = data.ToArray();
    }

    /// <summary>
    /// Emits every byte now contiguous from the current position.
    /// </summary>
    private byte[] Drain()
    {
        if (_pending.Count == 0)
            return Array.Empty<byte>();

        var output = new List<byte>();
        while (_pending.Count > 0)
        {
            var (start, bytes) = First(_pending);

            // A block that starts past the current position leaves a gap: stop.
            // Everything from here on stays pending until the gap is filled.
            if (SerialLess(_nextSequence, start))
                break;

            _pending.Remove(start);
            output.AddRange(bytes);
            _nextSequence = start + (uint)bytes.Length;
        }

        _deliveredBytes += output.Count;
        return output.Count == 0 ? Array.Empty<byte>() : output.ToArray();
    }

    private static KeyValuePair<uint, byte[]> First(SortedDictionary<uint, byte[]> map)
    {
        foreach (var entry in map)
            return entry;
        throw new InvalidOperationException("empty");
    }

    // -- serial (wrap-aware) sequence comparison, RFC 1982 --------------------

    private static bool SerialLess(uint a, uint b) => unchecked((int)(a - b)) < 0;
    private static bool SerialLessOrEqual(uint a, uint b) => unchecked((int)(a - b)) <= 0;

    private sealed class SerialComparer : IComparer<uint>
    {
        public static readonly SerialComparer Instance = new();
        public int Compare(uint a, uint b)
        {
            if (a == b) return 0;
            return SerialLess(a, b) ? -1 : 1;
        }
    }
}

/// <summary>
/// Both directions of one conversation, reassembled together.
/// </summary>
/// <remarks>
/// A conversation is two independent streams — client to server and back — each
/// with its own sequence space. They are reassembled separately and never mixed;
/// keeping them under one object is only so a caller feeds segments in and reads
/// two ordered streams out without tracking the pair by hand.
/// </remarks>
public sealed class TcpConversation
{
    private readonly TcpStreamReassembler _outbound = new();
    private readonly TcpStreamReassembler _inbound = new();

    public TcpStreamReassembler Outbound => _outbound;
    public TcpStreamReassembler Inbound => _inbound;

    /// <summary>Feeds one segment to the stream its direction belongs to.</summary>
    public byte[] Accept(TcpSegment segment) => segment.Direction switch
    {
        StreamDirection.Outbound => _outbound.Accept(segment),
        StreamDirection.Inbound => _inbound.Accept(segment),
        _ => Array.Empty<byte>()
    };
}
