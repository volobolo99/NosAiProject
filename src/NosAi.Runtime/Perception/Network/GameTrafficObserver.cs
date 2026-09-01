// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Perception — Game traffic observer: scope → decode → observations
// ============================================================================
//
// Network observation converges into the World Model exactly as vision does: the
// game's packets become EntitySighting (projectable into Detection) and tactical
// events, usable by combat, prediction and strategy.
//
// The decoder is an interface. Tests use a synthetic protocol that does not
// claim to be NosTale. The observed world-channel decoder is
// NosTaleWorldProtocolDecoder, which reads the packets NosTaleWorldDecoder
// produces and nothing marked unknown in docs/PROTOCOLLO_NOSTALE.md.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Perception.Network;

/// <summary>Kind of tactical event decoded from game traffic.</summary>
public enum GameEventKind : byte
{
    EntitySighting = 0,
    CombatHit = 1,
    EntityDeath = 2,
    ChatMessage = 3,
    Unknown = 4,
}

/// <summary>An entity seen on the wire, carried into the world model.</summary>
/// <param name="HpRatio">
/// The entity's health as a fraction of its maximum, or null when the wire said
/// where it is without saying how it is. That is the ordinary case, not the
/// exception: <c>mv</c> carries a position and no health at all, and 7685 of the
/// 8211 packets in the capture are <c>mv</c>. A sentinel such as -1, or a
/// <c>bool HasHp</c> beside a real double, would leave every caller free to read
/// a number that was never observed; an absent field is what
/// <see cref="ClassifiedValue{T}"/> asserts everywhere else in this project.
/// </param>
public sealed record EntitySighting(long EntityId, string Kind, double X, double Y, double? HpRatio, DataSourceKind Source)
{
    /// <summary>
    /// Projects into the perception Detection consumed by the world model, or
    /// null when this sighting carries no health.
    /// </summary>
    /// <remarks>
    /// <see cref="Detection"/> is shared with screen perception, where health is
    /// always read alongside the box, so its HP is not optional. Filling it with
    /// zero here would tell the world model it saw a mob at zero HP, which is a
    /// mob it believes to be dead. The caller handles the absence instead;
    /// <see cref="Detection"/> is a struct, so the nullable costs nothing and the
    /// source that always has health keeps producing a value every time.
    /// </remarks>
    public Detection? ToDetection() => HpRatio is { } hp ? new Detection(Kind, X, Y, hp) : null;
}

/// <summary>A decoded tactical event (a hit, a death, a chat line).</summary>
public sealed record GameEvent(GameEventKind Kind, long EntityId, string Descriptor, DataSourceKind Source);

/// <summary>
/// The controlled character's own vitals, in absolute units.
/// </summary>
/// <remarks>
/// <para>
/// Kept apart from <see cref="EntitySighting"/>, which carries an HP
/// <i>ratio</i>. A ratio is what a sighting of somebody else yields; it is not
/// enough to plan on. Gate 3 asks for HP and max HP as numbers, and a ratio
/// cannot be turned into them without inventing one of the two.
/// </para>
/// <para>
/// So these come from their own message in the operator's map, and when the map
/// does not describe that message they are simply absent. Absent is the honest
/// answer: it makes Gate 3 refuse to plan, which is correct, where a manufactured
/// max HP would have made it plan on a number nobody observed.
/// </para>
/// </remarks>
/// <param name="ObservedAtUtc">
/// When the packet these came from crossed the wire, or null when the decoder did
/// not know. Without it every consumer stamps its own clock, and a reading
/// replayed from a recording made two days ago looks exactly as fresh as one that
/// arrived a moment ago — both CACHED, both "observed now". The recording's own
/// timestamp is on the packet, and carrying it is what lets a freshness rule tell
/// a retained live reading from a replayed old one with no special case for
/// either (ADR-0016).
/// </param>
public sealed record PlayerVitals(
    int Hp,
    int MaxHp,
    int Mp,
    bool? HasTarget,
    bool? InCombat,
    DataSourceKind Source,
    DateTime? ObservedAtUtc = null);

/// <summary>The observations decoded from one packet.</summary>
public sealed record DecodedObservations(
    ImmutableArray<EntitySighting> Sightings,
    ImmutableArray<GameEvent> Events,
    PlayerVitals? Vitals = null)
{
    public static readonly DecodedObservations Empty =
        new(ImmutableArray<EntitySighting>.Empty, ImmutableArray<GameEvent>.Empty);

    public bool IsEmpty => Sightings.IsEmpty && Events.IsEmpty && Vitals is null;
}

