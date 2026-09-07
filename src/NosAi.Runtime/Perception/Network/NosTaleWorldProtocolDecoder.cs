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
/// Twelve opcodes are read. Seven carry the world: <c>stat</c>, <c>st</c>,
/// <c>in</c>, <c>mv</c>, <c>die</c>, <c>su</c>, <c>cond</c>. Four carry the
/// facts a post-condition needs (C1-3): <c>sr</c>, <c>ivn</c>, <c>get</c>,
/// <c>drop</c>; <c>ct</c> carries which entity the character acts on. Those
/// five are marked <i>probable</i> in the catalogue, and the
/// discipline for a probable reading is the one <c>in</c> and <c>st</c> already
/// follow for their probable fields: the reading keeps the packet's provenance
/// — LIVE bytes that framed stay LIVE — because provenance says where the bytes
/// came from and that their framing was verified (ADR-0014), while the guard
/// against a misread is the shape check. A packet with fewer fields than the
/// observed shape, or a field outside what that field can plausibly hold, is
/// not the packet the catalogue describes and is not read at all, on the same
/// principle by which <see cref="VitalsArePlausible"/> refuses a <c>stat</c>
/// whose HP exceeds its maximum. What is refused produces nothing; nothing is
/// ever clamped or defaulted into a value.
/// </para>
/// <para>
/// Framing has already been verified by the time a packet arrives here, so a
/// reading taken wholly from one packet keeps that packet's provenance and
/// carries that packet's capture time. There is no reconstructed binary map to
/// weaken the claim, and there is no clock of this class's own: every instant
/// on an observation is the instant the packet crossed the wire.
/// </para>
/// <para>
/// Entity sightings are the exception on provenance, and deliberately. Only
/// <c>in</c> carries a position and a health together; <c>mv</c> has position
/// without health and <c>st</c> health without position, so the sighting each
/// of them produces is half fresh and half remembered. Those are published
/// CACHED — really observed, no longer current — because a stale HP wearing a
/// LIVE label is precisely what the classification is for. Each half also keeps
/// the instant of the packet that stated it, so a consumer can measure what
/// CACHED cost rather than only be told it.
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
        DateTime at = packet.CapturedUtc;
        return fields[0] switch
        {
            "stat" => DecodeStat(fields, source, at),
            "st" => DecodeOtherVitals(fields, source, at),
            "in" => DecodeEnter(fields, source, at),
            "mv" => DecodeMove(fields, source, at),
            "die" => DecodeDeath(fields, source, at),
            "su" => DecodeHit(fields, source, at),
            "cond" => DecodeCondition(fields),
            "lev" => DecodeProgression(fields, source, at),
            "eq" => DecodeEq(fields, source, at),
            "equip" => DecodeEquip(fields, source, at),
            "sr" => DecodeSkillReady(fields, source, at),
            "ivn" => DecodeInventorySlot(fields, source, at),
            "get" => DecodePickup(fields, source, at),
            "drop" => DecodeGroundItem(fields, source, at),
            "ct" => DecodeCast(fields, source, at),
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
    private DecodedObservations DecodeOtherVitals(string[] fields, DataSourceKind source, DateTime capturedUtc)
    {
        if (fields.Length < 10 || !IsReadableEntity(fields[1]))
            return DecodedObservations.Empty;
        if (!TryLong(fields[2], out long entityId)
            || !TryInt(fields[7], out int hp)
            || !TryInt(fields[9], out int maxHp))
            return DecodedObservations.Empty;
        if (maxHp <= 0 || hp < 0 || hp > maxHp)
            return DecodedObservations.Empty;

        // The ratio is kept exactly as before, and the two numbers it was
        // divided from are kept beside it: they are already validated above,
        // and dividing them away was the only reason the World Model could not
        // state a monster's health in points.
        var vitals = new AbsoluteVitals(hp, maxHp);
        double hpRatio = (double)hp / maxHp;
        TrackedEntity previous = _entities.GetValueOrDefault(entityId);
        _entities[entityId] = previous with
        {
            HpRatio = hpRatio, HasHp = true, HpAtUtc = capturedUtc, Vitals = vitals
        };

        if (!previous.HasPosition)
            return DecodedObservations.Empty;

        // Fresh health, and a position from whichever packet last carried one —
        // stamped with that packet's instant, not this one's.
        return Sighting(
            entityId, previous.X, previous.Y, hpRatio, Stale(source),
            positionAtUtc: previous.PositionAtUtc, hpAtUtc: capturedUtc, vnum: previous.Vnum,
            vitals: vitals);
    }

    /// <summary>
    /// <c>in type vnum id x y dir hp% mp% …</c> — an entity enters view.
    /// The tail after mp% is unknown and is not read.
    /// </summary>
    private DecodedObservations DecodeEnter(string[] fields, DataSourceKind source, DateTime capturedUtc)
    {
        if (fields.Length < 9 || !IsReadableEntity(fields[1]))
            return DecodedObservations.Empty;
        if (!TryInt(fields[2], out int vnum)
            || !TryLong(fields[3], out long entityId)
            || !TryDouble(fields[4], out double x)
            || !TryDouble(fields[5], out double y)
            || !TryInt(fields[7], out int hpPercent))
            return DecodedObservations.Empty;
        // The vnum is confirmed (36, 45, 9, 96 in the captures, each grouping
        // identical monsters); a non-positive one is a misplaced field, and the
        // packet is refused whole rather than read with a hole in it.
        if (vnum <= 0 || hpPercent is < 0 or > 100)
            return DecodedObservations.Empty;

        double hpRatio = hpPercent / 100.0;
        // No absolute pair, and none reconstructed: hp% is all this packet
        // states, and a maximum inferred from a percentage would be a number
        // the wire never said. Replacing the whole tracked entity also drops
        // any pair an earlier `st` left, which is right -- this is a newer,
        // coarser statement of the same fact, not a stale one to fall back on.
        _entities[entityId] = new TrackedEntity(
            x, y, hpRatio, HasHp: true, HasPosition: true,
            PositionAtUtc: capturedUtc, HpAtUtc: capturedUtc, Vnum: vnum);
        // Position and health both come from this packet, so nothing stale is
        // mixed in and the sighting keeps the packet's own provenance and time.
        return Sighting(entityId, x, y, hpRatio, source, capturedUtc, capturedUtc, vnum, vitals: null);
    }

    /// <summary>
    /// <c>mv type id x y speed</c> — position update. HP is not on this packet,
    /// so the sighting carries a health of null unless a previous <c>in</c> or
    /// <c>st</c> supplied one. A move of an entity never seen with health is
    /// still a sighting: it says where, and says nothing about how healthy,
    /// rather than inventing full health or being dropped.
    /// </summary>
    private DecodedObservations DecodeMove(string[] fields, DataSourceKind source, DateTime capturedUtc)
    {
        if (fields.Length < 5 || !IsReadableEntity(fields[1]))
            return DecodedObservations.Empty;
        if (!TryLong(fields[2], out long entityId)
            || !TryDouble(fields[3], out double x)
            || !TryDouble(fields[4], out double y))
            return DecodedObservations.Empty;

        TrackedEntity previous = _entities.GetValueOrDefault(entityId);
        _entities[entityId] = previous with { X = x, Y = y, HasPosition = true, PositionAtUtc = capturedUtc };

        // With health from an earlier packet the position is fresh and the health
        // is not, so the sighting is marked stale and the health keeps the older
        // instant. With no health at all there is nothing stale mixed in, and the
        // packet keeps its own provenance.
        return previous.HasHp
            ? Sighting(entityId, x, y, previous.HpRatio, Stale(source), capturedUtc, previous.HpAtUtc, previous.Vnum, previous.Vitals)
            : Sighting(entityId, x, y, null, source, capturedUtc, null, previous.Vnum, vitals: null);
    }

    /// <summary><c>die type id …</c> — the entity is gone.</summary>
    private DecodedObservations DecodeDeath(string[] fields, DataSourceKind source, DateTime capturedUtc)
    {
        if (fields.Length < 3 || !TryLong(fields[2], out long entityId))
            return DecodedObservations.Empty;

        // Read the vnum before removing the entry: this is the last chance to
        // know which species died, and the caller needs it to ever confirm
        // "killed a mob of vnum X" from this event.
        int? vnum = _entities.GetValueOrDefault(entityId).Vnum;
        _entities.Remove(entityId);
        return new DecodedObservations(
            ImmutableArray<EntitySighting>.Empty,
            ImmutableArray.Create(new GameEvent(GameEventKind.EntityDeath, entityId, "die", source, capturedUtc, vnum)));
    }

    /// <summary>
    /// <c>su atkType atkId tgtType tgtId …</c> — a hit resolving. Identities are
    /// confirmed. The packet is the hit event, and — measured below — also a
    /// vitals source for the target when field 11 is 1.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The attacker type separates the packet's two shapes: type
    /// <see cref="PlayerEntityType"/> is the player-attacks shape, type 3 the
    /// monster-attacks one (docs/PROTOCOLLO_NOSTALE.md). A player-attacks hit
    /// carries its capture time out as the wire's contribution to
    /// <c>HasTarget</c> — it contradicts a screen that saw no target frame, and it
    /// never establishes the fact on its own (ADR-0018).
    /// </para>
    /// <para>
    /// The other direction is C1-2: a hit whose target is <i>this</i> character
    /// names its aggressor. That needs the own id, and the two directions are
    /// gated differently on purpose. The contradiction may run on the type alone
    /// because its false positive is UNKNOWN, a fact the planner skips. The
    /// aggressor may not, because its false positive would be a counter-attack
    /// aimed at whoever a stranger nearby happened to be fighting.
    /// </para>
    /// <para>
    /// Target vitals, measured across the four captures that carry <c>su</c>
    /// (212 packets: nostale_live 13, equip_test 2, nostale_combat 117,
    /// certificazione 80; all 18 fields): field 11 is a presence flag, and in
    /// every one of the 212 it equals 1 exactly when field 16 is positive —
    /// 172 packets with flag 1, 40 with flag 0. Where flag 1, field 16 is at
    /// most field 17 and field 17 is positive, in all 172; field 17 is constant
    /// per target id (40 distinct targets). A flag of 0 makes field 16 a zero
    /// that means <i>not reported</i>, never <i>dead</i>, so the decoder reads
    /// the target's HP and max HP only when field 11 is 1.
    /// </para>
    /// </remarks>
    private DecodedObservations DecodeHit(string[] fields, DataSourceKind source, DateTime capturedUtc)
    {
        if (fields.Length < 5
            || !TryLong(fields[4], out long targetId)
            || !TryInt(fields[1], out int attackerType)
            || !TryLong(fields[2], out long attackerId)
            || !TryInt(fields[3], out int targetType))
            return DecodedObservations.Empty;

        // Type 1 alone says "a player attacked", not "this character attacked".
        // Once cond has named the controlled character, the id decides, and a
        // stranger fighting nearby stops contradicting the screen. Until then the
        // type is all there is, and a false disagreement costs a fact the planner
        // skips rather than a confident wrong answer (ADR-0018).
        bool playerAttacked = _playerEntityId is { } own
            ? attackerId == own
            : attackerType == PlayerEntityType;

        // The aggressor is published only when the target is known to be this
        // character: own id known, and both the id and the confirmed type agree.
        // A target of type 1 with an unknown own id is "a player was hit", which
        // does not name anybody this character has a reason to attack.
        // Never the character itself: the captures show a self-buff as a su whose
        // attacker and target are both the own id, and a reactive rule handed
        // that would counter-attack its own character.
        PlayerHit? hit = _playerEntityId is { } self
            && targetType == PlayerEntityType
            && targetId == self
            && attackerId != self
                ? new PlayerHit(new Aggressor(attackerId, attackerType), capturedUtc, source)
                : null;

        ImmutableArray<EntitySighting> sightings = ReadTargetVitals(fields, targetId, source, capturedUtc);

        return new DecodedObservations(
            sightings,
            ImmutableArray.Create(new GameEvent(GameEventKind.CombatHit, targetId, "su", source, capturedUtc)),
            Vitals: null,
            PlayerAttackedAtUtc: playerAttacked ? capturedUtc : null,
            PlayerHit: hit);
    }

    /// <summary>
    /// The target's vitals when <c>su</c> reports them, or no sighting at all.
    /// </summary>
    /// <remarks>
    /// Field 11 is a presence flag: only when it is 1 do fields 16 and 17 name
    /// the target's current and maximum HP, and a 0 there means the target's HP
    /// is not reported on this packet, not that the target is dead. Reading the
    /// pair without the flag would publish "0 HP" forty times out of 212 in these
    /// captures, every one of them a lie. The same plausibility check
    /// <see cref="DecodeOtherVitals"/> applies — <c>maxHp &gt; 0</c> and
    /// <c>0 &lt;= hp &lt;= maxHp</c> — refuses an implausible pair, and the
    /// tracked entity and the sighting are updated exactly as that method does,
    /// so a remembered position carries the fresh health out as CACHED.
    /// The controlled character is never published here: <c>stat</c> is the
    /// source for its own life, and a second, possibly different number from
    /// <c>su</c> would be a conflict this decoder invented.
    /// </remarks>
    private ImmutableArray<EntitySighting> ReadTargetVitals(
        string[] fields, long targetId, DataSourceKind source, DateTime capturedUtc)
    {
        if (fields.Length < 18)
            return ImmutableArray<EntitySighting>.Empty;
        if (_playerEntityId is { } own && targetId == own)
            return ImmutableArray<EntitySighting>.Empty;
        if (!TryInt(fields[11], out int vitalsFlag) || vitalsFlag != 1)
            return ImmutableArray<EntitySighting>.Empty;
        if (!TryInt(fields[16], out int hp) || !TryInt(fields[17], out int maxHp))
            return ImmutableArray<EntitySighting>.Empty;
        if (maxHp <= 0 || hp < 0 || hp > maxHp)
            return ImmutableArray<EntitySighting>.Empty;

        var vitals = new AbsoluteVitals(hp, maxHp);
        double hpRatio = (double)hp / maxHp;
        TrackedEntity previous = _entities.GetValueOrDefault(targetId);
        _entities[targetId] = previous with
        {
            HpRatio = hpRatio, HasHp = true, HpAtUtc = capturedUtc, Vitals = vitals
        };

        if (!previous.HasPosition)
            return ImmutableArray<EntitySighting>.Empty;

        return ImmutableArray.Create(new EntitySighting(
            targetId, "Monster", previous.X, previous.Y, hpRatio, Stale(source),
            previous.PositionAtUtc, capturedUtc, previous.Vnum, vitals));
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

    /// <summary>
    /// <c>lev level xp jobLevel jobXp xpMax jobXpMax …</c> — the player's own
    /// progression. The six read fields are marked <i>probable</i> in the
    /// catalogue; everything after the sixth is unknown and is not read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read whole or not at all. The failure mode that matters here is a
    /// half-read progression: a level published with a garbage XP beside it is a
    /// fact nobody observed, so any field that is not the number it should be —
    /// or outside what that number can plausibly hold — refuses the packet whole,
    /// on the same principle by which <c>stat</c> refuses an HP above its
    /// maximum. Nothing is clamped into a value.
    /// </para>
    /// <para>
    /// This is state, not an event: no <see cref="GameEvent"/> is emitted, and
    /// <c>lev</c> describes the player, not an entity in view.
    /// </para>
    /// </remarks>
    private static DecodedObservations DecodeProgression(string[] fields, DataSourceKind source, DateTime capturedUtc)
    {
        if (fields.Length < 7)
            return DecodedObservations.Empty;
        if (!TryInt(fields[1], out int level)
            || !TryLong(fields[2], out long experience)
            || !TryInt(fields[3], out int jobLevel)
            || !TryLong(fields[4], out long jobExperience)
            || !TryLong(fields[5], out long experienceForNextLevel)
            || !TryLong(fields[6], out long jobExperienceForNextJobLevel))
            return DecodedObservations.Empty;
        if (level <= 0 || jobLevel <= 0 || experience < 0 || jobExperience < 0
            || experienceForNextLevel <= 0 || jobExperienceForNextJobLevel <= 0)
            return DecodedObservations.Empty;

        return DecodedObservations.Empty with
        {
            Progression = new PlayerProgression(
                level, experience, experienceForNextLevel,
                jobLevel, jobExperience, jobExperienceForNextJobLevel)
        };
    }

    /// <summary>
    /// <c>eq id … dottedGroup …</c> — what the character is wearing, as one
    /// line. The dotted group lists one vnum per equipment-panel position, with
    /// <c>-1</c> for a position that is not worn.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The position index in the dotted group is the slot number, and a
    /// <c>-1</c> position is not an empty slot in the contract — it is a slot
    /// that is not worn, so it produces no <see cref="WornEquipmentSlot"/> at
    /// all. The id is the session's own entity id, read from the packet rather
    /// than remembered from <c>cond</c>, so this opcode is self-contained.
    /// </para>
    /// <para>
    /// This opcode's slot numbering is the equipment-panel position, which is
    /// not the same space as <c>equip</c>'s item slot ids; the two are kept
    /// apart by <see cref="WornEquipment.Opcode"/>.
    /// </para>
    /// </remarks>
    private static DecodedObservations DecodeEq(string[] fields, DataSourceKind source, DateTime capturedUtc)
    {
        // eq id ? ? ? ? ? group ...
        if (fields.Length < 8 || !TryLong(fields[1], out long entityId) || entityId <= 0)
            return DecodedObservations.Empty;

        string group = fields[7];
        if (group.Length == 0 || group.IndexOf('.') < 0)
            return DecodedObservations.Empty;

        string[] positions = group.Split('.');
        var slots = ImmutableArray.CreateBuilder<WornEquipmentSlot>();
        for (int index = 0; index < positions.Length; index++)
        {
            if (!TryInt(positions[index], out int vnum) || vnum < -1)
                return DecodedObservations.Empty;
            if (vnum == -1)
                continue; // not worn, not a slot
            slots.Add(new WornEquipmentSlot(index, vnum));
        }

        return DecodedObservations.Empty with
        {
            Equipment = new WornEquipment(entityId, slots.ToImmutable(), EquipmentWireOpcode.Eq)
        };
    }

    /// <summary>
    /// <c>equip ? ? group…</c> — the equipment inventory, per item slot with
    /// detail. Each group is <c>slot.vnum.…</c>; the first two numbers are read
    /// and the rest ignored.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The packet carries no entity id, so the id the decoder already tracks
    /// from <c>cond</c> is used; before that is known the packet is refused
    /// rather than attributed to nobody — the same refusal shape
    /// <see cref="DecodeCast"/> applies to a selection without an own id.
    /// </para>
    /// <para>
    /// A partially-read set is the failure that matters here: a planner acting
    /// on a weapon that is not there is worse than acting on nothing, so any
    /// group that does not parse refuses the packet whole.
    /// </para>
    /// </remarks>
    private DecodedObservations DecodeEquip(string[] fields, DataSourceKind source, DateTime capturedUtc)
    {
        if (_playerEntityId is not { } entityId)
            return DecodedObservations.Empty;

        var slots = ImmutableArray.CreateBuilder<WornEquipmentSlot>();
        for (int i = 3; i < fields.Length; i++)
        {
            string[] parts = fields[i].Split('.');
            if (parts.Length < 2
                || !TryInt(parts[0], out int slot)
                || !TryInt(parts[1], out int vnum))
                return DecodedObservations.Empty;
            if (slot < 0 || vnum < 0)
                return DecodedObservations.Empty;
            if (vnum == 0)
                continue; // an empty slot, not an occupied one
            slots.Add(new WornEquipmentSlot(slot, vnum));
        }

        return DecodedObservations.Empty with
        {
            Equipment = new WornEquipment(entityId, slots.ToImmutable(), EquipmentWireOpcode.Equip)
        };
    }

    // ------------------------------------------------------------------ C1-3

    /// <summary>
    /// <c>sr slot</c> — a skill slot came off cooldown. Observed as <c>sr 0</c>,
    /// <c>sr 2</c>, <c>sr 6</c>; probable.
    /// </summary>
    /// <remarks>
    /// A slot is a small non-negative index. Negative is not a slot, and a value
    /// past <see cref="MaxPlausibleSkillSlot"/> is a field that is not a slot at
    /// all rather than a very large skill bar. Nothing beyond field 1 was ever
    /// observed, and nothing beyond it is read.
    /// </remarks>
    private static DecodedObservations DecodeSkillReady(string[] fields, DataSourceKind source, DateTime capturedUtc)
    {
        if (fields.Length < 2 || !TryInt(fields[1], out int slot))
            return DecodedObservations.Empty;
        if (slot < 0 || slot > MaxPlausibleSkillSlot)
            return DecodedObservations.Empty;

        return new DecodedObservations(
            ImmutableArray<EntitySighting>.Empty,
            ImmutableArray<GameEvent>.Empty,
            SkillReady: new SkillReady(slot, capturedUtc, source));
    }

    /// <summary>
    /// <c>ivn kind slot.vnum.amount.rarity</c> — what one inventory slot holds.
    /// Observed as <c>ivn 2 34.2006.1.0</c>, with the vnum matching the
    /// <c>drop</c> that preceded it; probable.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first field is an inventory-kind selector, and the group shape depends
    /// on it. The bag shape (<c>ivn 2 …</c>) is four parts,
    /// <c>slot.vnum.amount.rarity</c>; the equipment shape (<c>ivn 0 …</c>)
    /// observed in <c>data/equip_test.noscap</c> is seven parts,
    /// <c>slot.vnum</c> plus five unread trailing fields, where an emptied slot
    /// carries vnum 0.
    /// </para>
    /// <para>
    /// The four-part shape is read only as the capture showed it: fewer parts is
    /// a truncated packet; a different count is a shape nobody has observed, in
    /// which the third part may not be an amount at all. An empty slot (a vnum
    /// of −1, or an amount of 0) was never observed in the bag shape and is not
    /// read: the cost is that an emptied slot produces no reading, which a
    /// consumer sees as the absence of a newer one rather than as an invented
    /// empty.
    /// </para>
    /// <para>
    /// The equipment shape carries no stack count or rarity: an equipment slot
    /// holds a single item instance, so <see cref="InventorySlotReading.Amount"/>
    /// is 1 and <see cref="InventorySlotReading.Rarity"/> is 0, neither of which
    /// is stated by the wire on this shape.
    /// </para>
    /// </remarks>
    private static DecodedObservations DecodeInventorySlot(string[] fields, DataSourceKind source, DateTime capturedUtc)
    {
        if (fields.Length < 3 || !TryInt(fields[1], out int inventoryKind) || inventoryKind < 0)
            return DecodedObservations.Empty;

        string[] parts = fields[2].Split('.');

        if (parts.Length == 4)
        {
            // Bag shape: slot.vnum.amount.rarity.
            if (!TryInt(parts[0], out int slot)
                || !TryInt(parts[1], out int vnum)
                || !TryInt(parts[2], out int amount)
                || !TryInt(parts[3], out int rarity))
                return DecodedObservations.Empty;
            if (slot < 0 || vnum <= 0 || amount <= 0)
                return DecodedObservations.Empty;

            return new DecodedObservations(
                ImmutableArray<EntitySighting>.Empty,
                ImmutableArray<GameEvent>.Empty,
                InventorySlot: new InventorySlotReading(inventoryKind, slot, vnum, amount, rarity, capturedUtc, source));
        }

        if (parts.Length == 7)
        {
            // Equipment shape: slot.vnum.<five unread trailing fields>.
            if (!TryInt(parts[0], out int slot)
                || !TryInt(parts[1], out int vnum))
                return DecodedObservations.Empty;
            if (slot < 0 || vnum <= 0)
                return DecodedObservations.Empty;

            return new DecodedObservations(
                ImmutableArray<EntitySighting>.Empty,
                ImmutableArray<GameEvent>.Empty,
                InventorySlot: new InventorySlotReading(inventoryKind, slot, vnum, 1, 0, capturedUtc, source));
        }

        return DecodedObservations.Empty;
    }

    /// <summary>
    /// <c>get takerType takerId dropId ?</c> — a ground item was picked up.
    /// Observed as <c>get 1 3443217 1092257 0</c>, the drop id matching a
    /// preceding <c>drop</c>; probable. Field 4 is not named and is not read.
    /// </summary>
    /// <remarks>
    /// Whether the taker was this character is answered only once <c>cond</c>
    /// has named the own id, by the same rule as the aggressor: taker type 1 says
    /// a player picked it up, not which one, so before the id is known the
    /// answer is null — and null is carried, not resolved to false.
    /// </remarks>
    private DecodedObservations DecodePickup(string[] fields, DataSourceKind source, DateTime capturedUtc)
    {
        if (fields.Length < 4
            || !TryInt(fields[1], out int takerType)
            || !TryLong(fields[2], out long takerId)
            || !TryLong(fields[3], out long dropId))
            return DecodedObservations.Empty;
        if (takerType < 0 || takerId <= 0 || dropId <= 0)
            return DecodedObservations.Empty;

        bool? byPlayer = _playerEntityId is { } own
            ? takerType == PlayerEntityType && takerId == own
            : null;

        return new DecodedObservations(
            ImmutableArray<EntitySighting>.Empty,
            ImmutableArray<GameEvent>.Empty,
            Pickup: new ItemPickup(takerType, takerId, dropId, byPlayer, capturedUtc, source));
    }

    /// <summary>
    /// <c>drop vnum dropId x y amount ? ownerId</c> — an item on the ground.
    /// Observed as <c>drop 2006 1092257 110 63 1 0 3443217</c>; probable. Field
    /// 6 is unknown and is not read.
    /// </summary>
    /// <remarks>
    /// The coordinates get the same bound the memory reader applies to the
    /// character's own position: a value past
    /// <see cref="MaxPlausibleCoordinate"/> is not a distant square, it is a
    /// field that is not a coordinate. An amount below one is not an item on the
    /// ground.
    /// </remarks>
    private static DecodedObservations DecodeGroundItem(string[] fields, DataSourceKind source, DateTime capturedUtc)
    {
        if (fields.Length < 8
            || !TryInt(fields[1], out int vnum)
            || !TryLong(fields[2], out long dropId)
            || !TryInt(fields[3], out int x)
            || !TryInt(fields[4], out int y)
            || !TryInt(fields[5], out int amount)
            || !TryLong(fields[7], out long ownerId))
            return DecodedObservations.Empty;
        if (vnum <= 0 || dropId <= 0 || amount <= 0 || ownerId < 0)
            return DecodedObservations.Empty;
        if (!IsPlausibleCoordinate(x) || !IsPlausibleCoordinate(y))
            return DecodedObservations.Empty;

        return new DecodedObservations(
            ImmutableArray<EntitySighting>.Empty,
            ImmutableArray<GameEvent>.Empty,
            GroundItem: new GroundItem(vnum, dropId, x, y, amount, ownerId, capturedUtc, source));
    }

    /// <summary>
    /// <c>ct srcType srcId tgtType tgtId ? ? ?</c> — an entity acts on another.
    /// Observed 108 times as <c>ct 3 313816 1 3443217 -1 -1 0</c> and, with the
    /// character as source, as <c>ct 1 3443217 3 3205 -1 -1 220</c>; probable.
    /// The three trailing fields are not named by the catalogue and are not read.
    /// </summary>
    /// <remarks>
    /// Read as the answer to <i>which</i> entity the character is acting on
    /// (docs/TASTI_E_BERSAGLIO.md § 6.3), and only when the source is the own id
    /// from <c>cond</c>: a monster's <c>ct</c> aimed at the character is that
    /// monster's business. A cast aimed at the character itself — a self-buff in
    /// the captures — selects nothing and produces nothing. Whether a target is
    /// currently selected stays the screen's fact (ADR-0018); this one has no
    /// clearing counterpart on the wire and is sticky by nature.
    /// </remarks>
    private DecodedObservations DecodeCast(string[] fields, DataSourceKind source, DateTime capturedUtc)
    {
        if (fields.Length < 5
            || !TryInt(fields[1], out int sourceType)
            || !TryLong(fields[2], out long sourceId)
            || !TryInt(fields[3], out int targetType)
            || !TryLong(fields[4], out long targetId))
            return DecodedObservations.Empty;
        if (sourceType < 0 || sourceId <= 0 || targetType < 0 || targetId <= 0)
            return DecodedObservations.Empty;

        if (_playerEntityId is not { } own
            || sourceType != PlayerEntityType
            || sourceId != own
            || targetId == own)
            return DecodedObservations.Empty;

        return new DecodedObservations(
            ImmutableArray<EntitySighting>.Empty,
            ImmutableArray<GameEvent>.Empty,
            PlayerTarget: new PlayerTargetSelection(new TargetedEntity(targetId, targetType), capturedUtc, source));
    }

    /// <summary>
    /// The largest skill slot index a packet is taken to name.
    /// </summary>
    /// <remarks>
    /// Deliberately loose, as <see cref="MaxPlausibleCoordinate"/> is: it rejects
    /// a field that is not a slot at all, not a skill bar slightly larger than
    /// the ones observed (0, 2 and 6). A tight bound would refuse a real packet
    /// to catch a garbage one, and the garbage this guards against is orders of
    /// magnitude out.
    /// </remarks>
    public const int MaxPlausibleSkillSlot = 255;

    /// <summary>
    /// The largest coordinate a NosTale map is taken to have — the same bound
    /// <c>MemoryGameplayProvider</c> applies to the character's own position,
    /// restated here so the network layer does not depend on the memory one.
    /// </summary>
    public const int MaxPlausibleCoordinate = 1000;

    private static bool IsPlausibleCoordinate(int value) => value >= 0 && value <= MaxPlausibleCoordinate;

    private static DecodedObservations Sighting(
        long entityId, double x, double y, double? hpRatio, DataSourceKind source,
        DateTime positionAtUtc, DateTime? hpAtUtc, int? vnum, AbsoluteVitals? vitals = null)
        => new(
            ImmutableArray.Create(new EntitySighting(
                entityId, "Monster", x, y, hpRatio, source, positionAtUtc, hpAtUtc, vnum, vitals)),
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
    /// something else. Both are refused rather than read at positions nobody has
    /// established.
    /// </remarks>
    /// <remarks>
    /// <para>
    /// <b>Correzione del 2026-09-07.</b> Questo commento diceva «Type 2 was never
    /// observed at all». È falso, e lo dice una registrazione di questo
    /// repository: <c>data/equip_test.noscap</c> porta <b>430 pacchetti
    /// <c>mv</c> di tipo 2</b> contro 3039 di tipo 3, cioè il 12% dei suoi
    /// movimenti.
    /// </para>
    /// <para>
    /// <b>Il layout è stato stabilito lo stesso giorno, e il tipo 2 resta
    /// rifiutato per un motivo diverso.</b> Su <c>data/messaggi.noscap</c> — 2718
    /// <c>mv</c>, 14 <c>st</c> e 6 <c>in</c> di tipo 2, su 30 entità distinte — i
    /// campi stanno dove stanno per il tipo 3, e lo dicono tre misure:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>continuità del movimento</b>: 2688 passi di tipo 2 contro 17849 di tipo
    /// 3, stessa mediana (2,0 caselle), stesso novantesimo percentile (2,8), il
    /// 99,3% entro tre caselle contro il 99,9%. Campi letti nel posto sbagliato
    /// darebbero salti casuali, non una camminata;
    /// </description></item>
    /// <item><description>
    /// <b>il vnum risolve</b>: tutti e quattro i vnum visti in <c>in</c> di tipo 2
    /// (2362, 1494, 1488, 2557) esistono nella stessa tabella <c>monster</c> del
    /// catalogo e danno nomi sensati — «Caverna dei Conigli», «Pir»,
    /// «Baby^panda», «Graham»;
    /// </description></item>
    /// <item><description>
    /// <b><c>st</c> ha gli stessi dodici campi</b>, e i suoi valori soddisfano
    /// ogni invariante di plausibilità (<c>0 ≤ hp ≤ maxHp</c>, <c>maxHp &gt; 0</c>).
    /// </description></item>
    /// </list>
    /// <para>
    /// <b>Perché allora resta fuori.</b> Quei nomi non sono mostri: sono NPC, pet
    /// e portali. <see cref="Sighting"/> etichetta ogni avvistamento
    /// <c>"Monster"</c>, e quell'etichetta arriva al World Model attraverso
    /// <c>GameTrafficObserver</c>. Ammettere il tipo 2 così com'è significherebbe
    /// presentare al pianificatore un negoziante, o il pet del giocatore stesso,
    /// come un bersaglio. Il tipo 2 entrerà quando l'avvistamento porterà la
    /// specie osservata invece di una costante — che è un cambio di contratto, non
    /// una riga in questo metodo.
    /// </para>
    /// </remarks>
    internal static bool IsReadableEntity(string typeField) => typeField == "3";

    /// <summary>
    /// The entity type a player carries, confirmed in <c>su</c>, <c>cond</c> and
    /// <c>sayi</c>.
    /// </summary>
    /// <remarks>
    /// It does not distinguish the controlled character from another player
    /// fighting nearby; only the own id from <c>cond</c> does. Before that id is
    /// known the type alone still feeds <see cref="TargetStateComposer"/>'s
    /// contradiction, whose false positive is UNKNOWN — a fact the planner then
    /// skips, never a confident wrong answer — and feeds nothing that could name
    /// an aggressor.
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

    /// <summary>
    /// What this decoder remembers about an entity between packets, with the
    /// instant each half was last stated so a merged sighting can say how old
    /// its remembered half is.
    /// </summary>
    /// <param name="Vitals">
    /// The absolute pair behind <paramref name="HpRatio"/>, when the packet that
    /// stated the health stated it that way. Remembered and aged exactly like
    /// the ratio: a later <c>mv</c> that reuses the remembered health reuses
    /// this too, stamped with the same older <paramref name="HpAtUtc"/>, and
    /// keeps whatever source label the existing code already computes for that
    /// case. Null wherever the ratio came from a percentage.
    /// </param>
    private readonly record struct TrackedEntity(
        double X,
        double Y,
        double HpRatio,
        bool HasHp,
        bool HasPosition,
        DateTime PositionAtUtc,
        DateTime HpAtUtc,
        int? Vnum = null,
        AbsoluteVitals? Vitals = null);
}
