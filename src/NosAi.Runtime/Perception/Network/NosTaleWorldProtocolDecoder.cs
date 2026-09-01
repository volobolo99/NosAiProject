using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Perception.Network;

/// <summary>
/// Turns one framed world-channel packet into observations the world model can use.
/// </summary>
/// <remarks>
/// <para>
/// The layer above <see cref="NosAi.LiveIntegration.Capture.NosTaleWorldFramer"/>.
/// The framer cuts the stream at verified packet boundaries and hands over the
/// encoded bytes; this class decodes them with <see cref="NosTaleWorldDecoder"/>
/// and reads the fields <c>docs/PROTOCOLLO_NOSTALE.md</c> marks confirmed or
/// probable. Nothing marked unknown is read. An unknown field that later turns
/// out to be needed is a new capture, not a guess from its neighbours.
/// </para>
/// <para>
/// Framing has already been verified by the time a packet arrives here, so a
/// reading taken wholly from one packet keeps that packet's provenance — LIVE
/// bytes that framed stay LIVE, a replay stays CACHED. There is no reconstructed
/// binary map to weaken the claim.
/// </para>
/// <para>
/// Entity sightings are the exception, and deliberately. Only <c>in</c> carries a
/// position and a health together; <c>mv</c> has position without health and
/// <c>st</c> health without position, so the sighting each of them produces is
/// half fresh and half remembered. Those are published CACHED — really observed,
/// no longer current — because a stale HP wearing a LIVE label is precisely what
/// the classification is for.
/// </para>
/// <para>
/// HasTarget and InCombat are not established on any packet this class reads,
/// so they stay null and the provider publishes them UNKNOWN. Inferring
/// combat from a recent hit would be a derivation wearing a LIVE label.
/// </para>
/// </remarks>
public sealed class NosTaleWorldProtocolDecoder : IGamePacketDecoder
{
    private readonly Dictionary<long, TrackedEntity> _entities = new();

    /// <summary>
    /// The controlled character's own entity id, once <c>cond</c> has named it.
    /// </summary>
    /// <remarks>
    /// Null until the wire says so. It is never guessed and never configured: an
    /// id supplied by hand would be a number nobody observed deciding whether a
    /// hit was the player's, which is the shape of mistake this decoder exists to
    /// avoid.
    /// </remarks>
    private long? _playerEntityId;

    /// <inheritdoc />
    public string ProtocolName => "nostale-world-observed";

    /// <inheritdoc />
    /// <remarks>
    /// <c>stat</c> carries them and the HUD confirmed it. They arrive when they
    /// change, not on a schedule — 62 packets in 90 s of combat and 22 in an idle
    /// capture — so most batches contain none, and that is not the same thing as
    /// not being able to read them.
    /// </remarks>
    public bool ReadsPlayerVitals => true;

    /// <inheritdoc />
    public bool CanDecode(ObservedPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return packet.Direction == NetworkDirection.Inbound
            && TryReadText(packet.Payload.Span, out string text)
            && text.Length > 0;
    }

    /// <inheritdoc />
    public DecodedObservations Decode(ObservedPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        if (packet.Direction != NetworkDirection.Inbound)
            return DecodedObservations.Empty;
        if (!TryReadText(packet.Payload.Span, out string text) || text.Length == 0)
            return DecodedObservations.Empty;

        string[] fields = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length == 0)
            return DecodedObservations.Empty;