/// <summary>
/// Decodes scoped game packets into observations.
/// </summary>
/// <remarks>
/// A decoder never invents: an opcode it does not recognise yields
/// <see cref="DecodedObservations.Empty"/>, not a guessed entity. The real
/// NosTale decoder must be supplied with the actual opcode map; the built-in
/// decoder understands only the synthetic protocol used by the tests.
/// </remarks>
public interface IGamePacketDecoder
{
    string ProtocolName { get; }

    /// <summary>
    /// Whether this decoder is able to read the player's own vitals at all.
    /// </summary>
    /// <remarks>
    /// Not "did it read them", which is per batch, but "can it ever". A consumer
    /// that finds no vitals in a report needs the difference: a decoder that does
    /// not know where they are will never produce them, while one that does simply
    /// saw no such packet in this batch. Reporting the first case as the second
    /// would hide an unfinished protocol map behind a quiet wire, and the second as
    /// the first would blame the map for a gap of a few hundred milliseconds.
    /// </remarks>
    bool ReadsPlayerVitals { get; }

    /// <summary>Whether this decoder can attempt to read the given packet at all.</summary>
    bool CanDecode(ObservedPacket packet);

    DecodedObservations Decode(ObservedPacket packet);
}

/// <summary>
/// Accumulated result of observing a batch of packets, with honest counters so a
/// channel that decoded nothing is diagnosable rather than silently empty.
/// </summary>
public sealed record NetworkObservationReport(
    long Frame,
    ImmutableArray<EntitySighting> Sightings,
    ImmutableArray<GameEvent> Events,
    long ObservedPackets,
    long ScopedOutPackets,
    long DecodedPackets,
    long UndecodablePackets,
    DataSourceKind Source,
    // The most recent vitals decoded in this batch, or null when the map does not
    // describe them. Null is not "full health": it is "nobody said".
    PlayerVitals? Vitals = null,
    // Whether the decoder behind this report can read the player's vitals at all,
    // so a consumer can tell "not mapped" from "not in this batch".
    bool VitalsReadable = false);

/// <summary>
/// Pulls packets from a source, keeps only the game's own traffic, decodes them
/// and produces observations for the world model.
/// </summary>
public sealed class GameTrafficObserver
{
    private readonly INetworkObservationSource _source;
    private readonly ScopedGameTrafficFilter _filter;
    private readonly IGamePacketDecoder _decoder;
    private long _frame;

    public ScopedGameTrafficFilter Filter => _filter;

    /// <summary>Provenance of this channel: it can be no more trusted than its source.</summary>
    public DataSourceKind Source => _source.Source;

