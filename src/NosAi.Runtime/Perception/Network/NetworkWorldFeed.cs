// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Perception — Network feed for any consumer, plus a traffic recorder for
//              calibrating the protocol map
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
public sealed class NetworkWorldFeed : IPlayerAttackObserver
{
    private readonly GameTrafficObserver _observer;
    private readonly List<Action<NetworkObservationReport>> _subscribers = new();
    private NetworkObservationReport? _latest;
    private DateTime? _lastPlayerAttackAtUtc;
    private long? _playerEntityId;

    /// <summary>The most recent report, or null before the first poll.</summary>
    public NetworkObservationReport? Latest => _latest;

    /// <summary>
    /// The controlled character's own entity id, once any batch has carried the
    /// <c>cond</c> that names it. Null until then, never guessed.
    /// </summary>
    /// <remarks>
    /// Kept across polls because <c>cond</c> arrives once and the id does not
    /// change within a session. This is the value a memory reader checks its
    /// own character id against (<c>MemoryGameplayProvider</c>'s identity
    /// check): two independent sources agreeing on one number is what turns a
    /// pointer chain's coordinate into a position.
    /// </remarks>
    public long? PlayerEntityId => _playerEntityId;

    /// <inheritdoc />
    /// <remarks>
    /// Kept across polls rather than read off the latest report. A hit is an
    /// instant, and the batch that carried it is usually not the batch the
    /// composer is asking about; forgetting it at the next poll would make the
    /// contradiction check see nothing a fraction of a second after the hit.
    /// </remarks>
    public DateTime? LastPlayerAttackAtUtc => _lastPlayerAttackAtUtc;

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
        // Never moves backwards: a replay or an out-of-order packet must not make
        // the most recent observed hit older than one already seen.
        if (report.PlayerAttackedAtUtc is { } attackedAt
            && (_lastPlayerAttackAtUtc is null || attackedAt > _lastPlayerAttackAtUtc))
        {
            _lastPlayerAttackAtUtc = attackedAt;
        }
        _playerEntityId ??= report.PlayerEntityId;
        foreach (Action<NetworkObservationReport> subscriber in _subscribers)
        {
            // One faulty consumer must not stop the others from being fed.
            try { subscriber(report); } catch { /* observer isolation */ }
        }
        return report;
    }

    /// <summary>
    /// The world state implied by the latest report, when there is one.
    /// </summary>
    /// <remarks>
    /// False when the player's health is not known. The caller must keep the
    /// previous state or wait, not substitute a default: <c>WorldState</c> carries
    /// no provenance, so a placeholder inserted here is indistinguishable from an
    /// observation for the rest of its life.
    /// </remarks>
    public bool TryToWorldState(
        out NosAi.Runtime.WorldModel.WorldState worldState,
        out string? failureReason)
        => _observer.TryToWorldState(_latest ?? _observer.ObservePending(1), out worldState, out failureReason);

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

        // The player's own health comes from the vitals message, not from a
        // sighting. The server never sights the player: every movement packet in
        // 117 KB of real capture is another entity, because position is
        // client-authoritative (docs/PROTOCOLLO_NOSTALE.md). Reading the ratio off
        // a sighting with entity id 0 therefore found nothing on the real wire,
        // while the exact HP and max HP sat unused in the same report.
        if (report.Vitals is { } vitals && vitals.MaxHp > 0)
        {
            context.With("player.hp_ratio", Classify((double)vitals.Hp / vitals.MaxHp, vitals.Source));
        }
        else if (report.Sightings.FirstOrDefault(s => s.EntityId == 0) is { HpRatio: { } playerHp } player)
        {
            // A channel whose decoder does sight the player keeps working.
            context.With("player.hp_ratio", Classify(playerHp, player.Source));
        }
        else
        {
            // A sighting of the player without health is a distinct case from not
            // sighting the player at all, and the reason says which it was: the
            // one is fixed by mapping the health field, the other by finding the
            // player on the wire.
            bool sightedWithoutHp = report.Sightings.Any(s => s.EntityId == 0);
            context.WithUnknown("player.hp_ratio", sightedWithoutHp
                ? "player_hp_not_observed"
                : report.VitalsReadable
                    ? "player_vitals_not_in_batch"
                    : "player_not_sighted");
        }

        var monsters = report.Sightings.Where(s => s.EntityId != 0).ToArray();
        context.With("monsters.count", Classify(monsters.Length, report.Source));

        if (currentTargetId is { } targetId)
        {
            EntitySighting? target = monsters.FirstOrDefault(s => s.EntityId == targetId);
            // Seen but without health is not the same fact as not seen, and it is
            // certainly not health of zero: an mv packet locates the target and
            // says nothing about its condition.
            if (target is { HpRatio: { } targetHp }) context.With("target.hp_ratio", Classify(targetHp, target.Source));
            else if (target is not null) context.WithUnknown("target.hp_ratio", "target_hp_not_observed");
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
