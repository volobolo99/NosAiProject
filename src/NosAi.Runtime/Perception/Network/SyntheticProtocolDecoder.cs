// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Perception — SYNTHETIC protocol decoder (tests only, not NosTale)
// ============================================================================
//
// This decoder reads a format invented FOR THIS MODULE'S TESTS. It is NOT the
// NosTale protocol and does not claim to be: the opcodes here are ours, and the
// real pipeline will use a decoder supplied with the game's actual opcode map.
//
// Synthetic frame:  [opcode:1][entityId:4 BE][x:2 BE][y:2 BE][hp:1 (0..100)]
//   opcode 0x01 = EntitySighting, 0x02 = CombatHit, 0x03 = EntityDeath.

using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Perception.Network;

/// <summary>Deterministic decoder for the module's own synthetic test protocol.</summary>
public sealed class SyntheticProtocolDecoder : IGamePacketDecoder
{
    public const byte OpSighting = 0x01;
    public const byte OpCombatHit = 0x02;
    public const byte OpEntityDeath = 0x03;

    private const int FrameLength = 1 + 4 + 2 + 2 + 1;

    public string ProtocolName => "nosai-synthetic-v1";

    /// <inheritdoc />
    /// <remarks>The synthetic protocol has no vitals message; it never invented one.</remarks>
    public bool ReadsPlayerVitals => false;

    public bool CanDecode(ObservedPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        return packet.Payload.Length == FrameLength;
    }

    public DecodedObservations Decode(ObservedPacket packet)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ReadOnlySpan<byte> data = packet.Payload.Span;
        if (data.Length != FrameLength) return DecodedObservations.Empty;

        byte opcode = data[0];
        long entityId = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(1, 4));
        int x = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(5, 2));
        int y = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(7, 2));
        byte hpByte = data[9];
        double hpRatio = Math.Clamp(hpByte / 100.0, 0.0, 1.0);

        // The observation inherits the packet's provenance: a sighting decoded
        // from a SIMULATED packet is SIMULATED, never silently promoted to LIVE.
        DataSourceKind source = packet.Source;

        switch (opcode)
        {
            case OpSighting:
                return new DecodedObservations(
                    ImmutableArray.Create(new EntitySighting(entityId, "Monster", x, y, hpRatio, source)),
                    ImmutableArray<GameEvent>.Empty);

            case OpCombatHit:
                return new DecodedObservations(
                    ImmutableArray.Create(new EntitySighting(entityId, "Monster", x, y, hpRatio, source)),
                    ImmutableArray.Create(new GameEvent(GameEventKind.CombatHit, entityId,
                        $"hp={hpRatio:0.00}", source)));

            case OpEntityDeath:
                return new DecodedObservations(
                    ImmutableArray<EntitySighting>.Empty,
                    ImmutableArray.Create(new GameEvent(GameEventKind.EntityDeath, entityId, "dead", source)));

            default:
                // An unknown opcode is not a guessable entity: report nothing.
                return DecodedObservations.Empty;
        }
    }

    /// <summary>Builds a synthetic frame; used by the certification suite.</summary>
    public static byte[] BuildFrame(byte opcode, long entityId, int x, int y, int hpPercent)
    {
        byte[] frame = new byte[FrameLength];
        frame[0] = opcode;
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(1, 4), (uint)entityId);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(5, 2), (ushort)x);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(7, 2), (ushort)y);
        frame[9] = (byte)Math.Clamp(hpPercent, 0, 100);
        return frame;
    }
}