    public GameTrafficObserver(INetworkObservationSource source, ScopedGameTrafficFilter filter, IGamePacketDecoder decoder)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _filter = filter ?? throw new ArgumentNullException(nameof(filter));
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
    }

    /// <summary>
    /// How long one poll may spend draining the source.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The count alone is not a bound on a live wire. A real capture of the game
    /// delivers packets continuously — around 90 a second in the recordings — so a
    /// loop that keeps going "while a packet is available" keeps going as fast as
    /// the game keeps talking. Draining 4096 messages that way took 52 seconds on
    /// the first real session, during which the Gate 1 snapshot could not be
    /// produced and the operator API answered nothing at all.
    /// </para>
    /// <para>
    /// So a poll takes what has arrived and returns. What it did not take stays
    /// queued for the next one; the counters say how much was read, and a caller
    /// that wants more calls again.
    /// </para>
    /// </remarks>
    public static readonly TimeSpan DefaultPollBudget = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Drains the source, decoding every in-scope packet into observations.
    /// Out-of-scope packets are counted and dropped without being decoded, so
    /// non-game traffic never even reaches the decoder.
    /// </summary>
    /// <param name="maxPackets">Most packets one poll may take.</param>
    /// <param name="budget">
    /// Most time one poll may spend. <see cref="DefaultPollBudget"/> when omitted;
    /// <see cref="Timeout.InfiniteTimeSpan"/> to drain a finite source to its end.
    /// </param>
    public NetworkObservationReport ObservePending(int maxPackets = 4096, TimeSpan? budget = null)
    {
        if (maxPackets < 1) throw new ArgumentOutOfRangeException(nameof(maxPackets));

        TimeSpan limit = budget ?? DefaultPollBudget;
        bool bounded = limit != Timeout.InfiniteTimeSpan;
        if (bounded && limit < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(budget));
        long started = Stopwatch.GetTimestamp();

        var sightings = ImmutableArray.CreateBuilder<EntitySighting>();
        var events = ImmutableArray.CreateBuilder<GameEvent>();
        long observed = 0, scopedOut = 0, decoded = 0, undecodable = 0;
        PlayerVitals? vitals = null;

        for (int i = 0; i < maxPackets && _source.TryObserve(out ObservedPacket packet); i++)
        {
            // Checked after taking the packet, never before: a packet already read
            // out of the source would otherwise be dropped on the floor.
            if (bounded && i > 0 && Stopwatch.GetElapsedTime(started) >= limit)
            {
                Accumulate(packet);
                break;
            }

            Accumulate(packet);
        }

        void Accumulate(ObservedPacket packet)
        {
            observed++;
            if (!_filter.Admit(packet))
            {
                scopedOut++;
                return;
            }
            if (!_decoder.CanDecode(packet))
            {
                undecodable++;
                return;
            }

            DecodedObservations result = _decoder.Decode(packet);
            if (result.IsEmpty)
            {
                undecodable++;
                return;
            }

            decoded++;
            sightings.AddRange(result.Sightings);
            events.AddRange(result.Events);
            // Last one wins: within a batch the later message is the more recent
            // state, and keeping the first would report a stale HP as current.
            if (result.Vitals is not null) vitals = result.Vitals;
        }

        return new NetworkObservationReport(
            Interlocked.Increment(ref _frame),
            sightings.ToImmutable(),
            events.ToImmutable(),
            observed, scopedOut, decoded, undecodable,
            _source.Source,
            vitals,
            _decoder.ReadsPlayerVitals);
    }

    /// <summary>
    /// Folds the network observations into a world state, when the player's health
    /// is known.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The player's health comes from the vitals message when the decoder produced
    /// one, and from a sighting with entity id 0 otherwise — that id is the
    /// channel's convention for the controlled player. The order matters on the
    /// real wire: the NosTale server never sights the player at all, so a state
    /// built only from sightings was unreachable while the exact HP and max HP sat
    /// in the same report, unused. With neither, there is no observed HP and this
    /// returns false with a reason rather than a state.
    /// </para>
    /// <para>
    /// It used to default to <c>playerHp = 1.0, playerAlive = true</c> and return
    /// the state anyway. Those are not neutral placeholders: full health and alive
    /// are the two values a policy is least likely to intervene on, so a channel
    /// that had decoded nothing at all produced the world state most likely to be
    /// acted upon. <see cref="WorldModel.WorldState"/> carries no provenance, so
    /// nothing downstream could have told that apart from a real reading — which
    /// is why the refusal has to happen here, at the last point that still knows.
    /// </para>
    /// </remarks>
    public bool TryToWorldState(
        NetworkObservationReport report,
        out NosAi.Runtime.WorldModel.WorldState worldState,
        out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(report);
        worldState = null!;
        failureReason = null;

        EntitySighting? player = null;
        var entities = new List<NosAi.Runtime.WorldModel.EntityState>();
        foreach (EntitySighting sighting in report.Sightings)
        {
            if (sighting.EntityId == 0)
            {
                player = sighting;
                continue;
            }
            entities.Add(new NosAi.Runtime.WorldModel.EntityState(
                $"{sighting.Kind}#{sighting.EntityId}", sighting.Kind, sighting.X, sighting.Y, sighting.HpRatio));
        }

        // Max HP of zero would make this a division by zero, and it is also the
        // signature of a misread field; the decoder refuses it upstream, and this
        // does not assume that check stayed there.
        double? playerHp = report.Vitals is { MaxHp: > 0 } vitals
            ? (double)vitals.Hp / vitals.MaxHp
            : player?.HpRatio;

        if (playerHp is not { } hpRatio)
        {
            failureReason = report.Source == DataSourceKind.Unknown
                ? "no_network_observation"
                : report.VitalsReadable
                    ? "player_vitals_not_in_batch"
                    : "player_not_sighted";
            return false;
        }

        worldState = new NosAi.Runtime.WorldModel.WorldState(
            report.Frame, hpRatio > 0.0, hpRatio, entities);
        return true;
    }
}
