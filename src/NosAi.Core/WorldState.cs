using System.Runtime.InteropServices;

namespace NosAi.Core;

/// <summary>Immutable, client-observable snapshot consumed by planning stages.</summary>
public sealed record WorldState(
    long Version,
    long UnixMillis,
    ReadOnlyMemory<EntitySnapshot> Entities,
    SelfSnapshot Self,
    MapSnapshot Map);

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct EntitySnapshot
{
    public readonly uint EntityId;
    public readonly float X;
    public readonly float Y;
    public readonly float Vx;
    public readonly float Vy;
    public readonly float Confidence;
    public readonly byte Phase;

    public EntitySnapshot(uint entityId, float x, float y, float vx, float vy, float confidence, byte phase)
    {
        EntityId = entityId;
        X = x;
        Y = y;
        Vx = vx;
        Vy = vy;
        Confidence = confidence;
        Phase = phase;
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct SelfSnapshot(float X, float Y, float HpRatio, bool Alive);

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct MapSnapshot(uint MapId, ushort Width, ushort Height);

public interface IWorldStateBuilder
{
    WorldState Build(ReadOnlySpan<EntitySnapshot> fused, in SelfSnapshot self, in MapSnapshot map, long version);
}
