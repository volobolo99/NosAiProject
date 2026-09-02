// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Perception — External protocol map and configurable decoder
// ============================================================================
//
// NosTale's wire format is proprietary and is NOT in this repository. Hardcoding
// "plausible" opcodes would produce wrong HP and wrong positions that *look*
// like observations: the worst way to be wrong, because the planner would act on
// them.
//
// So the map is DATA, not code: it lives on the dedicated volume, the operator
// derives it from traffic actually observed (see TrafficRecorder), and every
// read is validated. A field outside the message, a value out of range, or an
// unmapped opcode produce NO observation.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Perception.Network;

/// <summary>How to read one numeric field out of a message.</summary>
public sealed record FieldSpec(int Offset, int Size, bool BigEndian = true, double Scale = 1.0, bool Signed = false)
{
    public void Validate(string owner, string field)
    {
        if (Offset < 0) throw new InvalidDataException($"{owner}.{field}: offset cannot be negative.");
        if (Size is not (1 or 2 or 4 or 8)) throw new InvalidDataException($"{owner}.{field}: size must be 1, 2, 4 or 8.");
        if (double.IsNaN(Scale) || double.IsInfinity(Scale)) throw new InvalidDataException($"{owner}.{field}: non-finite scale.");
    }

    /// <summary>
    /// Reads the field. Returns false when the message is too short: a field
    /// that falls outside the message is not a zero, it is not readable.
    /// </summary>
    public bool TryRead(ReadOnlySpan<byte> message, out double value)
    {
        value = 0;
        if (Offset + Size > message.Length) return false;
        ReadOnlySpan<byte> raw = message.Slice(Offset, Size);
        ulong unsigned = Size switch
        {
            1 => raw[0],
            2 => BigEndian ? BinaryPrimitives.ReadUInt16BigEndian(raw) : BinaryPrimitives.ReadUInt16LittleEndian(raw),
            4 => BigEndian ? BinaryPrimitives.ReadUInt32BigEndian(raw) : BinaryPrimitives.ReadUInt32LittleEndian(raw),
            _ => BigEndian ? BinaryPrimitives.ReadUInt64BigEndian(raw) : BinaryPrimitives.ReadUInt64LittleEndian(raw),
        };

        if (Signed)
        {
            long signed = Size switch
            {
                1 => (sbyte)unsigned,
                2 => (short)unsigned,
                4 => (int)unsigned,
                _ => (long)unsigned,
            };
            value = signed * Scale;
        }
        else
        {
            value = unsigned * Scale;
        }
        return true;
    }
}

/// <summary>One mapped message type.</summary>
public sealed record MessageSpec(
    long Opcode,
    GameEventKind Kind,
    FieldSpec? EntityId,
    FieldSpec? X,
    FieldSpec? Y,
    FieldSpec? HpRatio,
    string EntityKind = "Monster",
    string? Description = null)
{
    public void Validate()
    {
        string owner = $"opcode {Opcode}";
        EntityId?.Validate(owner, nameof(EntityId));
        X?.Validate(owner, nameof(X));
        Y?.Validate(owner, nameof(Y));
        HpRatio?.Validate(owner, nameof(HpRatio));
        if (Kind == GameEventKind.EntitySighting && (EntityId is null || X is null || Y is null))
            throw new InvalidDataException($"{owner}: a sighting needs entityId, x and y.");
    }
}

/// <summary>
/// A complete, operator-supplied description of the observed protocol.
/// </summary>
/// <remarks>
/// <see cref="Confidence"/> records how the map was obtained. A map derived by
/// correlating captured traffic with known ground truth is not the same as a
/// documented specification, and decisions taken on it inherit that: it drives
/// the provenance of every observation the decoder produces, so a guessed map
/// can never yield a LIVE-looking reading.
/// </remarks>
public sealed record ProtocolMap(
    string Name,
    FramingSpec Framing,
    FieldSpec OpcodeField,
    ImmutableArray<MessageSpec> Messages,
    DataSourceKind Confidence = DataSourceKind.Derived,
    PlayerVitalsSpec? PlayerVitals = null)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidDataException("The protocol map needs a name.");
        Framing.Validate();
        OpcodeField.Validate(Name, nameof(OpcodeField));
        if (Messages.IsDefaultOrEmpty) throw new InvalidDataException($"Protocol map '{Name}' maps no message.");
        var seen = new HashSet<long>();
        foreach (MessageSpec message in Messages)
        {
            if (!seen.Add(message.Opcode))
                throw new InvalidDataException($"Protocol map '{Name}': opcode {message.Opcode} is mapped twice.");
            message.Validate();
        }
        if (PlayerVitals is { } vitals)
        {
            vitals.Validate(Name);
            if (seen.Contains(vitals.Opcode))
                throw new InvalidDataException(
                    $"Protocol map '{Name}': opcode {vitals.Opcode} is mapped both as a message and as the player vitals.");
        }
        if (Confidence is DataSourceKind.Live)
            throw new InvalidDataException(
                "A protocol map is never LIVE: it describes how to read observations, it is not one.");
    }
}

