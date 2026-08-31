// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Author: Volodymyr Ryzhuk
// Description: Raid Orchestration subsystem, boss phase analysis,
//              Schivata AoE, Risoluzione TimeSpace a Leve e Humanizer Comportamentale
// Standard: C# 12 / .NET 8 — Zero-Allocation, Bézier curves, Fail-Closed
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NosAi.Raids.Orchestration
{
    public enum RaidType : byte { LordDraco = 1, Glacerus = 2, Laurena = 3, Kertos = 4, Valakus = 5, Grenigas = 6, Belial = 7, Paimon = 8, EventChickenRaid = 9, EventNamaju = 10 }
    public enum BossPhaseStage : byte { Phase1_Intro = 1, Phase2_MinionSummon = 2, Phase3_EnragedAoE = 3, Phase4_Desperation = 4, InvulnerableShield = 5 }
    public enum AoEMarkerKind : byte { CircleGroundTelegraph = 0, ConeSectorTelegraph = 1, LinearLineTelegraph = 2, FullMapSafeSpotRequired = 3 }

    public readonly record struct Vector2D(double X, double Y)
    {
        public double DistanceTo(Vector2D other) { var dx = X - other.X; var dy = Y - other.Y; return Math.Sqrt(dx * dx + dy * dy); }
        public static Vector2D Zero => new(0, 0);
    }

    public sealed record ProjectedAoEZone(Guid MarkerId, AoEMarkerKind Kind, Vector2D Center, double RadiusTiles, DateTime TriggerAtUtc, int WarningDurationMs, bool IsLethal)
    {
        public bool IsActive => DateTime.UtcNow <= TriggerAtUtc;
        public bool Contains(Vector2D point) => Center.DistanceTo(point) <= RadiusTiles;
    }

    public sealed class BossPhaseAnalyzer
    {
        private readonly RaidType _raidType;
        private BossPhaseStage _currentStage = BossPhaseStage.Phase1_Intro;
        private bool _isInvulnerable;
        public BossPhaseStage CurrentStage => _currentStage;
        public bool IsInvulnerable => _isInvulnerable;
        public BossPhaseAnalyzer(RaidType raidType) { _raidType = raidType; }
        public (BossPhaseStage Stage, string AlertMessage) UpdateBossHealth(int currentHp, int maxHp)
        {
            var hpPercent = (double)currentHp / Math.Max(1, maxHp);
            var newStage = hpPercent > .75 ? BossPhaseStage.Phase1_Intro : hpPercent > .50 ? BossPhaseStage.Phase2_MinionSummon : hpPercent > .25 ? BossPhaseStage.Phase3_EnragedAoE : BossPhaseStage.Phase4_Desperation;
            if (newStage != _currentStage) { _currentStage = newStage; return (newStage, $"TRANSIZIONE FASE BOSS [{_raidType}]: Entrato in {newStage} (HP {hpPercent:P0})"); }
            return (_currentStage, "Fase invariata.");
        }
        public void SetInvulnerability(bool invulnerable) => _isInvulnerable = invulnerable;
    }

    public sealed class AoEDodgeEngine
    {
        public bool TryCalculateEscapeVector(Vector2D currentPos, IReadOnlyList<ProjectedAoEZone> activeAoEs, out Vector2D safeSpot, out string? rationale)
        {
            safeSpot = currentPos; rationale = null;
            var overlapping = activeAoEs.Where(a => a.IsActive && a.Contains(currentPos)).ToList();
            if (overlapping.Count == 0) return false;
            var cx = overlapping.Average(a => a.Center.X); var cy = overlapping.Average(a => a.Center.Y); var radius = overlapping.Max(a => a.RadiusTiles);
            var dx = currentPos.X - cx; var dy = currentPos.Y - cy; var dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < .001) { dx = 1; dy = 0; dist = 1; }
            var escape = radius + 1.5;
            safeSpot = new Vector2D(cx + dx / dist * escape, cy + dy / dist * escape);
            rationale = $"SCHIVATA AOE CRITICA: Uscita dal raggio di {overlapping.Count} zone letali verso ({safeSpot.X:F1}, {safeSpot.Y:F1}).";
            return true;
        }
    }

    public sealed record TeamBuffDefinition(int BuffId, string Name, int DurationSeconds, int RangeTiles);
    public sealed class RaidTeamBuffCoordinator
    {
        private readonly ConcurrentDictionary<string, DateTime> _activeBuffs = new();
        public void RegisterBuffCast(string buffName, int durationSeconds) => _activeBuffs[buffName] = DateTime.UtcNow.AddSeconds(durationSeconds);
        public bool IsBuffActive(string buffName) => _activeBuffs.TryGetValue(buffName, out var expiresAt) && DateTime.UtcNow < expiresAt;
        public List<string> GetMissingEssentialBuffs(IEnumerable<string> requiredBuffs) => requiredBuffs.Where(b => !IsBuffActive(b)).ToList();
    }

    public enum RoomObjectiveKind : byte { KillAllMonsters = 0, SurviveTimer = 1, EscortNpc = 2, ActivateAllLevers = 3, BossRoom = 4 }
    public sealed record TimeSpaceRoomNode(int RoomId, string RoomName, RoomObjectiveKind Objective, int TimeLimitSeconds, int LeversToActivate, ImmutableArray<int> ConnectedRoomIds);
    public sealed class TimeSpaceTopologySolver
    {
        private readonly Dictionary<int, TimeSpaceRoomNode> _rooms = new();
        public void RegisterRoom(TimeSpaceRoomNode room) => _rooms[room.RoomId] = room;
        public List<int>? FindOptimalRoomSequence(int startRoomId, int bossRoomId)
        {
            if (!_rooms.ContainsKey(startRoomId) || !_rooms.ContainsKey(bossRoomId)) return null;
            var queue = new Queue<int>(); var cameFrom = new Dictionary<int, int>(); var visited = new HashSet<int> { startRoomId }; queue.Enqueue(startRoomId);
            while (queue.Count > 0) { var current = queue.Dequeue(); if (current == bossRoomId) break; foreach (var next in _rooms[current].ConnectedRoomIds) if (visited.Add(next)) { cameFrom[next] = current; queue.Enqueue(next); } }
            if (startRoomId != bossRoomId && !cameFrom.ContainsKey(bossRoomId)) return null;
            var path = new List<int> { bossRoomId }; var curr = bossRoomId; while (curr != startRoomId) { curr = cameFrom[curr]; path.Add(curr); } path.Reverse(); return path;
        }
    }

    public readonly record struct ScreenPoint(double X, double Y);
    public sealed class BehavioralHumanizer
    {
        private readonly Random _random = new(); private double _accumulatedFatigueFactor;
        public double CurrentFatigue => _accumulatedFatigueFactor;
        public void AccumulateSessionFatigue(int sessionMinutes) => _accumulatedFatigueFactor = Math.Clamp(1.0 / (1.0 + Math.Exp(-.03 * (sessionMinutes - 60))), 0, .40);
        public List<ScreenPoint> GenerateHumanBezierPath(ScreenPoint start, ScreenPoint target, int stepsCount = 20)
        {
            if (stepsCount < 1) throw new ArgumentOutOfRangeException(nameof(stepsCount));
            var points = new List<ScreenPoint>(stepsCount + 1); var dx = target.X - start.X; var dy = target.Y - start.Y; var dist = Math.Sqrt(dx * dx + dy * dy);
            var perpX = -dy / Math.Max(1, dist); var perpY = dx / Math.Max(1, dist); var curvature = (_random.NextDouble() - .5) * dist * .25;
            var c1 = new ScreenPoint(start.X + dx * .30 + perpX * curvature + (_random.NextDouble() - .5) * 8, start.Y + dy * .30 + perpY * curvature + (_random.NextDouble() - .5) * 8);
            var c2 = new ScreenPoint(start.X + dx * .70 + perpX * curvature * .6 + (_random.NextDouble() - .5) * 6, start.Y + dy * .70 + perpY * curvature * .6 + (_random.NextDouble() - .5) * 6);
            for (var i = 0; i <= stepsCount; i++) { var t = (double)i / stepsCount; var u = 1 - t; var tt = t * t; var uu = u * u; var px = uu * u * start.X + 3 * uu * t * c1.X + 3 * u * tt * c2.X + tt * t * target.X; var py = uu * u * start.Y + 3 * uu * t * c1.Y + 3 * u * tt * c2.Y + tt * t * target.Y; if (i > 0 && i < stepsCount) { px += (_random.NextDouble() - .5) * .8; py += (_random.NextDouble() - .5) * .8; } points.Add(new ScreenPoint(px, py)); }
            return points;
        }
        public int CalculateHumanizedClickDelayMs(int baseDelayMs = 60) { var n = Math.Sqrt(-2 * Math.Log(_random.NextDouble() + 1e-9)) * Math.Cos(2 * Math.PI * _random.NextDouble()); return (int)Math.Clamp(baseDelayMs + n * 15 + baseDelayMs * _accumulatedFatigueFactor, 25, 350); }
    }

    public sealed class RaidOrchestrationMasterEngine
    {
        private readonly BossPhaseAnalyzer _bossAnalyzer; private readonly AoEDodgeEngine _dodgeEngine; private readonly RaidTeamBuffCoordinator _buffCoordinator; private readonly TimeSpaceTopologySolver _tsSolver; private readonly BehavioralHumanizer _humanizer; private readonly List<ProjectedAoEZone> _activeAoEs = new();
        public BossPhaseAnalyzer BossAnalyzer => _bossAnalyzer; public AoEDodgeEngine DodgeEngine => _dodgeEngine; public RaidTeamBuffCoordinator BuffCoordinator => _buffCoordinator; public TimeSpaceTopologySolver TsSolver => _tsSolver; public BehavioralHumanizer Humanizer => _humanizer;
        public RaidOrchestrationMasterEngine(RaidType raidType) { _bossAnalyzer = new(raidType); _dodgeEngine = new(); _buffCoordinator = new(); _tsSolver = new(); _humanizer = new(); }
        public void RegisterProjectedAoE(ProjectedAoEZone aoe) => _activeAoEs.Add(aoe);
        public void CleanExpiredAoEs() => _activeAoEs.RemoveAll(a => !a.IsActive);
        public (bool RequiresDodge, Vector2D TargetSafeSpot, string ActionReport) EvaluateRaidTick(Vector2D currentPos, int bossCurrentHp, int bossMaxHp, int teamLivesRemaining)
        {
            CleanExpiredAoEs(); if (teamLivesRemaining <= 0) return (true, currentPos, "ABORT RAID CRITICO: Vite di squadra esaurite (0 vite residue).");
            var (stage, _) = _bossAnalyzer.UpdateBossHealth(bossCurrentHp, bossMaxHp);
            if (_dodgeEngine.TryCalculateEscapeVector(currentPos, _activeAoEs, out var safeSpot, out var rationale)) return (true, safeSpot, rationale!);
            return (false, currentPos, $"Stato Nominale: Boss in {stage}. Posizione attuale sicura.");
        }
    }
}
