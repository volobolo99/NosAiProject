// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Percezione — Check di certificazione: reassembler, mappa protocollo, feed
// ============================================================================

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using NosAi.Runtime.AI.Decision;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Perception.Network;

public static partial class NetworkObservationTestRunner
{
    private static readonly FramingSpec TestFraming = new(
        LengthOffset: 0, LengthSize: 2, BigEndian: true, HeaderSize: 2, LengthIncludesHeader: false);

    /// <summary>Builds a length-prefixed message: [len:2][opcode:1][body...].</summary>
    private static byte[] Framed(byte opcode, params byte[] body)
    {
        var message = new byte[2 + 1 + body.Length];
        message[0] = (byte)((1 + body.Length) >> 8);
        message[1] = (byte)((1 + body.Length) & 0xFF);
        message[2] = opcode;
        body.CopyTo(message, 3);
        return message;
    }

    // ------------------------------------------------------------- reassembly

    private static bool TestReassemblerJoinsSplitMessages()
    {
        var reassembler = new MessageFramer(TestFraming);
        byte[] message = Framed(0x01, 1, 2, 3, 4);

        // A message split across two TCP segments must not decode as two.
        if (reassembler.Push(message.AsSpan(0, 3)).Count != 0) return false;
        if (reassembler.PendingBytes != 3) return false;

        var completed = reassembler.Push(message.AsSpan(3));
        return completed.Count == 1
            && completed[0].SequenceEqual(message)
            && reassembler.PendingBytes == 0;
    }

    private static bool TestReassemblerSplitsCoalescedMessages()
    {
        var reassembler = new MessageFramer(TestFraming);
        byte[] first = Framed(0x01, 9), second = Framed(0x02, 8, 7), third = Framed(0x03);

        // Three messages arriving in one segment, plus a trailing partial one.
        byte[] stream = first.Concat(second).Concat(third).Concat(new byte[] { 0x00 }).ToArray();
        var messages = reassembler.Push(stream);
        return messages.Count == 3
            && messages[0].SequenceEqual(first)
            && messages[1].SequenceEqual(second)
            && messages[2].SequenceEqual(third)
            && reassembler.PendingBytes == 1;
    }

    private static bool TestReassemblerFailsClosedOnNonsense()
    {
        var reassembler = new MessageFramer(TestFraming with { MaxMessageLength = 128 });
        // A length far beyond the cap means the framing is wrong or the capture
        // started mid-message. Guessing a resync point would fabricate messages.
        var messages = reassembler.Push(new byte[] { 0xFF, 0xFF, 0x01 });
        return messages.Count == 0
            && reassembler.IsDesynchronised
            && reassembler.DesyncReason!.StartsWith("implausible_message_length", StringComparison.Ordinal)
            && reassembler.Push(Framed(0x01, 1)).Count == 0    // stays closed
            && Resets(reassembler);
    }

    private static bool Resets(MessageFramer reassembler)
    {
        reassembler.Reset();
        return !reassembler.IsDesynchronised && reassembler.Push(Framed(0x01, 1)).Count == 1;
    }

    // ------------------------------------------------------------- protocol map

    private const string SightingMapJson = """
    {
      "name": "test-observed-v1",
      "confidence": "Derived",
      "framing": { "lengthOffset": 0, "lengthSize": 2, "bigEndian": true, "headerSize": 2 },
      "opcode": { "offset": 2, "size": 1 },
      "messages": [
        { "opcode": 1, "kind": "EntitySighting",
          "entityId": { "offset": 3, "size": 4 },
          "x": { "offset": 7, "size": 2 },
          "y": { "offset": 9, "size": 2 },
          "hpRatio": { "offset": 11, "size": 1, "scale": 0.01 } },
        { "opcode": 2, "kind": "EntityDeath", "entityId": { "offset": 3, "size": 4 } }
      ]
    }
    """;

    private static byte[] SightingMessage(long entityId, int x, int y, int hpPercent) => Framed(0x01,
        (byte)(entityId >> 24), (byte)(entityId >> 16), (byte)(entityId >> 8), (byte)entityId,
        (byte)(x >> 8), (byte)x, (byte)(y >> 8), (byte)y, (byte)hpPercent);

    private static bool TestProtocolMapDecodesFromConfiguration()
    {
        var decoder = new ConfigurableProtocolDecoder(ProtocolMapLoader.Parse(SightingMapJson));
        DecodedObservations decoded = decoder.DecodeMessage(SightingMessage(4242, 300, 450, 75), DataSourceKind.Live);

        EntitySighting sighting = decoded.Sightings.Single();
        // Fields come from the file, not from code: this is what makes a real
        // protocol expressible without inventing opcodes in the binary.
        return sighting.EntityId == 4242
            && Math.Abs(sighting.X - 300) < 1e-9
            && Math.Abs(sighting.Y - 450) < 1e-9
            && sighting.HpRatio is { } hp && Math.Abs(hp - 0.75) < 1e-9;
    }