/// <summary>
/// Where the controlled character's own vitals are, in absolute units.
/// </summary>
/// <remarks>
/// <para>
/// Optional, and absent from every map until an operator finds the message that
/// carries these numbers and correlates the offsets against values read off the
/// client's own screen. Until then Gate 3 keeps refusing to plan, which is the
/// correct outcome and the only honest one: HP as a ratio is not HP.
/// </para>
/// <para>
/// <see cref="HasTarget"/> and <see cref="InCombat"/> are flags read as non-zero.
/// They are separately optional because a map may well pin the vitals long before
/// anyone works out where the combat state lives, and a partial map should give
/// what it has rather than nothing.
/// </para>
/// </remarks>
public sealed record PlayerVitalsSpec(
    long Opcode,
    FieldSpec Hp,
    FieldSpec MaxHp,
    FieldSpec Mp,
    FieldSpec? HasTarget = null,
    FieldSpec? InCombat = null)
{
    public void Validate(string mapName)
    {
        string owner = $"{mapName}.playerVitals";
        Hp.Validate(owner, nameof(Hp));
        MaxHp.Validate(owner, nameof(MaxHp));
        Mp.Validate(owner, nameof(Mp));
        HasTarget?.Validate(owner, nameof(HasTarget));
        InCombat?.Validate(owner, nameof(InCombat));
    }
}

/// <summary>
/// Decodes reassembled messages using an operator-supplied <see cref="ProtocolMap"/>.
/// </summary>
/// <remarks>
/// Everything this decoder knows comes from the map. An unmapped opcode, an
/// unreadable field or an out-of-range HP ratio produces no observation and is
/// counted, so a wrong map shows up as "nothing decodes" rather than as a stream
/// of confident nonsense.
/// </remarks>
public sealed class ConfigurableProtocolDecoder : IGamePacketDecoder
{
    private readonly ProtocolMap _map;
    private readonly Dictionary<long, MessageSpec> _byOpcode;
    private long _unmappedOpcodes;
    private long _malformedMessages;

    public string ProtocolName => _map.Name;
    public ProtocolMap Map => _map;

    /// <inheritdoc />
    /// <remarks>
    /// Only when the operator has found the message that carries them. Until then
    /// this decoder can never produce vitals, and saying so is what lets a consumer
    /// report <c>player_vitals_not_mapped</c> rather than blaming a quiet batch.
    /// </remarks>
    public bool ReadsPlayerVitals => _map.PlayerVitals is not null;

    /// <summary>Messages whose opcode the map does not describe.</summary>
    public long UnmappedOpcodeCount => _unmappedOpcodes;

    /// <summary>Messages whose mapped fields could not be read or failed validation.</summary>
    public long MalformedMessageCount => _malformedMessages;

    public ConfigurableProtocolDecoder(ProtocolMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        map.Validate();
        _map = map;
        _byOpcode = map.Messages.ToDictionary(m => m.Opcode);
    }

    public bool CanDecode(ObservedPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return packet.Payload.Length >= _map.Framing.HeaderSize;
    }

    public DecodedObservations Decode(ObservedPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return DecodeMessage(packet.Payload.Span, packet.Source, packet.CapturedUtc);
    }

