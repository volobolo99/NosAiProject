using System.Text;
using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception.Network;

namespace NosAi.LiveIntegration.Capture;

/// <summary>
/// What a recording yields when it is read as the world channel.
/// </summary>
/// <remarks>
/// Counts and ranges, not a verdict. It reports what the decoder got out of the
/// bytes so the operator can compare it against what the client's own HUD was
/// showing — which is the only independent check this path has.
/// </remarks>
public sealed record WorldChannelReplaySummary(
    long InboundMessages,
    long UndecodedMessages,
    long UnreadableFrames,
    IReadOnlyList<KeyValuePair<string, long>> Opcodes,
    IReadOnlyList<string> ReadOpcodes,
    long VitalsReadings,
    int MinHp,
    int MaxHp,
    IReadOnlyList<int> MaxHpValues,
    int MinMp,
    int MaxMp,
    long Sightings,
    long DistinctEntities,
    long CombatHits,
    long Deaths,
    DataSourceKind Source)
{
    /// <summary>Packets carrying an opcode the decoder reads.</summary>
    public long ReadablePackets => Opcodes.Where(o => ReadOpcodes.Contains(o.Key)).Sum(o => o.Value);

    /// <summary>Every packet the framer produced, whatever its opcode.</summary>
    public long TotalPackets => Opcodes.Sum(o => o.Value);

    /// <summary>A short human summary. States measurements; claims no meaning.</summary>
    public string Describe()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Canale world letto da una registrazione (provenienza {Source.ToWire()}):");
        sb.AppendLine($"  messaggi inbound       : {InboundMessages} (senza osservazione: {UndecodedMessages})");
        sb.AppendLine($"  frame non leggibili    : {UnreadableFrames} (direzione client->server, cifrata)");
        sb.AppendLine("  opcode sul filo:");
        foreach (KeyValuePair<string, long> opcode in Opcodes)
        {
            string mark = ReadOpcodes.Contains(opcode.Key) ? "letto" : "     ";
            double share = TotalPackets == 0 ? 0 : opcode.Value * 100.0 / TotalPackets;
            sb.AppendLine($"    {mark} {opcode.Key,-8} {opcode.Value,6}  {share,5:F1}%");
        }
        sb.AppendLine($"    => {ReadablePackets}/{TotalPackets} pacchetti portano un opcode che il decoder legge");

        sb.AppendLine($"  vitals (stat)          : {VitalsReadings} letture");
        if (VitalsReadings > 0)
        {
            sb.AppendLine($"    HP                   : {MinHp}..{MaxHp}");
            sb.AppendLine($"    HP massimo osservato : [{string.Join(", ", MaxHpValues)}]");
            sb.AppendLine($"    MP                   : {MinMp}..{MaxMp}");
            sb.AppendLine("    Confrontare con la HUD del client al momento della cattura: e' l'unico");
            sb.AppendLine("    riscontro indipendente che questo percorso abbia.");
        }
        else
        {
            sb.AppendLine("    Nessuna: nella finestra registrata non e' passato un 'stat'.");
        }

        sb.AppendLine($"  avvistamenti           : {Sightings} su {DistinctEntities} entita' distinte");
        sb.AppendLine($"  eventi                 : {CombatHits} colpi, {Deaths} morti");
        return sb.ToString();
    }
}

/// <summary>
/// Replays a recording through the world-channel framer and decoder, offline.
/// </summary>
/// <remarks>
/// <para>
/// The repeatable form of the check the decoder was written against: no driver,
/// no elevation, no client running — the bytes as they were captured, read by the
/// code that ships.
/// </para>
/// <para>
/// A recording is CACHED by construction, and this class agrees with
/// <see cref="ReplayNetworkSource"/> about that: the bytes were real when they
/// were captured and they are not current now. Replaying them can confirm the
/// decoder reads them correctly, and can never produce a LIVE observation — that
/// needs the driver on a running session.
/// </para>
/// <para>
/// The source is opened twice on purpose. Counting what is on the wire and
/// counting what became an observation are different questions, and the gap
/// between them is the useful part: an opcode nobody has established shows up
/// here as traffic that arrives and is not read, rather than as silence.
/// </para>
/// </remarks>
public static class WorldChannelReplay
{
    /// <summary>The opcodes <see cref="NosTaleWorldProtocolDecoder"/> reads.</summary>
    private static readonly string[] ReadOpcodes = { "stat", "st", "in", "mv", "die", "su" };

