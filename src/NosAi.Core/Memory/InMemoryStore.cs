namespace NosAi.Core.Memory;

public sealed class InMemoryStore : IMemoryStore
{
    private readonly Dictionary<string, MemoryRecord> _records = new(StringComparer.Ordinal);

    public bool Append(in MemoryRecord record)
    {
        if (record.MemoryId == Guid.Empty || string.IsNullOrWhiteSpace(record.Key)) return false;
        if (record.Provenance == MemoryProvenance.Unknown && record.Type != MemoryType.Reasoning) return false;
        _records[record.Key] = record;
        return true;
    }

    public bool TryGet(string key, out MemoryRecord record) => _records.TryGetValue(key, out record);

    public int Query(string key, Span<MemoryRecord> destination)
    {
        if (destination.IsEmpty) return 0;
        var count = 0;
        foreach (var item in _records.Values)
        {
            if (item.Invalidated || !item.Key.Contains(key, StringComparison.OrdinalIgnoreCase)) continue;
            destination[count++] = item;
            if (count == destination.Length) break;
        }
        return count;
    }
}