    /// <param name="capturedUtc">
    /// When the message crossed the wire. Carried through to the vitals so a
    /// replayed recording is not mistaken for a reading taken now (ADR-0016).
    /// </param>
    public DecodedObservations DecodeMessage(
        ReadOnlySpan<byte> message, DataSourceKind packetSource, DateTime? capturedUtc = null)
    {
        if (!_map.OpcodeField.TryRead(message, out double rawOpcode))
        {
            _malformedMessages++;
            return DecodedObservations.Empty;
        }

        if (_map.PlayerVitals is { } vitalsSpec && (long)rawOpcode == vitalsSpec.Opcode)
            return DecodeVitals(message, vitalsSpec, packetSource, capturedUtc);

        if (!_byOpcode.TryGetValue((long)rawOpcode, out MessageSpec? spec))
        {
            // Not knowing an opcode is normal and honest; guessing it is not.
            _unmappedOpcodes++;
            return DecodedObservations.Empty;
        }

        // An observation is never more trusted than the map used to read it.
        DataSourceKind source = Weaker(packetSource, _map.Confidence);

        long entityId = 0;
        if (spec.EntityId is { } idField)
        {
            if (!idField.TryRead(message, out double id)) { _malformedMessages++; return DecodedObservations.Empty; }
            entityId = (long)id;
        }

        double x = 0, y = 0;
        if (spec.X is { } xField && !xField.TryRead(message, out x)) { _malformedMessages++; return DecodedObservations.Empty; }
        if (spec.Y is { } yField && !yField.TryRead(message, out y)) { _malformedMessages++; return DecodedObservations.Empty; }

        double hpRatio = 1.0;
        bool hpKnown = false;
        if (spec.HpRatio is { } hpField)
        {
            if (!hpField.TryRead(message, out hpRatio)) { _malformedMessages++; return DecodedObservations.Empty; }
            // A ratio outside 0..1 means the field or the scale is wrong. Clamping
            // would hide a broken map behind plausible numbers.
            if (hpRatio < -0.001 || hpRatio > 1.001) { _malformedMessages++; return DecodedObservations.Empty; }
            hpRatio = Math.Clamp(hpRatio, 0.0, 1.0);
            hpKnown = true;
        }

        // A map that does not name the health field yields a sighting without
        // health, not one at full health: 1.0 was the placeholder this local
        // needed to exist, never a reading.
        // A mapped message states position and health together, so both halves
        // carry the packet's own instant — when the caller knew it. Null stays
        // null: a consumer that needs an age must not be handed the poll's clock.
        var sightings = ImmutableArray<EntitySighting>.Empty;
        if (spec.Kind is GameEventKind.EntitySighting or GameEventKind.CombatHit)
            sightings = ImmutableArray.Create(
                new EntitySighting(
                    entityId, spec.EntityKind, x, y, hpKnown ? hpRatio : null, source,
                    capturedUtc, hpKnown ? capturedUtc : null));

        var events = ImmutableArray<GameEvent>.Empty;
        if (spec.Kind is not GameEventKind.EntitySighting)
        {
            string descriptor = spec.Description ?? (hpKnown ? $"hp={hpRatio:0.00}" : spec.Kind.ToString());
            events = ImmutableArray.Create(new GameEvent(spec.Kind, entityId, descriptor, source, capturedUtc));
        }

        return new DecodedObservations(sightings, events);
    }

    /// <summary>
    /// Reads the controlled character's vitals out of the message the map names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every check below exists because the failure it catches produces a number
    /// rather than an error. A misplaced offset yields bytes that read perfectly
    /// well as an integer, and the only thing separating a real HP from a stray
    /// one is whether it makes sense as HP.
    /// </para>
    /// <para>
    /// So: max HP of zero or less is not a character, it is the wrong offset —
    /// and it would also make every ratio computed downstream a division by zero.
    /// HP above max HP is the same evidence. Negative HP is not a corpse, it is a
    /// field read as signed that is not. None of these are clamped: clamping is
    /// how a wrong map goes on producing plausible readings forever.
    /// </para>
    /// </remarks>
    private DecodedObservations DecodeVitals(
        ReadOnlySpan<byte> message, PlayerVitalsSpec spec, DataSourceKind packetSource, DateTime? capturedUtc = null)
    {
        if (!spec.Hp.TryRead(message, out double hp)
            || !spec.MaxHp.TryRead(message, out double maxHp)
            || !spec.Mp.TryRead(message, out double mp))
        {
            _malformedMessages++;
            return DecodedObservations.Empty;
        }

        if (maxHp <= 0 || hp < 0 || hp > maxHp + 0.5 || mp < 0)
        {
            _malformedMessages++;
            return DecodedObservations.Empty;
        }

        bool? hasTarget = null;
        if (spec.HasTarget is { } targetField)
        {
            if (!targetField.TryRead(message, out double flag)) { _malformedMessages++; return DecodedObservations.Empty; }
            hasTarget = Math.Abs(flag) > 0.5;
        }

        bool? inCombat = null;
        if (spec.InCombat is { } combatField)
        {
            if (!combatField.TryRead(message, out double flag)) { _malformedMessages++; return DecodedObservations.Empty; }
            inCombat = Math.Abs(flag) > 0.5;
        }

        return new DecodedObservations(
            ImmutableArray<EntitySighting>.Empty,
            ImmutableArray<GameEvent>.Empty,
            new PlayerVitals(
                (int)hp, (int)maxHp, (int)mp, hasTarget, inCombat,
                Weaker(packetSource, _map.Confidence), capturedUtc));
    }

    private static DataSourceKind Weaker(DataSourceKind a, DataSourceKind b)
    {
        static int Rank(DataSourceKind kind) => kind switch
        {
            DataSourceKind.Live => 4,
            DataSourceKind.Derived => 3,
            DataSourceKind.Cached => 2,
            DataSourceKind.Simulated => 1,
            _ => 0,
        };
        return Rank(a) <= Rank(b) ? a : b;
    }
}

// --- wire shapes for the map file -------------------------------------------

