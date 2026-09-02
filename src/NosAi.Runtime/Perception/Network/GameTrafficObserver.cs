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
/// <param name="PositionObservedAtUtc">
/// When the packet that stated this position crossed the wire, or null when the
/// decoder did not know. It is the packet's own capture time, never the clock of
/// whoever polled: a poll stamps every replayed sighting "now", which is exactly
/// how a recording made yesterday would look as fresh as the live wire
/// (ADR-0016). A sighting is half position and half health, and the two halves
/// can come from packets minutes apart, so each carries its own instant - the
/// same reason the health itself is nullable rather than defaulted.
/// </param>
/// <param name="Vnum">
/// What the entity is, by the game's own number, or null when no packet has said.
/// Only <c>in</c> carries it — a move or a vitals update names the entity by id
/// alone — so it is remembered from the spawn and null for an entity first seen
/// moving. It is exposed and never interpreted here: type 3 on this wire is
/// monster and NPC together (docs/PROTOCOLLO_NOSTALE.md), and telling them apart
/// is the reference catalogue's answer to a lookup by vnum, not this decoder's
/// guess (docs/TASTI_E_BERSAGLIO.md § 5).
/// </param>
/// <param name="HpObservedAtUtc">
/// When the packet that stated this health crossed the wire; null when there is
/// no health here, or when the decoder did not know the time. For a move that
/// carries a remembered health this is older than the position's instant, and
/// that difference is the CACHED label made measurable.
/// </param>
public sealed record EntitySighting(
    long EntityId,
    string Kind,
    double X,
    double Y,
    double? HpRatio,
    DataSourceKind Source,
    DateTime? PositionObservedAtUtc = null,
    DateTime? HpObservedAtUtc = null,
    int? Vnum = null)
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
/// <param name="ObservedAtUtc">
/// When the packet crossed the wire, or null when the decoder did not know. An
/// event is an instant; without it a consumer can only say that something
/// happened, never how long ago.
/// </param>
public sealed record GameEvent(
    GameEventKind Kind,
    long EntityId,
    string Descriptor,
    DataSourceKind Source,
    DateTime? ObservedAtUtc = null);

/// <summary>Who hit the controlled character: an entity id and its type.</summary>
/// <remarks>
/// The two fields <c>su</c> confirms for the attacker. The id is what a
/// counter-attack aims at; the type is kept because a monster and a player call
/// for different answers from a reactive rule, and it costs nothing to carry.
/// </remarks>
public readonly record struct Aggressor(long EntityId, int EntityType);

/// <summary>The controlled character was hit: by whom, and when.</summary>
/// <remarks>
/// <para>
/// C1-2. Produced only once the decoder has established that the target of the
/// hit is <i>this</i> character, which needs the own entity id from <c>cond</c>.
/// Target type 1 alone says "a player was hit"; naming an aggressor on that alone
/// would let a stranger's fight next to the character nominate somebody for it
/// to attack. The asymmetry is ADR-0018's, resolved the other way: there a false
/// positive costs a fact the planner skips, here it would cost an act aimed at
/// the wrong entity, so the fact is withheld until the id is known.
/// </para>
/// <para>
/// It carries its own instant because the reason it exists is a reactive rule
/// with a decay window (C6-1), and a hit without a time cannot decay. The window
/// belongs to that consumer; this record never expires on its own.
/// </para>
/// </remarks>
public sealed record PlayerHit(Aggressor By, DateTime ObservedAtUtc, DataSourceKind Source);

/// <summary>An entity the controlled character acted on: an entity id and its type.</summary>
public readonly record struct TargetedEntity(long EntityId, int EntityType);

/// <summary>
/// <c>ct</c> with the controlled character as the source: which entity the
/// character is acting on, and when.
/// </summary>
/// <remarks>
/// <para>
/// The wire's answer to <i>which</i>; the screen keeps the answer to
/// <i>whether</i> (ADR-0018, docs/TASTI_E_BERSAGLIO.md § 6.3). No packet in any
/// capture clears a target, so this fact is sticky by nature: it names the last
/// entity the character acted on, and says nothing about whether a target is
/// still selected. A consumer that needs the latter reads <c>HasTarget</c>.
/// </para>
/// <para>
/// Produced only once <c>cond</c> has named the own id and the source of the
/// packet is that id — a monster's <c>ct</c> aimed at the character is not the
/// character's selection — and never for a cast the character aims at itself,
/// which the captures show for a self-buff and which selects nothing.
/// </para>
/// </remarks>
public sealed record PlayerTargetSelection(TargetedEntity Target, DateTime ObservedAtUtc, DataSourceKind Source);

/// <summary><c>sr slot</c>: a skill slot came off cooldown.</summary>
/// <remarks>
/// "Skill ready / cooldown ended, by skill slot" in docs/PROTOCOLLO_NOSTALE.md,
/// marked probable. It is the second half of <c>UseSkill</c>'s post-condition;
/// what it says is when the slot became usable again, not when it stopped being.
/// </remarks>
public sealed record SkillReady(int Slot, DateTime ObservedAtUtc, DataSourceKind Source);