    private static bool TestUnmappedOpcodeDecodesNothing()
    {
        var decoder = new ConfigurableProtocolDecoder(ProtocolMapLoader.Parse(SightingMapJson));
        DecodedObservations decoded = decoder.DecodeMessage(Framed(0x7F, 1, 2, 3), DataSourceKind.Live);
        return decoded.IsEmpty && decoder.UnmappedOpcodeCount == 1 && decoder.MalformedMessageCount == 0;
    }

    private static bool TestMalformedMessageDecodesNothing()
    {
        var decoder = new ConfigurableProtocolDecoder(ProtocolMapLoader.Parse(SightingMapJson));

        // Mapped opcode, but the message is too short to hold the mapped fields:
        // a field outside the message is unreadable, not zero.
        if (!decoder.DecodeMessage(Framed(0x01, 1, 2), DataSourceKind.Live).IsEmpty) return false;

        // HP scaled out of 0..1 means the offset or the scale is wrong. Clamping
        // would hide a broken map behind a plausible number.
        if (!decoder.DecodeMessage(SightingMessage(1, 10, 10, 240), DataSourceKind.Live).IsEmpty) return false;
        return decoder.MalformedMessageCount == 2;
    }

    private static bool TestMapConfidenceCapsProvenance()
    {
        // The map was derived by correlation, so even a LIVE packet read through
        // it yields a DERIVED observation: a reading is never more trustworthy
        // than the description used to interpret it.
        var derived = new ConfigurableProtocolDecoder(ProtocolMapLoader.Parse(SightingMapJson));
        var observation = derived.DecodeMessage(SightingMessage(1, 5, 5, 50), DataSourceKind.Live);
        if (observation.Sightings.Single().Source != DataSourceKind.Derived) return false;

        // And a map can never claim to be LIVE itself.
        try
        {
            ProtocolMapLoader.Parse(SightingMapJson.Replace("\"Derived\"", "\"Live\"", StringComparison.Ordinal));
            return false;
        }
        catch (InvalidDataException) { return true; }
    }

    private static bool TestMalformedMapRefused()
    {
        bool duplicateRefused = false, unknownKindRefused = false, sightingWithoutFieldsRefused = false;
        try
        {
            ProtocolMapLoader.Parse(SightingMapJson.Replace("\"opcode\": 2", "\"opcode\": 1", StringComparison.Ordinal));
        }
        catch (InvalidDataException ex) { duplicateRefused = ex.Message.Contains("twice"); }

        try { ProtocolMapLoader.Parse(SightingMapJson.Replace("EntityDeath", "Teleport", StringComparison.Ordinal)); }
        catch (InvalidDataException ex) { unknownKindRefused = ex.Message.Contains("Teleport"); }

        try
        {
            ProtocolMapLoader.Parse("""
            { "name": "bad", "framing": { "lengthOffset": 0, "lengthSize": 2, "headerSize": 2 },
              "opcode": { "offset": 2, "size": 1 },
              "messages": [ { "opcode": 1, "kind": "EntitySighting" } ] }
            """);
        }
        catch (InvalidDataException ex) { sightingWithoutFieldsRefused = ex.Message.Contains("entityId"); }

        return duplicateRefused && unknownKindRefused && sightingWithoutFieldsRefused;
    }

