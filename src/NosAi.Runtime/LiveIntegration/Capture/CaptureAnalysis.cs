using System.Text;

namespace NosAi.LiveIntegration.Capture;

/// <summary>
/// What a recorded capture looks like, per direction, without interpreting it.
/// </summary>
/// <remarks>
/// The honest first look at a protocol nobody here has decoded. It measures the
/// stream — how much, how often, how the bytes are distributed — and never claims
/// a meaning. Its whole job is to make the structure visible so a decoder can be
/// written from evidence rather than guessed, which is the line ADR-0014 draws.
/// </remarks>
public sealed record DirectionAnalysis(
    StreamDirection Direction,
    long PacketCount,
    long TotalBytes,
    int MinPayload,
    int MaxPayload,
    double MeanPayload,
    TimeSpan Duration,
    IReadOnlyList<KeyValuePair<byte, long>> TopFirstBytes,
    IReadOnlyList<KeyValuePair<int, long>> PayloadLengthHistogram)
{
    /// <summary>
    /// The most frequent first byte, if one dominates.
    /// </summary>
    /// <remarks>
    /// A candidate for an opcode or a length prefix — a hint for the decoder, not a
    /// finding. Null when the stream carried nothing.
    /// </remarks>
    public byte? DominantFirstByte => TopFirstBytes.Count > 0 ? TopFirstBytes[0].Key : null;
}

/// <summary>A whole capture measured, both directions.</summary>
public sealed record CaptureAnalysis(
    DirectionAnalysis Outbound,
    DirectionAnalysis Inbound,
    long PacketsRejected)
{
    /// <summary>A short, honest human summary. States measurements, claims no meaning.</summary>
    public string Describe()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Analisi cattura (solo misure, nessuna interpretazione):");
        Append(sb, "PC   -> server", Outbound);
        Append(sb, "server -> PC ", Inbound);
        if (PacketsRejected > 0)
            sb.AppendLine($"  pacchetti scartati (altra conversazione o non parsabili): {PacketsRejected}");
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, string label, DirectionAnalysis d)
    {
        sb.AppendLine($"  {label}: {d.PacketCount} pacchetti, {d.TotalBytes} byte, " +
                      $"payload {d.MinPayload}..{d.MaxPayload} (media {d.MeanPayload:F1}), durata {d.Duration.TotalSeconds:F1}s");
        if (d.DominantFirstByte is { } b)
            sb.AppendLine($"    primo byte piu' frequente: 0x{b:X2} (candidato opcode/lunghezza, non una conclusione)");
    }
}

/// <summary>
/// Measures a recorded capture, one direction's application stream at a time.
/// </summary>
/// <remarks>
/// It runs the same reassembly the live path does, then measures the delivered
/// bytes rather than framing them. Two things make the numbers trustworthy: it
/// works on the reassembled stream, not raw packets, so retransmissions and
/// reordering do not skew the distribution; and it counts what the parser
/// rejected, so a capture that is mostly noise cannot look like a clean protocol.
/// </remarks>
public static class CaptureAnalyzer
{
    /// <summary>Reads a recording file and measures it.</summary>
    public static CaptureAnalysis AnalyzeFile(string path)
    {
        using var source = CaptureFile.Open(path);
        return Analyze(source);
    }

    /// <summary>Measures any packet source to exhaustion.</summary>
    public static CaptureAnalysis Analyze(IPacketSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var outbound = new DirectionAccumulator(StreamDirection.Outbound);
        var inbound = new DirectionAccumulator(StreamDirection.Inbound);
        var conversation = new TcpConversation();
        long rejected = 0;

        while (source.TryRead(TimeSpan.Zero, out var packet))
        {
            if (!Ipv4TcpParser.TryParseSegment(packet.Raw.Span, source.ServerAddress, source.ServerPort, out var segment, out _))
            {
                rejected++;
                continue;
            }

            byte[] delivered = conversation.Accept(segment);
            if (delivered.Length == 0)
                continue;

            var target = segment.Direction == StreamDirection.Outbound ? outbound : inbound;
            target.Add(packet.TimestampUtc, delivered);
        }

        return new CaptureAnalysis(outbound.Build(), inbound.Build(), rejected);
    }

    /// <summary>Gathers one direction's measurements as bytes are delivered.</summary>
    private sealed class DirectionAccumulator
    {
        private readonly StreamDirection _direction;
        private readonly Dictionary<byte, long> _firstBytes = new();
        private readonly Dictionary<int, long> _lengths = new();
        private long _packets, _bytes;
        private int _min = int.MaxValue, _max;
        private DateTime _first = DateTime.MinValue, _last;

        public DirectionAccumulator(StreamDirection direction) => _direction = direction;

        public void Add(DateTime timestamp, byte[] delivered)
        {
            _packets++;
            _bytes += delivered.Length;
            _min = Math.Min(_min, delivered.Length);
            _max = Math.Max(_max, delivered.Length);

            if (_first == DateTime.MinValue)
                _first = timestamp;
            _last = timestamp;

            if (delivered.Length > 0)
                _firstBytes[delivered[0]] = _firstBytes.GetValueOrDefault(delivered[0]) + 1;

            // Bucketed so the histogram stays legible on a real capture: exact for
            // the small lengths where a protocol's fixed headers live, coarse above.
            int bucket = delivered.Length <= 64 ? delivered.Length : (delivered.Length / 64) * 64;
            _lengths[bucket] = _lengths.GetValueOrDefault(bucket) + 1;
        }

        public DirectionAnalysis Build()
        {
            var topFirstBytes = _firstBytes
                .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key)
                .Take(8).ToList();
            var histogram = _lengths.OrderBy(kv => kv.Key).ToList();

            return new DirectionAnalysis(
                _direction,
                _packets,
                _bytes,
                _packets == 0 ? 0 : _min,
                _max,
                _packets == 0 ? 0 : (double)_bytes / _packets,
                _first == DateTime.MinValue ? TimeSpan.Zero : _last - _first,
                topFirstBytes,
                histogram);
        }
    }
}