/// <summary><c>ivn kind slot.vnum.amount.rarity</c>: what one inventory slot holds.</summary>
/// <param name="InventoryKind">
/// Field 1 of the packet, observed as <c>2</c>. The catalogue does not name it;
/// it is carried because the slot number alone was never observed to be unique
/// across it, and a reading keyed on the slot alone could overwrite one bag's
/// slot with another's. No meaning is attached to the number.
/// </param>
/// <param name="Amount">
/// How many of the item the slot holds. The observed shape is a slot with an
/// item in it, so an amount of zero is outside it and is not read.
/// </param>
public sealed record InventorySlotReading(
    int InventoryKind,
    int Slot,
    int Vnum,
    int Amount,
    int Rarity,
    DateTime ObservedAtUtc,
    DataSourceKind Source);

/// <summary><c>get takerType takerId dropId</c>: a ground item was picked up.</summary>
/// <param name="ByPlayer">
/// Whether the taker was the controlled character. True or false once
/// <c>cond</c> has named the character's id; null before, because taker type 1
/// says "a player picked it up" and not which one. Null is not false.
/// </param>
public sealed record ItemPickup(
    int TakerType,
    long TakerId,
    long DropId,
    bool? ByPlayer,
    DateTime ObservedAtUtc,
    DataSourceKind Source);

/// <summary><c>drop vnum dropId x y amount ? ownerId</c>: an item lying on the ground.</summary>
/// <param name="OwnerId">
/// Field 7, read as the owner's entity id because the catalogue records it so
/// and the capture showed the session's own id there. What a value of zero
/// would mean was never observed and is not decided here.
/// </param>
public sealed record GroundItem(
    int Vnum,
    long DropId,
    int X,
    int Y,
    int Amount,
    long OwnerId,
    DateTime ObservedAtUtc,
    DataSourceKind Source);

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
    DateTime? ObservedAtUtc = null,
    int? MaxMp = null);

