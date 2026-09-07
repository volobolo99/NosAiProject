using System.Runtime.InteropServices;

namespace NosAi.Core;

/// <summary>Immutable, client-observable snapshot consumed by planning stages.</summary>
/// <remarks>
/// <b>Not <c>NosAi.Runtime.WorldModel.WorldState</c></b>, which is the one the
/// runtime actually keeps: Gate 1 updates it, <c>GameTrafficObserver</c> emits it,
/// and it holds <c>EntityState</c> objects with a nullable health ratio. This one
/// is the packed, allocation-conscious shape the pure planning layer takes -- a
/// <c>ReadOnlyMemory</c> of blittable structs a planner can walk without touching
/// the heap -- and its only consumers are <c>IOrchestrator</c> and
/// <c>IPlanner</c> in <c>NosAi.Core.Planning</c>.
/// <para>
/// Both were called <c>WorldState</c> until 2026-09-07, and this one sat in the
/// <b>root</b> namespace of <c>NosAi.Core</c>, which every namespace inside that
/// assembly can see without a <c>using</c>. See <c>PIANO_DI_RIORDINO.md § R1</c>.
/// </para>
/// </remarks>
public sealed record PlannerWorldState(
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
    PlannerWorldState Build(ReadOnlySpan<EntitySnapshot> fused, in SelfSnapshot self, in MapSnapshot map, long version);
}
