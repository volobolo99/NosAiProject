using System.Text.Json;

namespace NosAi.Core.Memory;

public sealed class FileSystemAdaptiveKnowledgeStore : IAdaptiveKnowledgeStore
{
    private readonly string _root;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public FileSystemAdaptiveKnowledgeStore(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    public async ValueTask<KnowledgeEntry?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var file = Directory.EnumerateFiles(_root, $"{Sanitize(id)}.json", SearchOption.AllDirectories).FirstOrDefault();
        if (file is null) return null;

        await using var stream = File.OpenRead(file);
        return await JsonSerializer.DeserializeAsync<KnowledgeEntry>(stream, JsonOptions, cancellationToken);
    }

    public async ValueTask<IReadOnlyList<KnowledgeEntry>> QueryAsync(KnowledgeScope scope, string topic, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        var normalizedTopic = topic.Trim();
        var files = Directory.EnumerateFiles(_root, "*.json", SearchOption.AllDirectories);
        var result = new List<KnowledgeEntry>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = File.OpenRead(file);
            var entry = await JsonSerializer.DeserializeAsync<KnowledgeEntry>(stream, JsonOptions, cancellationToken);
            if (entry is not null && entry.Scope == scope && entry.Topic.Contains(normalizedTopic, StringComparison.OrdinalIgnoreCase))
                result.Add(entry);
        }
        return result;
    }

    public async ValueTask SaveAsync(KnowledgeEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var path = KnowledgePathPolicy.Build(_root, entry);
        var fullPath = Path.GetFullPath(Path.Combine(_root, path.RelativeFilePath));
        if (!fullPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Knowledge path escaped the configured root.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            var temp = fullPath + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(entry, JsonOptions), cancellationToken);
            File.Move(temp, fullPath, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<KnowledgePath> EnsurePathAsync(KnowledgeDiscovery discovery, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        var entry = new KnowledgeEntry(
            Guid.NewGuid().ToString("N"), discovery.Topic, KnowledgeScope.Context,
            KnowledgeStatus.Discovered, discovery.Provenance, "unknown", discovery.Reason,
            0, 0, discovery.ObservedAtUtc, discovery.ObservedAtUtc);
        var path = KnowledgePathPolicy.Build(_root, entry);
        var directory = Path.Combine(_root, path.RelativeDirectory);
        await _gate.WaitAsync(cancellationToken);
        try { Directory.CreateDirectory(directory); }
        finally { _gate.Release(); }
        return path;
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim().Select(c => invalid.Contains(c) || c is '/' or '\\' ? '_' : c).ToArray();
        return string.IsNullOrWhiteSpace(new string(chars)) ? "unknown" : new string(chars);
    }
}