    /// <summary>Reads a recording file and reports what the world channel said.</summary>
    public static WorldChannelReplaySummary ReplayFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Replay(() => CaptureFile.Open(path));
    }

    /// <param name="openSource">
    /// Opens the packets. Called twice — once to census the wire, once to decode
    /// it — so it must yield a fresh source each time.
    /// </param>
    public static WorldChannelReplaySummary Replay(Func<IPacketSource> openSource)
    {
        ArgumentNullException.ThrowIfNull(openSource);
        IReadOnlyList<KeyValuePair<string, long>> opcodes = Census(openSource);
        return Decode(openSource, opcodes);
    }

    /// <summary>What opcodes arrived, whether or not anything reads them.</summary>
    private static IReadOnlyList<KeyValuePair<string, long>> Census(Func<IPacketSource> openSource)
    {
        var opcodes = new Dictionary<string, long>();
        using IPacketSource packets = openSource();
        var engine = new GameTrafficCaptureEngine(
            packets, NosTaleWorldFramer.Factory(DataSourceKind.Cached));

        engine.FrameProduced += frame =>
        {
            if (frame.Frame.Source == DataSourceKind.Unknown)
                return;
            foreach (string line in NosTaleWorldDecoder.Decode(frame.Frame.Body.Span))
            {
                string opcode = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault() ?? "(vuoto)";
                opcodes[opcode] = opcodes.GetValueOrDefault(opcode) + 1;
            }
        };

        engine.Run();
        return opcodes.OrderByDescending(o => o.Value).ThenBy(o => o.Key, StringComparer.Ordinal).ToList();
    }

    /// <summary>What the whole chain made of those packets.</summary>
    private static WorldChannelReplaySummary Decode(
        Func<IPacketSource> openSource, IReadOnlyList<KeyValuePair<string, long>> opcodes)
    {
        const DataSourceKind source = DataSourceKind.Cached;
        IPacketSource packets = openSource();
        var endpoint = new GameEndpoint(packets.ServerAddress.ToString(), packets.ServerPort);

        // The source takes ownership of the packets and disposes them.
        using var observationSource = ReassembledObservationSource.ForNosTaleWorld(packets, source);
        var observer = new GameTrafficObserver(
            observationSource, new ScopedGameTrafficFilter(endpoint), new NosTaleWorldProtocolDecoder());

        long inbound = 0, undecoded = 0, sightings = 0, hits = 0, deaths = 0, vitalsReadings = 0;
        int minHp = int.MaxValue, maxHp = 0, minMp = int.MaxValue, maxMp = 0;
        var maxHpValues = new SortedSet<int>();
        var entities = new HashSet<long>();

        while (true)
        {
            // One message per report, deliberately. A batch keeps only its most
            // recent vitals — correct for a runtime, which wants the current HP —
            // but it would collapse 62 readings into as many as there were polls,
            // and this is the report that has to show every one of them against
            // what the HUD was doing.
            NetworkObservationReport report = observer.ObservePending(1);
            if (report.ObservedPackets == 0)
                break;

            inbound += report.ObservedPackets;
            undecoded += report.UndecodablePackets;
            sightings += report.Sightings.Length;

            foreach (EntitySighting sighting in report.Sightings)
                entities.Add(sighting.EntityId);
            foreach (GameEvent gameEvent in report.Events)
            {
                if (gameEvent.Kind == GameEventKind.CombatHit) hits++;
                if (gameEvent.Kind == GameEventKind.EntityDeath) deaths++;
            }

            if (report.Vitals is { } vitals)
            {
                vitalsReadings++;
                minHp = Math.Min(minHp, vitals.Hp);
                maxHp = Math.Max(maxHp, vitals.Hp);
                minMp = Math.Min(minMp, vitals.Mp);
                maxMp = Math.Max(maxMp, vitals.Mp);
                maxHpValues.Add(vitals.MaxHp);
            }
        }

        return new WorldChannelReplaySummary(
            inbound, undecoded, observationSource.UnreadableFrames,
            opcodes, ReadOpcodes,
            vitalsReadings,
            vitalsReadings == 0 ? 0 : minHp, maxHp, maxHpValues.ToList(),
            vitalsReadings == 0 ? 0 : minMp, maxMp,
            sightings, entities.Count, hits, deaths,
            source);
    }
}