        DataSourceKind source = packet.Source;
        return fields[0] switch
        {
            "stat" => DecodeStat(fields, source, packet.CapturedUtc),
            "st" => DecodeOtherVitals(fields, source),
            "in" => DecodeEnter(fields, source),
            "mv" => DecodeMove(fields, source),
            "die" => DecodeDeath(fields, source),
            "su" => DecodeHit(fields, source, packet.CapturedUtc),
            "cond" => DecodeCondition(fields),
            _ => DecodedObservations.Empty,
        };
    }

    /// <summary>
    /// <c>stat hp maxHp mp maxMp …</c> — the player's own vitals, confirmed
    /// against the HUD. Fields after max MP are unknown and are not read.
    /// </summary>
    private static DecodedObservations DecodeStat(string[] fields, DataSourceKind source, DateTime capturedUtc)
    {
        if (fields.Length < 5)
            return DecodedObservations.Empty;
        if (!TryInt(fields[1], out int hp)
            || !TryInt(fields[2], out int maxHp)
            || !TryInt(fields[3], out int mp)
            || !TryInt(fields[4], out int maxMp))
            return DecodedObservations.Empty;

        if (!VitalsArePlausible(hp, maxHp, mp) || maxMp < 0 || mp > maxMp)
            return DecodedObservations.Empty;

        return new DecodedObservations(
            ImmutableArray<EntitySighting>.Empty,
            ImmutableArray<GameEvent>.Empty,
            new PlayerVitals(hp, maxHp, mp, HasTarget: null, InCombat: null, source, capturedUtc, MaxMp: maxMp));
    }

    /// <summary>
    /// <c>st type id lv ? hp% mp% hp mp maxHp maxMp ?</c> — another entity's
    /// vitals. Absolute HP and max HP (fields 7 and 9) are used; the percentage
    /// at field 5 disagrees with those values across the capture and is ignored.
    /// </summary>
    private DecodedObservations DecodeOtherVitals(string[] fields, DataSourceKind source)
    {
        if (fields.Length < 10 || !IsReadableEntity(fields[1]))
            return DecodedObservations.Empty;
        if (!TryLong(fields[2], out long entityId)
            || !TryInt(fields[7], out int hp)
            || !TryInt(fields[9], out int maxHp))
            return DecodedObservations.Empty;
        if (maxHp <= 0 || hp < 0 || hp > maxHp)
            return DecodedObservations.Empty;

        double hpRatio = (double)hp / maxHp;
        TrackedEntity previous = _entities.GetValueOrDefault(entityId);
        _entities[entityId] = previous with { HpRatio = hpRatio, HasHp = true };

        if (!previous.HasPosition)
            return DecodedObservations.Empty;

        // Fresh health, and a position from whichever packet last carried one.
        return Sighting(entityId, previous.X, previous.Y, hpRatio, Stale(source));
    }

    /// <summary>
    /// <c>in type vnum id x y dir hp% mp% …</c> — an entity enters view.
    /// The tail after mp% is unknown and is not read.
    /// </summary>
    private DecodedObservations DecodeEnter(string[] fields, DataSourceKind source)
    {
        if (fields.Length < 9 || !IsReadableEntity(fields[1]))
            return DecodedObservations.Empty;
        if (!TryLong(fields[3], out long entityId)
            || !TryDouble(fields[4], out double x)
            || !TryDouble(fields[5], out double y)
            || !TryInt(fields[7], out int hpPercent))
            return DecodedObservations.Empty;
        if (hpPercent is < 0 or > 100)
            return DecodedObservations.Empty;

        double hpRatio = hpPercent / 100.0;
        _entities[entityId] = new TrackedEntity(x, y, hpRatio, HasHp: true, HasPosition: true);
        // Position and health both come from this packet, so nothing stale is
        // mixed in and the sighting keeps the packet's own provenance.
        return Sighting(entityId, x, y, hpRatio, source);
    }

    /// <summary>
    /// <c>mv type id x y speed</c> — position update. HP is not on this packet,
    /// so the sighting carries a health of null unless a previous <c>in</c> or
    /// <c>st</c> supplied one. A move of an entity never seen with health is
    /// still a sighting: it says where, and says nothing about how healthy,
    /// rather than inventing full health or being dropped.
    /// </summary>
    private DecodedObservations DecodeMove(string[] fields, DataSourceKind source)
    {
        if (fields.Length < 5 || !IsReadableEntity(fields[1]))
            return DecodedObservations.Empty;
        if (!TryLong(fields[2], out long entityId)
            || !TryDouble(fields[3], out double x)
            || !TryDouble(fields[4], out double y))
            return DecodedObservations.Empty;

        TrackedEntity previous = _entities.GetValueOrDefault(entityId);
        _entities[entityId] = previous with { X = x, Y = y, HasPosition = true };

        // With health from an earlier packet the position is fresh and the health
        // is not, so the sighting is marked stale. With no health at all there is
        // nothing stale mixed in, and the packet keeps its own provenance.
        return previous.HasHp
            ? Sighting(entityId, x, y, previous.HpRatio, Stale(source))
            : Sighting(entityId, x, y, null, source);
    }

    /// <summary><c>die type id …</c> — the entity is gone.</summary>
    private DecodedObservations DecodeDeath(string[] fields, DataSourceKind source)
    {
        if (fields.Length < 3 || !TryLong(fields[2], out long entityId))
            return DecodedObservations.Empty;

        _entities.Remove(entityId);
        return new DecodedObservations(
            ImmutableArray<EntitySighting>.Empty,
            ImmutableArray.Create(new GameEvent(GameEventKind.EntityDeath, entityId, "die", source)));
    }

    /// <summary>
    /// <c>su atkType atkId tgtType tgtId …</c> — a hit resolving. Identities are
    /// confirmed. The last fields sometimes repeat the player's HP, but they
    /// never carry MP, so this packet is the hit event and not a vitals source.
    /// </summary>
    /// <remarks>
    /// The attacker type separates the packet's two shapes: type
    /// <see cref="PlayerEntityType"/> is the player-attacks shape, type 3 the
    /// monster-attacks one (docs/PROTOCOLLO_NOSTALE.md). A player-attacks hit
    /// carries its capture time out as the wire's contribution to
    /// <c>HasTarget</c> — it contradicts a screen that saw no target frame, and it
    /// never establishes the fact on its own (ADR-0018).
    /// </remarks>
    private DecodedObservations DecodeHit(string[] fields, DataSourceKind source, DateTime capturedUtc)
    {
        if (fields.Length < 5
            || !TryLong(fields[4], out long targetId)
            || !TryInt(fields[1], out int attackerType)
            || !TryLong(fields[2], out long attackerId)
            || !TryInt(fields[3], out _))
            return DecodedObservations.Empty;

        // Type 1 alone says "a player attacked", not "this character attacked".
        // Once cond has named the controlled character, the id decides, and a
        // stranger fighting nearby stops contradicting the screen. Until then the
        // type is all there is, and a false disagreement costs a fact the planner
        // skips rather than a confident wrong answer (ADR-0018).
        bool playerAttacked = _playerEntityId is { } own
            ? attackerId == own
            : attackerType == PlayerEntityType;

        return new DecodedObservations(
            ImmutableArray<EntitySighting>.Empty,
            ImmutableArray.Create(new GameEvent(GameEventKind.CombatHit, targetId, "su", source)),
            Vitals: null,
            PlayerAttackedAtUtc: playerAttacked ? capturedUtc : null);
    }

    /// <summary>
    /// <c>cond type id ? ? speed</c> — the player's movement speed, entity type 1
    /// only. Speed is <c>probable</c> in docs/PROTOCOLLO_NOSTALE.md (11 for a
    /// level-56 character). The two fields between id and speed are also marked
    /// probable there and have never been observed asserted; they are not read.
    /// This is state, not an event: no <see cref="GameEvent"/> is emitted.
    /// </summary>
    private DecodedObservations DecodeCondition(string[] fields)
    {
        if (fields.Length < 6)
            return DecodedObservations.Empty;
        if (!TryInt(fields[1], out int entityType) || entityType != PlayerEntityType)
            return DecodedObservations.Empty;
        if (!TryLong(fields[2], out long entityId) || entityId <= 0)
            return DecodedObservations.Empty;
        if (!TryInt(fields[5], out int speed) || speed < 0)
            return DecodedObservations.Empty;

        // The server sends cond for the controlled character, so field 2 is this
        // session's own entity id — the fact ADR-0018 needed and did not have.
        _playerEntityId = entityId;

        return new DecodedObservations(
            ImmutableArray<EntitySighting>.Empty,
            ImmutableArray<GameEvent>.Empty,
            PlayerMovementSpeed: speed,
            PlayerEntityId: entityId);
    }

    private static DecodedObservations Sighting(
        long entityId, double x, double y, double? hpRatio, DataSourceKind source)
        => new(
            ImmutableArray.Create(new EntitySighting(entityId, "Monster", x, y, hpRatio, source)),
            ImmutableArray<GameEvent>.Empty);

    /// <summary>
    /// Whether an entity message carries the one layout this decoder can read.
    /// </summary>
    /// <remarks>
    /// Type 3 — monster or NPC — is the only type the captures ever showed in
    /// <c>in</c>, <c>mv</c> and <c>st</c>, and the shapes in the catalogue are that
    /// type's. Type 1 (the player) is confirmed only in <c>su</c>, <c>cond</c> and
    /// <c>sayi</c>, and another player entering view carries a name where a monster
    /// carries a vnum — so the same field positions would read a coordinate out of
    /// something else. Type 2 was never observed at all. Both are refused rather
    /// than read at positions nobody has established.
    /// </remarks>
    private static bool IsReadableEntity(string typeField) => typeField == "3";

    /// <summary>
    /// The entity type a player carries, confirmed in <c>su</c>, <c>cond</c> and
    /// <c>sayi</c>.
    /// </summary>
    /// <remarks>
    /// It does not distinguish the controlled character from another player
    /// fighting nearby, and nothing on the read side of the wire establishes the
    /// character's own entity id. The consequence is a possible false
    /// disagreement in <see cref="TargetStateComposer"/>, whose result is UNKNOWN
    /// — a fact the planner then skips, never a confident wrong answer. ADR-0018
    /// records this as the place to tighten once the own id is available.
    /// </remarks>
    private const int PlayerEntityType = 1;

    /// <summary>
    /// A reading that mixes this packet with an earlier observation of the same
    /// entity.
    /// </summary>
    /// <remarks>
    /// <c>mv</c> carries a position and no health; <c>st</c> carries health and no
    /// position. Either way half the sighting was observed earlier, and how much
    /// earlier is not bounded — an entity can move for minutes between the packets
    /// that mention its health. CACHED is exactly that: really observed, no longer
    /// current. Labelling the whole sighting with the fresh half's provenance would
    /// present a stale HP as a current one, which is the lie the classification
    /// exists to prevent (ADR-0012).
    /// </remarks>
    private static DataSourceKind Stale(DataSourceKind source) => source switch
    {
        DataSourceKind.Live or DataSourceKind.Derived => DataSourceKind.Cached,
        // Simulated and Unknown are already no better than cached; a replay is
        // cached already.
        _ => source,
    };

    /// <summary>
    /// The same checks <see cref="ConfigurableProtocolDecoder"/> applies: a
    /// misplaced field reads as an integer, and the only thing separating a
    /// real HP from a stray one is whether it makes sense as HP.
    /// </summary>
    private static bool VitalsArePlausible(int hp, int maxHp, int mp)
        => maxHp > 0 && hp >= 0 && hp <= maxHp && mp >= 0;

    /// <summary>
    /// The framer hands over encoded bytes; tests may hand over the printable
    /// line directly. Either way the rest of this class sees one ASCII packet.
    /// </summary>
    private static bool TryReadText(ReadOnlySpan<byte> payload, out string text)
    {
        if (TryReadAscii(payload, out text))
            return true;

        IReadOnlyList<string> packets = NosTaleWorldDecoder.Decode(payload);
        if (packets.Count != 1 || packets[0].Length == 0)
        {
            text = "";
            return false;
        }

        text = packets[0];
        return true;
    }

    private static bool TryReadAscii(ReadOnlySpan<byte> payload, out string text)
    {
        text = "";
        if (payload.Length == 0)
            return false;
        foreach (byte b in payload)
        {
            if (b is < 0x20 or > 0x7E)
                return false;
        }
        text = Encoding.ASCII.GetString(payload);
        return true;
    }

    private static bool TryInt(string field, out int value)
        => int.TryParse(field, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static bool TryLong(string field, out long value)
        => long.TryParse(field, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static bool TryDouble(string field, out double value)
        => double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private readonly record struct TrackedEntity(
        double X, double Y, double HpRatio, bool HasHp, bool HasPosition);
}
