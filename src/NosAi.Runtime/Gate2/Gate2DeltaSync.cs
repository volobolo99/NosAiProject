// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Gate 2 — Versioned binary delta codec and synchronisation tracker
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.Gate2;

/// <summary>
/// Versioned binary encoding of <see cref="WorldStateDeltaPacket"/> (format "G2D", v1).
/// </summary>
/// <remarks>
/// This is the Gate 2 internal encoding used to obtain and measure the delta
/// bandwidth saving. It is NOT the canonical PC-phone wire protocol: per
/// ADR-0006 that remains <c>GuardAiNetworkChannel</c>; any adoption there is a
/// separate, explicit protocol change. Per ADR-0005 the format carries a version
/// byte so future revisions stay detectable.
/// </remarks>
public static class WorldStateDeltaCodec
{
    private const byte Version = 1;
    private static readonly byte[] Magic = { (byte)'G', (byte)'2', (byte)'D', Version };

    private const byte PacketFlagPlayerInCombat = 1 << 0;

    private const byte EntityFlagRemoved = 1 << 0;
    private const byte EntityFlagHasPosition = 1 << 1;
    private const byte EntityFlagHasHp = 1 << 2;
    private const byte EntityFlagHasAlive = 1 << 3;
    private const byte EntityFlagAliveValue = 1 << 4;
    private const byte EntityFlagHasNewEntity = 1 << 5;
    private const byte EntityFlagHasCombat = 1 << 6;
    private const byte EntityFlagCombatValue = 1 << 7;

    private const byte NewEntityFlagAlive = 1 << 0;
    private const byte NewEntityFlagTargetable = 1 << 1;

    private const int MaxStringBytes = 4096;
    private const int MaxEntityCount = 1_000_000;

    public static byte[] Serialize(WorldStateDeltaPacket delta)
    {
        ArgumentNullException.ThrowIfNull(delta);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(Magic);
        WriteString(writer, delta.SessionId);
        writer.Write(delta.BaseFrameIndex);
        writer.Write(delta.TargetFrameIndex);
        writer.Write(delta.PlayerPosition.X);
        writer.Write(delta.PlayerPosition.Y);
        writer.Write(delta.PlayerHp);
        writer.Write(delta.PlayerMp);
        writer.Write(delta.PlayerInCombat ? PacketFlagPlayerInCombat : (byte)0);

        var mutations = delta.MutatedEntities.IsDefault ? ImmutableArray<EntityDelta>.Empty : delta.MutatedEntities;
        writer.Write(mutations.Length);
        foreach (var entity in mutations)
        {
            writer.Write(entity.EntityId);
            byte flags = 0;
            if (entity.IsRemoved) flags |= EntityFlagRemoved;
            if (entity.NewPosition is not null) flags |= EntityFlagHasPosition;
            if (entity.NewHp is not null) flags |= EntityFlagHasHp;
            if (entity.NewIsAlive is not null)
            {
                flags |= EntityFlagHasAlive;
                if (entity.NewIsAlive.Value) flags |= EntityFlagAliveValue;
            }
            if (entity.NewIsCombat is not null)
            {
                flags |= EntityFlagHasCombat;
                if (entity.NewIsCombat.Value) flags |= EntityFlagCombatValue;
            }
            if (entity.NewEntity is not null) flags |= EntityFlagHasNewEntity;
            writer.Write(flags);

            if (entity.NewPosition is { } position)
            {
                writer.Write(position.X);
                writer.Write(position.Y);
            }
            if (entity.NewHp is { } hp) writer.Write(hp);
            if (entity.NewEntity is { } full)
            {
                writer.Write((byte)full.Type);
                WriteString(writer, full.Name);
                writer.Write(full.CurrentHp);
                writer.Write(full.MaxHp);
                writer.Write(full.Position.X);
                writer.Write(full.Position.Y);
                byte entityFlags = 0;
                if (full.IsAlive) entityFlags |= NewEntityFlagAlive;
                if (full.IsTargetable) entityFlags |= NewEntityFlagTargetable;
                writer.Write(entityFlags);
                writer.Write((byte)full.Provenance);
                writer.Write(full.ConfidenceScore);
                writer.Write(full.LastObservedUtc.Ticks);
            }
        }

        writer.Flush();
        return stream.ToArray();
    }

