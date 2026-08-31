// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Versione: 1.0 Beta
// Percezione — Observer del traffico di gioco: scope → decodifica → osservazioni
// ============================================================================
//
// L'osservazione di rete converge nel World Model esattamente come la visione: i
// pacchetti del gioco diventano EntitySighting (proiettabili in Detection) ed
// eventi tattici, utili a combattimento, previsione e calcolo strategie.
//
// Il formato wire reale di NosTale è proprietario e NON è incluso in questo
// repository. Inventarne gli opcode significherebbe spacciare congetture per
// osservazioni: il decoder è quindi un'interfaccia, con un decoder sintetico per
// i test, mentre il decoder NosTale reale è un punto d'integrazione esplicito e
// dichiarato mancante.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
public sealed record EntitySighting(long EntityId, string Kind, double X, double Y, double HpRatio, DataSourceKind Source)
{
    /// <summary>Projects into the perception Detection consumed by the world model.</summary>
    public Detection ToDetection() => new(Kind, X, Y, HpRatio);
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
public sealed record PlayerVitals(
    int Hp,
    int MaxHp,
    int Mp,
    bool? HasTarget,
    bool? InCombat,
    DataSourceKind Source);

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
    PlayerVitals? Vitals = null);

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
    /// Drains the source, decoding every in-scope packet into observations.
    /// Out-of-scope packets are counted and dropped without being decoded, so
    /// non-game traffic never even reaches the decoder.
    /// </summary>
    public NetworkObservationReport ObservePending(int maxPackets = 4096)
    {
        if (maxPackets < 1) throw new ArgumentOutOfRangeException(nameof(maxPackets));

        var sightings = ImmutableArray.CreateBuilder<EntitySighting>();
        var events = ImmutableArray.CreateBuilder<GameEvent>();
        long observed = 0, scopedOut = 0, decoded = 0, undecodable = 0;
        PlayerVitals? vitals = null;

        for (int i = 0; i < maxPackets && _source.TryObserve(out ObservedPacket packet); i++)
        {
            observed++;
            if (!_filter.Admit(packet))
            {
                scopedOut++;
                continue;
            }
            if (!_decoder.CanDecode(packet))
            {
                undecodable++;
                continue;
            }

            DecodedObservations result = _decoder.Decode(packet);
            if (result.IsEmpty)
            {
                undecodable++;
                continue;
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
            vitals);
    }

    /// <summary>
    /// Folds the network sightings into a world state, when the player was seen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Entity id 0 is the controlled player by convention. When no sighting
    /// carries it, there is no observed HP, and this returns false with a reason
    /// rather than a state.
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

        if (player is null)
        {
            failureReason = report.Source == DataSourceKind.Unknown
                ? "no_network_observation"
                : "player_not_sighted";
            return false;
        }

        worldState = new NosAi.Runtime.WorldModel.WorldState(
            report.Frame, player.HpRatio > 0.0, player.HpRatio, entities);
        return true;
    }
}
