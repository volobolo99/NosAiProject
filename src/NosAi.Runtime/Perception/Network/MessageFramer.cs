// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Versione: 1.0 Beta
// Percezione — Framing dello stream TCP in messaggi applicativi
// ============================================================================
//
// TCP è uno stream, non un canale a messaggi: un pacchetto osservato può
// contenere mezzo messaggio, tre messaggi, o la coda di uno e la testa del
// successivo. Decodificare direttamente il payload di un pacchetto significa
// leggere campi a offset sbagliati e produrre osservazioni false — che è il modo
// peggiore di fallire, perché sembrano dati veri.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace NosAi.Runtime.Perception.Network;

/// <summary>How application messages are delimited inside the TCP stream.</summary>
public sealed record FramingSpec(
    int LengthOffset,
    int LengthSize,
    bool BigEndian,
    int HeaderSize,
    bool LengthIncludesHeader,
    int MaxMessageLength = 64 * 1024)
{
    public void Validate()
    {
        if (LengthOffset < 0) throw new ArgumentOutOfRangeException(nameof(LengthOffset));
        if (LengthSize is not (1 or 2 or 4)) throw new ArgumentOutOfRangeException(nameof(LengthSize), "Length field must be 1, 2 or 4 bytes.");
        if (HeaderSize < LengthOffset + LengthSize)
            throw new ArgumentOutOfRangeException(nameof(HeaderSize), "The header must contain the length field.");
        if (MaxMessageLength is < 1 or > (16 * 1024 * 1024))
            throw new ArgumentOutOfRangeException(nameof(MaxMessageLength));
    }
}

/// <summary>
/// Frames a single TCP direction's byte stream into whole application messages.
/// </summary>
/// <remarks>
/// <para>
/// This used to be called <c>TcpStreamReassembler</c>, which is also the name of
/// <c>NosAi.LiveIntegration.Capture.TcpStreamReassembler</c>. The two were never
/// the same thing and neither was redundant, but sharing a name across namespaces
/// invited exactly the drift the audit of 2026-08-30 warned about: two classes
/// that look interchangeable in a call site and are not.
/// </para>
/// <para>
/// They are consecutive layers. The capture one turns TCP <i>segments</i> into an
/// ordered contiguous byte stream — sequence numbers, out-of-order arrival,
/// overlapping retransmissions, wrap. This one takes that stream and cuts it into
/// messages. Run this one on raw arrival-order payloads and it frames nonsense;
/// run that one and stop there and there are no messages at all.
/// </para>
/// <para>
/// One instance per direction: inbound and outbound are independent streams and
/// mixing them would interleave two byte sequences into nonsense.
/// </para>
/// <para>
/// Fail closed on nonsense: a declared length beyond
/// <see cref="FramingSpec.MaxMessageLength"/> means the stream is desynchronised
/// (wrong framing spec, or capture started mid-message). The reassembler stops
/// and reports it instead of allocating wildly or emitting garbage messages —
/// a resynchronisation guess would fabricate observations.
/// </para>
/// </remarks>
public sealed class MessageFramer
{
    private readonly FramingSpec _framing;
    private readonly List<byte> _buffer = new();
    private bool _desynchronised;

    /// <summary>Bytes waiting for the rest of their message.</summary>
    public int PendingBytes => _buffer.Count;

    /// <summary>True once the stream stopped making sense; no further messages are emitted.</summary>
    public bool IsDesynchronised => _desynchronised;

    public string? DesyncReason { get; private set; }

    public MessageFramer(FramingSpec framing)
    {
        ArgumentNullException.ThrowIfNull(framing);
        framing.Validate();
        _framing = framing;
    }

    /// <summary>
    /// Adds observed bytes and returns every complete message they finished.
    /// A partial message stays buffered until the rest arrives.
    /// </summary>
    public IReadOnlyList<byte[]> Push(ReadOnlySpan<byte> data)
    {
        var messages = new List<byte[]>();
        if (_desynchronised) return messages;

        foreach (byte b in data) _buffer.Add(b);

        while (true)
        {
            if (_buffer.Count < _framing.HeaderSize) break;

            long declared = ReadLength();
            long total = _framing.LengthIncludesHeader ? declared : declared + _framing.HeaderSize;

            if (total < _framing.HeaderSize || total > _framing.MaxMessageLength)
            {
                _desynchronised = true;
                DesyncReason = $"implausible_message_length:{declared}";
                break;
            }
            if (_buffer.Count < total) break;   // incomplete: wait for more bytes

            var message = new byte[total];
            _buffer.CopyTo(0, message, 0, (int)total);
            _buffer.RemoveRange(0, (int)total);
            messages.Add(message);
        }
        return messages;
    }

    private long ReadLength()
    {
        Span<byte> field = stackalloc byte[_framing.LengthSize];
        for (int i = 0; i < _framing.LengthSize; i++) field[i] = _buffer[_framing.LengthOffset + i];
        return _framing.LengthSize switch
        {
            1 => field[0],
            2 => _framing.BigEndian ? BinaryPrimitives.ReadUInt16BigEndian(field) : BinaryPrimitives.ReadUInt16LittleEndian(field),
            _ => _framing.BigEndian ? BinaryPrimitives.ReadUInt32BigEndian(field) : BinaryPrimitives.ReadUInt32LittleEndian(field),
        };
    }

    /// <summary>Drops buffered bytes and clears the desync flag (use on reconnect).</summary>
    public void Reset()
    {
        _buffer.Clear();
        _desynchronised = false;
        DesyncReason = null;
    }
}