    public static bool TryDeserialize(ReadOnlyMemory<byte> payload, out WorldStateDeltaPacket? delta)
    {
        delta = null;
        try
        {
            using var stream = new MemoryStream(payload.ToArray(), writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

            var magic = reader.ReadBytes(Magic.Length);
            if (magic.Length != Magic.Length) return false;
            for (int i = 0; i < Magic.Length; i++)
            {
                if (magic[i] != Magic[i]) return false;
            }

            if (!TryReadString(reader, out string sessionId)) return false;
            ulong baseFrame = reader.ReadUInt64();
            ulong targetFrame = reader.ReadUInt64();
            if (targetFrame < baseFrame) return false;
            var playerPosition = new MapPoint(reader.ReadInt32(), reader.ReadInt32());
            int playerHp = reader.ReadInt32();
            int playerMp = reader.ReadInt32();
            byte packetFlags = reader.ReadByte();

            int entityCount = reader.ReadInt32();
            if (entityCount is < 0 or > MaxEntityCount) return false;
            var mutations = ImmutableArray.CreateBuilder<EntityDelta>(entityCount);
            for (int i = 0; i < entityCount; i++)
            {
                long entityId = reader.ReadInt64();
                byte flags = reader.ReadByte();
                MapPoint? position = null;
                if ((flags & EntityFlagHasPosition) != 0)
                    position = new MapPoint(reader.ReadInt32(), reader.ReadInt32());
                int? hp = (flags & EntityFlagHasHp) != 0 ? reader.ReadInt32() : null;
                bool? alive = (flags & EntityFlagHasAlive) != 0 ? (flags & EntityFlagAliveValue) != 0 : null;
                bool? combat = (flags & EntityFlagHasCombat) != 0 ? (flags & EntityFlagCombatValue) != 0 : null;

                WorldEntity? full = null;
                if ((flags & EntityFlagHasNewEntity) != 0)
                {
                    var type = (EntityType)reader.ReadByte();
                    if (!TryReadString(reader, out string name)) return false;
                    int currentHp = reader.ReadInt32();
                    int maxHp = reader.ReadInt32();
                    var entityPosition = new MapPoint(reader.ReadInt32(), reader.ReadInt32());
                    byte entityFlags = reader.ReadByte();
                    var provenance = (DataProvenance)reader.ReadByte();
                    float confidence = reader.ReadSingle();
                    long ticks = reader.ReadInt64();
                    if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks) return false;
                    full = new WorldEntity(entityId, type, name, entityPosition, currentHp, maxHp,
                        (entityFlags & NewEntityFlagAlive) != 0, (entityFlags & NewEntityFlagTargetable) != 0,
                        provenance, confidence, new DateTime(ticks, DateTimeKind.Utc));
                }

                mutations.Add(new EntityDelta(entityId, (flags & EntityFlagRemoved) != 0, position, hp, alive, combat, full));
            }

            // Trailing bytes mean the payload is not a well-formed v1 frame.
            if (stream.Position != stream.Length) return false;

            delta = new WorldStateDeltaPacket(sessionId, baseFrame, targetFrame, playerPosition,
                playerHp, playerMp, (packetFlags & PacketFlagPlayerInCombat) != 0, mutations.MoveToImmutable());
            return true;
        }
        catch (EndOfStreamException) { return false; }
        catch (IOException) { return false; }
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length > MaxStringBytes)
            throw new ArgumentException($"String exceeds the {MaxStringBytes}-byte codec limit.", nameof(value));
        writer.Write((ushort)bytes.Length);
        writer.Write(bytes);
    }

    private static bool TryReadString(BinaryReader reader, out string value)
    {
        value = string.Empty;
        ushort length = reader.ReadUInt16();
        if (length > MaxStringBytes) return false;
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length) return false;
        value = Encoding.UTF8.GetString(bytes);
        return true;
    }
}

/// <summary>The update a consumer receives: either a delta or a full resync, never both.</summary>
public sealed record SyncUpdate(bool IsFullResync, WorldStateSnapshot? FullSnapshot, WorldStateDeltaPacket? Delta);

/// <summary>
/// Tracks per-consumer delta baselines over a bounded snapshot history.
/// A consumer whose acknowledged base frame has been evicted gets a full resync
/// instead of an unreconstructable delta chain — fail closed, never fabricate.
/// </summary>
public sealed class DeltaSyncTracker
{
    private readonly object _lock = new();
    private readonly int _historyCapacity;
    private readonly SortedDictionary<ulong, WorldStateSnapshot> _history = new();
    private readonly Dictionary<string, ulong?> _consumerBaseFrames = new(StringComparer.Ordinal);
    private WorldStateSnapshot? _current;

    public DeltaSyncTracker(int historyCapacity = 32)
    {
        if (historyCapacity < 2) throw new ArgumentOutOfRangeException(nameof(historyCapacity));
        _historyCapacity = historyCapacity;
    }

    public void TrackFrame(WorldStateSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_lock)
        {
            if (_current is not null)
            {
                if (!string.Equals(snapshot.SessionId, _current.SessionId, StringComparison.Ordinal))
                    throw new InvalidOperationException("Delta tracking is single-session.");
                if (snapshot.FrameIndex <= _current.FrameIndex)
                    throw new InvalidOperationException("Tracked frames must advance monotonically.");
            }
            _history[snapshot.FrameIndex] = snapshot;
            _current = snapshot;
            while (_history.Count > _historyCapacity)
            {
                using var enumerator = _history.GetEnumerator();
                enumerator.MoveNext();
                _history.Remove(enumerator.Current.Key);
            }
        }
    }

    public void RegisterConsumer(string consumerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerId);
        lock (_lock) _consumerBaseFrames[consumerId] = null;
    }

    public SyncUpdate ProduceUpdate(string consumerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerId);
        lock (_lock)
        {
            if (_current is null) throw new InvalidOperationException("No frame has been tracked yet.");
            if (!_consumerBaseFrames.TryGetValue(consumerId, out ulong? baseFrame))
                throw new InvalidOperationException($"Unknown delta consumer '{consumerId}'.");

            if (baseFrame is null || !_history.TryGetValue(baseFrame.Value, out var baseSnapshot))
                return new SyncUpdate(true, _current, null);

            return new SyncUpdate(false, null, WorldStateDeltaEngine.ComputeDelta(baseSnapshot, _current));
        }
    }

    public void Acknowledge(string consumerId, ulong frameIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerId);
        lock (_lock)
        {
            if (!_consumerBaseFrames.ContainsKey(consumerId))
                throw new InvalidOperationException($"Unknown delta consumer '{consumerId}'.");
            if (_current is null || frameIndex > _current.FrameIndex)
                throw new InvalidOperationException("Cannot acknowledge a frame that was never produced.");
            _consumerBaseFrames[consumerId] = frameIndex;
        }
    }
}