/// <summary>The observations decoded from one packet.</summary>
/// <param name="PlayerAttackedAtUtc">
/// When this packet showed the player attacking, or null when it did not. The
/// wire's whole contribution to <c>HasTarget</c> (ADR-0018), and a timestamp
/// rather than a flag because the only question asked of it is whether a hit
/// landed after the screen looked. Null is not "the player is not attacking".
/// </param>
/// <param name="PlayerMovementSpeed">
/// Movement speed from a player <c>cond</c>, or null when this packet did not
/// carry one. Null is not speed zero.
/// </param>
/// <param name="PlayerEntityId">
/// The controlled character's own entity id, when this packet named it.
/// </param>
/// <remarks>
/// <para>
/// The id closes the gap ADR-0018 named and left open. The target composer uses
/// a <c>su</c> whose attacker is a player to contradict a screen that saw no
/// target frame, and entity type <c>1</c> alone does not separate the controlled
/// character from another player fighting nearby — so a stranger's attack could
/// produce a false disagreement. With the own id known, the check can ask
/// whether <i>this</i> character attacked.
/// </para>
/// <para>
/// It is observed, not configured: <c>cond</c> carries type and id, both
/// confirmed in docs/PROTOCOLLO_NOSTALE.md, and the server sends it for the
/// controlled character. Null until a packet says so, and never guessed.
/// </para>
/// </remarks>
/// <param name="PlayerHit">
/// This packet showed the controlled character being hit, by a named aggressor,
/// or null. Null is not "not hit": it is also what a hit on the character reads
/// as while the own id is still unknown (C1-2).
/// </param>
/// <param name="SkillReady">A skill slot came off cooldown on this packet (<c>sr</c>).</param>
/// <param name="InventorySlot">One inventory slot's contents, from <c>ivn</c>.</param>
/// <param name="Pickup">A ground item was picked up, from <c>get</c>.</param>
/// <param name="GroundItem">An item was seen on the ground, from <c>drop</c>.</param>
public sealed record DecodedObservations(
    ImmutableArray<EntitySighting> Sightings,
    ImmutableArray<GameEvent> Events,
    PlayerVitals? Vitals = null,
    DateTime? PlayerAttackedAtUtc = null,
    int? PlayerMovementSpeed = null,
    long? PlayerEntityId = null,
    PlayerHit? PlayerHit = null,
    SkillReady? SkillReady = null,
    InventorySlotReading? InventorySlot = null,
    ItemPickup? Pickup = null,
    GroundItem? GroundItem = null,
    PlayerTargetSelection? PlayerTarget = null)
{
    public static readonly DecodedObservations Empty =
        new(ImmutableArray<EntitySighting>.Empty, ImmutableArray<GameEvent>.Empty);

    public bool IsEmpty =>
        Sightings.IsEmpty && Events.IsEmpty && Vitals is null
        && PlayerMovementSpeed is null && PlayerEntityId is null
        && PlayerHit is null && SkillReady is null && InventorySlot is null
        && Pickup is null && GroundItem is null && PlayerTarget is null;
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
    bool VitalsReadable = false,
    // The most recent hit in this batch where the player was the attacker, or null
    // when the batch showed none. The wire's contribution to HasTarget (ADR-0018);
    // it contradicts the screen and never establishes the fact on its own.
    DateTime? PlayerAttackedAtUtc = null,
    // The character's movement speed from cond, or null when this batch carried
    // none. The bound F1-10's continuity check measures a step against; without it
    // that check cannot run and the position reads UNKNOWN rather than LIVE.
    int? PlayerMovementSpeed = null,
    // The controlled character's own entity id, once a packet has named it. Null
    // until then, never guessed.
    long? PlayerEntityId = null)
{
    /// <summary>
    /// The most recent hit on the controlled character in this batch, or null
    /// when the batch showed none it could attribute.
    /// </summary>
    /// <remarks>
    /// Most recent by the packet's own instant, not by batch order, for the same
    /// reason as <see cref="PlayerAttackedAtUtc"/>: an out-of-order packet must
    /// not move the answer backwards. Null is not "not hit".
    /// </remarks>
    public PlayerHit? LastPlayerHit { get; init; }

    /// <summary>
    /// The most recent entity the controlled character acted on in this batch,
    /// or null when the batch showed none it could attribute. Most recent by the
    /// packet's own instant.
    /// </summary>
    public PlayerTargetSelection? LastPlayerTarget { get; init; }

    /// <summary>Every <c>sr</c> in this batch, in wire order.</summary>
    public ImmutableArray<SkillReady> SkillsReady { get; init; } = ImmutableArray<SkillReady>.Empty;

    /// <summary>Every <c>ivn</c> in this batch, in wire order.</summary>
    public ImmutableArray<InventorySlotReading> InventorySlots { get; init; } = ImmutableArray<InventorySlotReading>.Empty;

    /// <summary>Every <c>get</c> in this batch, in wire order.</summary>
    public ImmutableArray<ItemPickup> Pickups { get; init; } = ImmutableArray<ItemPickup>.Empty;

    /// <summary>Every <c>drop</c> in this batch, in wire order.</summary>
    public ImmutableArray<GroundItem> GroundItems { get; init; } = ImmutableArray<GroundItem>.Empty;
}

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
        DateTime? playerAttackedAt = null;
        int? playerSpeed = null;
        long? playerEntityId = null;
        PlayerHit? playerHit = null;
        PlayerTargetSelection? playerTarget = null;
        var skillsReady = ImmutableArray.CreateBuilder<SkillReady>();
        var inventorySlots = ImmutableArray.CreateBuilder<InventorySlotReading>();
        var pickups = ImmutableArray.CreateBuilder<ItemPickup>();
        var groundItems = ImmutableArray.CreateBuilder<GroundItem>();

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
            // Latest wins on its own merit rather than on batch order: the
            // composer compares this against the screen's timestamp, and an
            // out-of-order packet must not move the answer backwards.
            if (result.PlayerAttackedAtUtc is { } attackedAt
                && (playerAttackedAt is null || attackedAt > playerAttackedAt))
            {
                playerAttackedAt = attackedAt;
            }
            // Last one wins: speed changes during a session, and the most recent
            // cond is the one a continuity check should be measured against.
            if (result.PlayerMovementSpeed is { } speed) playerSpeed = speed;
            // The id does not change within a session, so the first packet that
            // names it is as good as the last.
            playerEntityId ??= result.PlayerEntityId;
            // Latest by its own instant, as for the attack above: the batch that
            // carried the hit is rarely the batch a reactive rule asks about, and
            // an out-of-order packet must not make the last hit older.
            if (result.PlayerHit is { } hit
                && (playerHit is null || hit.ObservedAtUtc > playerHit.ObservedAtUtc))
            {
                playerHit = hit;
            }
            // Every one, in wire order. These are events and slot readings, not a
            // single current state; collapsing them to the last would lose the
            // ones a post-condition window needs to see.
            if (result.PlayerTarget is { } target
                && (playerTarget is null || target.ObservedAtUtc > playerTarget.ObservedAtUtc))
            {
                playerTarget = target;
            }
            if (result.SkillReady is { } ready) skillsReady.Add(ready);
            if (result.InventorySlot is { } slot) inventorySlots.Add(slot);
            if (result.Pickup is { } pickup) pickups.Add(pickup);
            if (result.GroundItem is { } drop) groundItems.Add(drop);
        }

        return new NetworkObservationReport(
            Interlocked.Increment(ref _frame),
            sightings.ToImmutable(),
            events.ToImmutable(),
            observed, scopedOut, decoded, undecodable,
            _source.Source,
            vitals,
            _decoder.ReadsPlayerVitals,
            playerAttackedAt,
            playerSpeed,
            playerEntityId)
        {
            LastPlayerHit = playerHit,
            LastPlayerTarget = playerTarget,
            SkillsReady = skillsReady.ToImmutable(),
            InventorySlots = inventorySlots.ToImmutable(),
            Pickups = pickups.ToImmutable(),
            GroundItems = groundItems.ToImmutable(),
        };
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
