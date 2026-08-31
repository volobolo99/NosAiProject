// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Versione: 1.0 Beta
// Percezione — Feed di rete verso qualsiasi consumatore, e registratore di
//              traffico per la calibrazione della mappa di protocollo
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NosAi.Runtime.AI.Decision;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Perception.Network;

/// <summary>
/// Distributes network-derived observations to every consumer that needs them.
/// </summary>
/// <remarks>
/// <para>
/// This is the "any function can take its data from the network" surface: the
/// world model, the decision engine, tactics, navigation and economy all read
/// the same observations, so there is one truth rather than several.
/// </para>
/// <para>
/// Precision is why network data is worth having: a value read from the
/// protocol is exact, where the same value read off the screen is an estimate.
/// The feed keeps that distinction rather than erasing it — network facts carry
/// the provenance the decoder gave them, and a consumer that needs LIVE data can
/// still refuse a DERIVED one.
/// </para>
/// </remarks>
public sealed class NetworkWorldFeed
{
    private readonly GameTrafficObserver _observer;
    private readonly List<Action<NetworkObservationReport>> _subscribers = new();
    private NetworkObservationReport? _latest;

    /// <summary>The most recent report, or null before the first poll.</summary>
    public NetworkObservationReport? Latest => _latest;

    public NetworkWorldFeed(GameTrafficObserver observer)
        => _observer = observer ?? throw new ArgumentNullException(nameof(observer));

    /// <summary>Registers a consumer of network observations.</summary>
    public void Subscribe(Action<NetworkObservationReport> subscriber)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        _subscribers.Add(subscriber);
    }

    /// <summary>Polls the channel once and fans the result out to every consumer.</summary>
    public NetworkObservationReport Poll(int maxPackets = 4096)
    {
        NetworkObservationReport report = _observer.ObservePending(maxPackets);
        _latest = report;
        foreach (Action<NetworkObservationReport> subscriber in _subscribers)
        {
            // One faulty consumer must not stop the others from being fed.
            try { subscriber(report); } catch { /* observer isolation */ }
        }
        return report;
    }

    /// <summary>The world state implied by the latest report.</summary>
    public NosAi.Runtime.WorldModel.WorldState ToWorldState()
        => _observer.ToWorldState(_latest ?? _observer.ObservePending(0));

    /// <summary>
    /// Projects the latest report into decision-engine facts.
    /// </summary>
    /// <remarks>
    /// Facts absent from the report are recorded as UNKNOWN with a reason, never
    /// omitted silently and never defaulted: a rule that needs an unobserved fact
    /// must be skipped, and it can only be skipped if the fact is present as
    /// unknown rather than missing by accident.
    /// </remarks>
    public DecisionContext ToDecisionContext(long? currentTargetId = null)
    {
        var context = new DecisionContext();
        NetworkObservationReport? report = _latest;

        if (report is null || report.Source == DataSourceKind.Unknown)
        {
            context.WithUnknown("player.hp_ratio", "no_network_observation");
            context.WithUnknown("target.hp_ratio", "no_network_observation");
            context.WithUnknown("monsters.count", "no_network_observation");
            return context;
        }

        EntitySighting? player = report.Sightings.FirstOrDefault(s => s.EntityId == 0);
        if (player is not null) context.With("player.hp_ratio", Classify(player.HpRatio, player.Source));
        else context.WithUnknown("player.hp_ratio", "player_not_sighted");

        var monsters = report.Sightings.Where(s => s.EntityId != 0).ToArray();
        context.With("monsters.count", Classify(monsters.Length, report.Source));

        if (currentTargetId is { } targetId)
        {
            EntitySighting? target = monsters.FirstOrDefault(s => s.EntityId == targetId);
            if (target is not null) context.With("target.hp_ratio", Classify(target.HpRatio, target.Source));
            else context.WithUnknown("target.hp_ratio", "target_not_sighted");
        }
        else
        {
            context.WithUnknown("target.hp_ratio", "no_target_selected");
        }

        return context;
    }

    private static ClassifiedValue<double> Classify(double value, DataSourceKind source) => source switch
    {
        DataSourceKind.Live => ClassifiedValue<double>.Live(value),
        DataSourceKind.Derived => ClassifiedValue<double>.Derived(value),
        DataSourceKind.Cached => ClassifiedValue<double>.Cached(value, DateTime.UtcNow),
        DataSourceKind.Simulated => ClassifiedValue<double>.Simulated(value),
        _ => ClassifiedValue<double>.Unknown("source_unknown"),
    };
}

/// <summary>
/// Records scoped game traffic so the operator can derive the protocol map.
/// </summary>
/// <remarks>
/// <para>
/// This is the honest path to a real decoder: rather than guessing opcodes, the
/// operator captures traffic alongside known ground truth ("my HP was 4200/5000
/// at this moment") and correlates the two until the field offsets are known.
/// </para>
/// <para>
/// It writes only what the scope admitted — game traffic — and stays on the
/// dedicated volume. It records; it never transmits anything anywhere.
/// </para>
/// </remarks>
public sealed class TrafficRecorder
{
    private readonly List<ObservedPacket> _recorded = new();
    private readonly int _capacity;

    public int Count => _recorded.Count;
    public IReadOnlyList<ObservedPacket> Recorded => _recorded;

    public TrafficRecorder(int capacity = 100_000)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    /// <summary>Records one packet, dropping the oldest when full.</summary>
    public void Record(ObservedPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        _recorded.Add(packet);
        if (_recorded.Count > _capacity) _recorded.RemoveAt(0);
    }

    /// <summary>
    /// Writes the capture as hex lines: timestamp, direction, length, payload.
    /// A text format on purpose — correlating bytes with a remembered HP value is
    /// done by eye and by grep, not by a parser.
    /// </summary>
    public void WriteTo(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var builder = new StringBuilder();
        builder.AppendLine("# NosAi scoped game-traffic capture");
        builder.AppendLine("# utc\tdirection\tsource\tlength\tpayload_hex");
        foreach (ObservedPacket packet in _recorded)
        {
            builder.Append(packet.CapturedUtc.ToString("O", CultureInfo.InvariantCulture)).Append('\t')
                   .Append(packet.Direction).Append('\t')
                   .Append(packet.Source.ToWire()).Append('\t')
                   .Append(packet.Payload.Length).Append('\t')
                   .AppendLine(Convert.ToHexString(packet.Payload.Span));
        }
        File.WriteAllText(path, builder.ToString());
    }

    /// <summary>
    /// Finds byte offsets whose value, read at the given width, equals a known
    /// ground-truth number.
    /// </summary>
    /// <remarks>
    /// The calibration primitive: told "HP was 4200 here", it reports every
    /// offset where 4200 actually appears. Several candidates is the normal first
    /// answer — repeating it across captures with different values is what
    /// narrows them to one, and that is the operator's judgement, not a guess the
    /// code makes on its own.
    /// </remarks>
    public static ImmutableArray<int> FindOffsetsMatching(ReadOnlySpan<byte> message, long knownValue, int size, bool bigEndian = true)
    {
        if (size is not (1 or 2 or 4 or 8)) throw new ArgumentOutOfRangeException(nameof(size));
        var offsets = ImmutableArray.CreateBuilder<int>();
        var field = new FieldSpec(0, size, bigEndian);
        for (int offset = 0; offset + size <= message.Length; offset++)
        {
            var probe = field with { Offset = offset };
            if (probe.TryRead(message, out double value) && Math.Abs(value - knownValue) < 0.5)
                offsets.Add(offset);
        }
        return offsets.ToImmutable();
    }
}