internal sealed class FieldFile
{
    [JsonPropertyName("offset")] public int Offset { get; set; }
    [JsonPropertyName("size")] public int Size { get; set; }
    [JsonPropertyName("bigEndian")] public bool BigEndian { get; set; } = true;
    [JsonPropertyName("scale")] public double Scale { get; set; } = 1.0;
    [JsonPropertyName("signed")] public bool Signed { get; set; }

    public FieldSpec ToSpec() => new(Offset, Size, BigEndian, Scale, Signed);
}

internal sealed class MessageFile
{
    [JsonPropertyName("opcode")] public long Opcode { get; set; }
    [JsonPropertyName("kind")] public string? Kind { get; set; }
    [JsonPropertyName("entityKind")] public string? EntityKind { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("entityId")] public FieldFile? EntityId { get; set; }
    [JsonPropertyName("x")] public FieldFile? X { get; set; }
    [JsonPropertyName("y")] public FieldFile? Y { get; set; }
    [JsonPropertyName("hpRatio")] public FieldFile? HpRatio { get; set; }
}

internal sealed class FramingFile
{
    [JsonPropertyName("lengthOffset")] public int LengthOffset { get; set; }
    [JsonPropertyName("lengthSize")] public int LengthSize { get; set; } = 2;
    [JsonPropertyName("bigEndian")] public bool BigEndian { get; set; } = true;
    [JsonPropertyName("headerSize")] public int HeaderSize { get; set; } = 2;
    [JsonPropertyName("lengthIncludesHeader")] public bool LengthIncludesHeader { get; set; }
    [JsonPropertyName("maxMessageLength")] public int MaxMessageLength { get; set; } = 64 * 1024;
}

internal sealed class ProtocolMapFile
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("confidence")] public string? Confidence { get; set; }
    [JsonPropertyName("framing")] public FramingFile? Framing { get; set; }
    [JsonPropertyName("opcode")] public FieldFile? Opcode { get; set; }
    [JsonPropertyName("messages")] public List<MessageFile>? Messages { get; set; }
}

/// <summary>Loads a protocol map from the dedicated volume.</summary>
public static class ProtocolMapLoader
{
    /// <summary>Default location under the runtime data root.</summary>
    public const string DefaultRelativePath = "config/protocol_map.json";

    public static ProtocolMap Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        ProtocolMapFile? file;
        try
        {
            file = JsonSerializer.Deserialize<ProtocolMapFile>(json,
                new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The protocol map is not valid JSON: {ex.Message}", ex);
        }

        if (file is null) throw new InvalidDataException("The protocol map is empty.");
        if (file.Framing is null) throw new InvalidDataException("The protocol map has no framing section.");
        if (file.Opcode is null) throw new InvalidDataException("The protocol map has no opcode field.");

        var messages = ImmutableArray.CreateBuilder<MessageSpec>();
        foreach (MessageFile message in file.Messages ?? new List<MessageFile>())
        {
            if (!Enum.TryParse(message.Kind, ignoreCase: true, out GameEventKind kind))
                throw new InvalidDataException($"Opcode {message.Opcode}: unknown kind '{message.Kind}'.");
            messages.Add(new MessageSpec(message.Opcode, kind,
                message.EntityId?.ToSpec(), message.X?.ToSpec(), message.Y?.ToSpec(), message.HpRatio?.ToSpec(),
                message.EntityKind ?? "Monster", message.Description));
        }

        // A map with no stated confidence is a derivation, not a specification.
        DataSourceKind confidence = file.Confidence is null
            ? DataSourceKind.Derived
            : Enum.TryParse(file.Confidence, ignoreCase: true, out DataSourceKind parsed)
                ? parsed
                : throw new InvalidDataException($"Unknown confidence '{file.Confidence}'.");

        var framing = new FramingSpec(file.Framing.LengthOffset, file.Framing.LengthSize, file.Framing.BigEndian,
            file.Framing.HeaderSize, file.Framing.LengthIncludesHeader, file.Framing.MaxMessageLength);

        var map = new ProtocolMap(file.Name ?? "unnamed", framing, file.Opcode.ToSpec(), messages.ToImmutable(), confidence);
        map.Validate();
        return map;
    }

    /// <summary>
    /// Loads from disk, reporting by name when the map is absent. A missing map
    /// is the normal state until the operator derives one: the channel then
    /// observes packets and decodes nothing, which is honest.
    /// </summary>
    public static bool TryLoadFile(string path, out ProtocolMap? map, out string? failure)
    {
        map = null;
        failure = null;
        try
        {
            if (!File.Exists(path))
            {
                failure = $"protocol_map_not_found:{path}";
                return false;
            }
            map = Parse(File.ReadAllText(path));
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            failure = $"protocol_map_unreadable:{ex.Message}";
            return false;
        }
    }
}
