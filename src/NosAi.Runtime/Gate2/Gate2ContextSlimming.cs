// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Versione: 1.0 Beta
// Gate 2 — Riduzione del contesto per VRAM: firma errori e vista world compatta
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NosAi.Runtime.Gate2;

/// <summary>Stable, slimmed signature of one runtime error occurrence.</summary>
public sealed record ExceptionSignature(
    string Signature,
    string Message,
    string ExceptionType,
    string Source,
    int? Attempt);

/// <summary>
/// Bounded error-history compression for VRAM-constrained providers. Mirrors the
/// semantics of <c>nosai/runtime/context_slimming.py</c>: addresses and line numbers
/// are normalized away so retries of the same fault share one signature, and the
/// history a provider sees is capped to the most recent entries.
/// </summary>
public sealed class ErrorHistoryCompressor
{
    private static readonly Regex AddressPattern = new(@"0x[0-9a-fA-F]+", RegexOptions.Compiled);
    private static readonly Regex LinePattern = new(@"line\s+\d+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly int _maxErrors;
    private readonly int _maxMessageChars;

    public ErrorHistoryCompressor(int maxErrors = 3, int maxMessageChars = 240)
    {
        if (maxErrors < 1 || maxMessageChars < 32)
            throw new ArgumentOutOfRangeException(nameof(maxErrors), "Invalid context slimming limits.");
        _maxErrors = maxErrors;
        _maxMessageChars = maxMessageChars;
    }

    public static string Normalize(string error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return LinePattern.Replace(AddressPattern.Replace(error, "0xADDR"), "line N");
    }

    public static string ComputeSignature(string error)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(error)));
        return Convert.ToHexString(digest)[..16].ToLowerInvariant();
    }

    public ExceptionSignature CompressError(string rawError, string exceptionType = "unknown",
        string source = "unknown", int? attempt = null)
    {
        ArgumentNullException.ThrowIfNull(rawError);
        string lastLine = rawError
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .LastOrDefault() ?? "unknown error";
        if (lastLine.Length > _maxMessageChars) lastLine = lastLine[.._maxMessageChars];
        return new ExceptionSignature(ComputeSignature(rawError), lastLine, exceptionType, source, attempt);
    }

    public ExceptionSignature CompressError(Exception exception, string source = "unknown", int? attempt = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return CompressError(exception.Message, exception.GetType().Name, source, attempt);
    }

    /// <summary>Keeps only the most recent errors, numbering attempts over the full history.</summary>
    public ImmutableArray<ExceptionSignature> CompressHistory(IReadOnlyList<string> errorHistory)
    {
        ArgumentNullException.ThrowIfNull(errorHistory);
        int start = Math.Max(0, errorHistory.Count - _maxErrors);
        var recent = new List<ExceptionSignature>(Math.Min(_maxErrors, errorHistory.Count));
        for (int i = start; i < errorHistory.Count; i++)
            recent.Add(CompressError(errorHistory[i], attempt: i + 1));
        return recent.ToImmutableArray();
    }
}

/// <summary>One entity of the slimmed world context, kept intentionally small.</summary>
public sealed record SlimmedEntity(
    long EntityId,
    EntityType Type,
    string Name,
    double Distance,
    float HpRatio,
    bool IsAlive);

/// <summary>Compact world view bounded for VRAM-constrained decision providers.</summary>
public sealed record SlimmedWorldContext(
    ulong FrameIndex,
    int MapId,
    Position2D PlayerPosition,
    int PlayerHp, int PlayerMaxHp,
    int PlayerMp, int PlayerMaxMp,
    bool PlayerInCombat,
    int TotalEntityCount,
    bool IsDegradedState,
    ImmutableArray<SlimmedEntity> NearestEntities);

/// <summary>
/// Deterministic reduction of a full snapshot into a bounded context: the nearest
/// entities to the player, ordered by distance then id so equal-distance ties never
/// reorder between frames.
/// </summary>
public static class WorldContextSlimmer
{
    public const int MaxEntityNameLength = 24;

    public static SlimmedWorldContext Slim(WorldStateSnapshot snapshot, int maxEntities = 8)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (maxEntities < 1) throw new ArgumentOutOfRangeException(nameof(maxEntities));

        var player = snapshot.Player;
        var nearest = snapshot.Entities.Values
            .Select(e => (Entity: e, Distance: e.Position.DistanceTo(player.Position)))
            .OrderBy(x => x.Distance)
            .ThenBy(x => x.Entity.EntityId)
            .Take(maxEntities)
            .Select(x => new SlimmedEntity(
                x.Entity.EntityId,
                x.Entity.Type,
                x.Entity.Name.Length > MaxEntityNameLength ? x.Entity.Name[..MaxEntityNameLength] : x.Entity.Name,
                Math.Round(x.Distance, 1),
                x.Entity.MaxHp > 0 ? (float)x.Entity.CurrentHp / x.Entity.MaxHp : 0f,
                x.Entity.IsAlive))
            .ToImmutableArray();

        return new SlimmedWorldContext(
            snapshot.FrameIndex, player.MapId, player.Position,
            player.CurrentHp, player.MaxHp, player.CurrentMp, player.MaxMp,
            player.IsInCombat, snapshot.Entities.Count, snapshot.IsDegradedState, nearest);
    }
}
