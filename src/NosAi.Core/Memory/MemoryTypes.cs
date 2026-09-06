namespace NosAi.Core.Memory;

/// <summary>
/// docs/ROADMAP_ESECUTIVA.md S:AP-09 names ten memory categories ("Working,
/// episodic, semantic, procedural, spatial, combat, quest, character,
/// failure e reasoning memory"); this enum originally carried five.
/// <see cref="Spatial"/>/<see cref="Combat"/>/<see cref="Quest"/>/
/// <see cref="Character"/>/<see cref="Failure"/> are appended after the
/// original members rather than inserted in the DoD's own prose order, so
/// every already-declared member keeps its original numeric value --
/// this is a <c>byte</c>-backed enum and nothing in this repository is
/// known to persist it today, but preserving existing values costs
/// nothing and avoids a needless renumbering.
/// </summary>
public enum MemoryType : byte
{
    Working,
    Episodic,
    Semantic,
    Procedural,
    Reasoning,
    Spatial,
    Combat,
    Quest,
    Character,
    Failure
}
public enum MemoryProvenance : byte { Network, Memory, Screen, Local, Operator, Unknown }

public readonly record struct MemoryRecord(
    Guid MemoryId,
    MemoryType Type,
    MemoryProvenance Provenance,
    float Confidence,
    long ObservedAtUnixMillis,
    long RecordedAtUnixMillis,
    long SessionId,
    string Key,
    string Value,
    bool Invalidated);

public interface IMemoryStore
{
    bool Append(in MemoryRecord record);
    bool TryGet(string key, out MemoryRecord record);
    int Query(string key, Span<MemoryRecord> destination);
}