    private static bool TestMissingMapReported()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"nosai_map_{Guid.NewGuid():N}.json");
        // No map is the normal state until the operator derives one: the channel
        // must say so rather than appear to have one.
        return !ProtocolMapLoader.TryLoadFile(missing, out ProtocolMap? map, out string? failure)
            && map is null
            && failure!.StartsWith("protocol_map_not_found:", StringComparison.Ordinal);
    }

    // ------------------------------------------------------------- calibration

    private static bool TestCalibrationFindsKnownValue()
    {
        // The operator knows their HP was 4200; the recorder reports where 4200
        // actually appears, which is how a real map gets derived instead of guessed.
        byte[] message = SightingMessage(4200, 300, 450, 75);
        ImmutableArray<int> offsets = TrafficRecorder.FindOffsetsMatching(message, 4200, size: 4);
        if (!offsets.Contains(3)) return false;

        ImmutableArray<int> xOffsets = TrafficRecorder.FindOffsetsMatching(message, 300, size: 2);
        return xOffsets.Contains(7);
    }

    private static bool TestRecorderRoundTrip()
    {
        var recorder = new TrafficRecorder(capacity: 3);
        for (int i = 1; i <= 5; i++)
            recorder.Record(GamePacket(SightingMessage(i, i, i, 50)));

        // Bounded: the oldest are dropped rather than growing without limit.
        if (recorder.Count != 3) return false;

        string path = Path.Combine(Path.GetTempPath(), $"nosai_capture_{Guid.NewGuid():N}.txt");
        try
        {
            recorder.WriteTo(path);
            string[] lines = File.ReadAllLines(path);
            return lines.Length == 5                       // two header lines plus three packets
                && lines[0].StartsWith("#", StringComparison.Ordinal)
                && lines[^1].Contains("SIMULATED", StringComparison.Ordinal);
        }
        finally { try { File.Delete(path); } catch { /* best-effort temp cleanup */ } }
    }

    // ------------------------------------------------------------- feed

    private static GameTrafficObserver FeedObserver(params byte[][] messages)
    {
        var packets = messages.Select(m => GamePacket(m)).ToArray();
        return new GameTrafficObserver(new SyntheticNetworkSource(packets),
            new ScopedGameTrafficFilter(GameServer),
            new ConfigurableProtocolDecoder(ProtocolMapLoader.Parse(SightingMapJson)));
    }

    private static bool TestFeedFansOutToConsumers()
    {
        var feed = new NetworkWorldFeed(FeedObserver(
            SightingMessage(0, 0, 0, 60), SightingMessage(101, 30, 40, 90)));

        int deliveries = 0;
        NetworkObservationReport? seen = null;
        feed.Subscribe(_ => throw new InvalidOperationException("a faulty consumer"));
        feed.Subscribe(r => { deliveries++; seen = r; });

        NetworkObservationReport report = feed.Poll();
        // A throwing consumer must not stop the others being fed.
        return deliveries == 1 && seen == report && report.Sightings.Length == 2;
    }

    private static bool TestFeedProducesDecisionFacts()
    {
        var feed = new NetworkWorldFeed(FeedObserver(
            SightingMessage(0, 0, 0, 30), SightingMessage(101, 30, 40, 20)));
        feed.Poll();

        DecisionContext context = feed.ToDecisionContext(currentTargetId: 101);
        bool playerRead = context.TryRead("player.hp_ratio", out double hp, out DataSourceKind hpSource);
        bool targetRead = context.TryRead("target.hp_ratio", out double targetHp, out _);
        bool monstersRead = context.TryRead("monsters.count", out double monsters, out _);

        // The packets are synthetic and the map is derived: the weaker of the two
        // wins, so the fact is SIMULATED. A synthetic feed must never yield facts
        // that look observed.
        return playerRead && Math.Abs(hp - 0.30) < 1e-9 && hpSource == DataSourceKind.Simulated
            && targetRead && Math.Abs(targetHp - 0.20) < 1e-9
            && monstersRead && Math.Abs(monsters - 1) < 1e-9;
    }

    private static bool TestFeedFactsAreUnknownWithoutObservation()
    {
        var feed = new NetworkWorldFeed(new GameTrafficObserver(new UnavailableNetworkSource(),
            new ScopedGameTrafficFilter(GameServer),
            new ConfigurableProtocolDecoder(ProtocolMapLoader.Parse(SightingMapJson))));
        feed.Poll();

        DecisionContext context = feed.ToDecisionContext(currentTargetId: 101);
        // Facts must be present as UNKNOWN, not absent: a rule can only be
        // skipped for "not observed" if the fact exists and says so.
        return !context.TryRead("player.hp_ratio", out _, out DataSourceKind source)
            && source == DataSourceKind.Unknown
            && context.FactNames.Contains("player.hp_ratio")
            && !context.TryRead("target.hp_ratio", out _, out _);
    }

    private static bool TestFeedDrivesTheDecisionEngine()
    {
        // End to end: network bytes -> observations -> facts -> a real decision.
        var feed = new NetworkWorldFeed(FeedObserver(SightingMessage(0, 0, 0, 10)));
        feed.Poll();

        var engine = new UtilityDecisionEngine(BuiltInRuleSet.Create());
        DecisionOutcome outcome = engine.Decide(feed.ToDecisionContext());
        // The decision inherits the provenance of the bytes it was taken on:
        // synthetic packets can only ever produce a SIMULATED decision.
        return outcome.HasDecision
            && outcome.Action == "ACTION_EMERGENCY_FLEE"
            && outcome.Source == DataSourceKind.Simulated;
    }
}
