namespace NosAi.Core.Memory;

public enum MemoryType : byte { Working, Episodic, Semantic, Procedural, Reasoning }
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
