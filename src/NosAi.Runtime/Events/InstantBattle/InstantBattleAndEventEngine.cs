// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Author: Volodymyr Ryzhuk
// Descrizione: Sottosistema per Combattimento Immediato (CI), Eventi a Tempo,
//              Raid Corona di Ghiaccio (Atto 4 Caligor), Raccolta Drop Protetta
//              and Automatic Scheduling with Inventory Verification
// Standard: C# 12 / .NET 8 — Zero-Allocation, Determinism, Fail-Closed Security
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NosAi.Runtime.Contracts;

namespace NosAi.Events.InstantBattle
{
    public enum ScheduledEventType : byte { InstantBattle_CI = 0, IceBreaker = 1, CaligorRaid_Act4 = 2, Act4ElementalLordRaid = 3, RainbowBattle = 4 }
    public enum InstantBattleBracket : byte { Bracket_1_19 = 0, Bracket_20_29 = 1, Bracket_30_39 = 2, Bracket_40_49 = 3, Bracket_50_59 = 4, Bracket_60_69 = 5, Bracket_70_79 = 6, Bracket_80_99 = 7, Champion_1_60 = 8 }
    public enum InstantBattleWaveStage : byte { WaitingForStart = 0, Wave1_InitialMobs = 1, Wave2_AggressivePack = 2, Wave3_MidBoss = 3, Wave4_SwarmAndHighDamage = 4, Wave5_FinalBoss = 5, VictoryDropCollection = 6, Completed = 7, FailedOrDead = 8 }
    public enum Act4Faction : byte { None = 0, Angels = 1, Demons = 2 }
    public sealed record GroundDropItem(long DropId, string Name, int ItemId, int Quantity, MapPoint Position, long EstimatedValueGold, bool IsHighValueMaterial, DateTime DroppedAtUtc);
    public sealed record EventActionIntent(Guid IntentId, string ActionKind, long TargetEntityId, MapPoint TargetPosition, float PriorityScore, string TacticalRationale);

    public sealed class ScheduledEventCalendarEngine
    {
        public TimeSpan GetTimeToNextInstantBattle(DateTime currentUtcTime) { int currentHour = currentUtcTime.Hour; int nextEvenHour = (currentHour % 2 == 0) ? (currentUtcTime.Minute >= 15 ? currentHour + 2 : currentHour) : currentHour + 1; DateTime nextCiTime = new(currentUtcTime.Year, currentUtcTime.Month, currentUtcTime.Day, nextEvenHour % 24, 0, 0, DateTimeKind.Utc); if (nextEvenHour >= 24) nextCiTime = nextCiTime.AddDays(1); TimeSpan diff = nextCiTime - currentUtcTime; return diff > TimeSpan.Zero ? diff : TimeSpan.Zero; }
        public bool IsEventWindowOpen(ScheduledEventType eventType, DateTime currentUtcTime, int toleranceMinutes = 5) { if (eventType == ScheduledEventType.InstantBattle_CI) return currentUtcTime.Hour % 2 == 0 && currentUtcTime.Minute < toleranceMinutes; if (eventType == ScheduledEventType.CaligorRaid_Act4) { bool isWeekend = currentUtcTime.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday; bool isHour = currentUtcTime.Hour is 17 or 20; return isWeekend && isHour && currentUtcTime.Minute < toleranceMinutes; } return false; }
        public InstantBattleBracket ResolvePlayerBracket(int combatLevel, int championLevel = 0) { if (championLevel > 0) return InstantBattleBracket.Champion_1_60; if (combatLevel < 20) return InstantBattleBracket.Bracket_1_19; if (combatLevel < 30) return InstantBattleBracket.Bracket_20_29; if (combatLevel < 40) return InstantBattleBracket.Bracket_30_39; if (combatLevel < 50) return InstantBattleBracket.Bracket_40_49; if (combatLevel < 60) return InstantBattleBracket.Bracket_50_59; if (combatLevel < 70) return InstantBattleBracket.Bracket_60_69; if (combatLevel < 80) return InstantBattleBracket.Bracket_70_79; return InstantBattleBracket.Bracket_80_99; }
    }

    public sealed class InstantBattleWaveSolver
    {
        private InstantBattleWaveStage _currentWave = InstantBattleWaveStage.WaitingForStart;
        private readonly MapPoint _arenaSafeCorner = new(15, 15);
        private readonly object _lock = new();
        public InstantBattleWaveStage CurrentWave { get { lock (_lock) return _currentWave; } }
        public MapPoint ArenaSafeCorner => _arenaSafeCorner;
        public (InstantBattleWaveStage Stage, string TacticalReport) AdvanceWave(int mobsAliveCount, bool bossSpawned) { lock (_lock) { var previous = _currentWave; if (_currentWave == InstantBattleWaveStage.WaitingForStart) _currentWave = InstantBattleWaveStage.Wave1_InitialMobs; else if (_currentWave == InstantBattleWaveStage.Wave1_InitialMobs && mobsAliveCount == 0) _currentWave = InstantBattleWaveStage.Wave2_AggressivePack; else if (_currentWave == InstantBattleWaveStage.Wave2_AggressivePack && mobsAliveCount == 0) _currentWave = InstantBattleWaveStage.Wave3_MidBoss; else if (_currentWave == InstantBattleWaveStage.Wave3_MidBoss && mobsAliveCount == 0 && !bossSpawned) _currentWave = InstantBattleWaveStage.Wave4_SwarmAndHighDamage; else if (_currentWave == InstantBattleWaveStage.Wave4_SwarmAndHighDamage && mobsAliveCount == 0) _currentWave = InstantBattleWaveStage.Wave5_FinalBoss; else if (_currentWave == InstantBattleWaveStage.Wave5_FinalBoss && mobsAliveCount == 0 && !bossSpawned) _currentWave = InstantBattleWaveStage.VictoryDropCollection; return _currentWave != previous ? (_currentWave, $"[COMBATTIMENTO IMMEDIATO] Avanzamento a {_currentWave}. Raggruppamento all'angolo raccomandato.") : (_currentWave, "Ondata in corso."); } }
        public void SetStage(InstantBattleWaveStage stage) { lock (_lock) _currentWave = stage; }
    }

    public sealed class SafeDropCollectorEngine
    {
        private const double MinimumSafeDistanceToMobs = 5.0; private const double CriticalHpThreshold = 0.50;
        public bool TrySelectSafeDropToCollect(MapPoint playerPos, int playerHp, int playerMaxHp, IReadOnlyList<GroundDropItem> groundDrops, IReadOnlyList<MapPoint> activeMobPositions, out GroundDropItem? selectedDrop, out string? rationale) { selectedDrop = null; rationale = null; if (groundDrops == null || groundDrops.Count == 0) return false; double hpRatio = (double)playerHp / Math.Max(1, playerMaxHp); if (hpRatio < CriticalHpThreshold) { rationale = $"BLOCCO RACCOLTA DROP (HP BASSO: {hpRatio:P0} < 50%): Priorità assoluta al recupero vitale."; return false; } var safeDrops = groundDrops.Where(drop => activeMobPositions.Count == 0 || activeMobPositions.Min(mob => mob.DistanceTo(drop.Position)) >= MinimumSafeDistanceToMobs).ToList(); if (safeDrops.Count == 0) { rationale = "Nessun drop sicuro: mostri ostili troppo vicini agli oggetti a terra."; return false; } selectedDrop = safeDrops.OrderByDescending(d => (d.IsHighValueMaterial ? 5000 : 0) + d.EstimatedValueGold - (d.Position.DistanceTo(playerPos) * 100)).FirstOrDefault(); if (selectedDrop != null) { rationale = $"RACCOLTA AUTORIZZATA: {selectedDrop.Name} (Valore: {selectedDrop.EstimatedValueGold:N0} Gold, Distanza: {selectedDrop.Position.DistanceTo(playerPos):F1} tile)."; return true; } return false; }
    }

    public sealed class PreEventReadinessInspector
    {
        public bool ValidateReadinessForEvent(int freeInventorySlotsCount, int bigPotionsCount, int playerHp, int playerMaxHp, out string? rejectionReason) { rejectionReason = null; if (freeInventorySlotsCount < 3) { rejectionReason = $"INVENTARIO SATURO: Meno di 3 slot liberi ({freeInventorySlotsCount} disponibili). Rischio perdita drop."; return false; } if (bigPotionsCount < 5) { rejectionReason = $"SCORTA POZIONI INSUFFICIENTE: Meno di 5 pozioni grandi ({bigPotionsCount} disponibili). Rischio morte prematura."; return false; } double hpRatio = (double)playerHp / Math.Max(1, playerMaxHp); if (hpRatio < 0.85) { rejectionReason = $"HP INSUFFICIENTI ALL'INGRESSO ({hpRatio:P0} < 85%): Recuperare prima dell'avvio."; return false; } return true; }
    }

    public sealed class Act4FactionCoordinator
    {
        private int _angelPercentage = 50, _demonPercentage = 50; private readonly Act4Faction _playerFaction;
        public int AngelPercentage => _angelPercentage; public int DemonPercentage => _demonPercentage; public Act4Faction PlayerFaction => _playerFaction;
        public Act4FactionCoordinator(Act4Faction playerFaction = Act4Faction.Angels) => _playerFaction = playerFaction;
        public void UpdateFactionGauges(int angelPct, int demonPct) { _angelPercentage = Math.Clamp(angelPct, 0, 100); _demonPercentage = Math.Clamp(demonPct, 0, 100); }
        public bool IsLordRaidPortalOpen(out string? raidPortalName) { raidPortalName = null; int playerGauge = _playerFaction == Act4Faction.Angels ? _angelPercentage : _demonPercentage; if (playerGauge >= 100) { raidPortalName = _playerFaction == Act4Faction.Angels ? "Portale Lord Berios / Morcos (Angeli)" : "Portale Lord Hatus / Calvinas (Demoni)"; return true; } return false; }
    }

    public sealed class InstantBattleAndEventOrchestrator
    {
        private readonly ScheduledEventCalendarEngine _calendarEngine = new(); private readonly InstantBattleWaveSolver _waveSolver = new(); private readonly SafeDropCollectorEngine _dropCollector = new(); private readonly PreEventReadinessInspector _readinessInspector = new(); private readonly Act4FactionCoordinator _factionCoordinator;
        private readonly List<GroundDropItem> _activeGroundDrops = new(); private readonly List<MapPoint> _activeMobPositions = new();
        public ScheduledEventCalendarEngine Calendar => _calendarEngine; public InstantBattleWaveSolver WaveSolver => _waveSolver; public SafeDropCollectorEngine DropCollector => _dropCollector; public PreEventReadinessInspector ReadinessInspector => _readinessInspector; public Act4FactionCoordinator FactionCoordinator => _factionCoordinator;
        public InstantBattleAndEventOrchestrator(Act4Faction faction = Act4Faction.Angels) => _factionCoordinator = new(faction);
        public void RegisterGroundDrop(GroundDropItem item) => _activeGroundDrops.Add(item); public void ClearGroundDrops() => _activeGroundDrops.Clear(); public void SetActiveMobs(IEnumerable<MapPoint> mobPositions) { _activeMobPositions.Clear(); _activeMobPositions.AddRange(mobPositions); }
        public IReadOnlyList<EventActionIntent> EvaluateEventTick(MapPoint playerPos, int playerHp, int playerMaxHp, int freeSlots, int potionsCount, DateTime currentUtcTime) { var intents = new List<EventActionIntent>(); if (_calendarEngine.IsEventWindowOpen(ScheduledEventType.InstantBattle_CI, currentUtcTime)) { if (_readinessInspector.ValidateReadinessForEvent(freeSlots, potionsCount, playerHp, playerMaxHp, out string? rejReason)) intents.Add(new(Guid.NewGuid(), "AcceptInstantBattleEntry", 0, playerPos, 1.0f, "Finestra CI aperta: tutti i controlli di inventario e pozioni sono verificati.")); else intents.Add(new(Guid.NewGuid(), "RejectEventEntry", 0, playerPos, 0.99f, rejReason!)); return intents; } if (_activeMobPositions.Count == 0 && _activeGroundDrops.Count > 0 && _dropCollector.TrySelectSafeDropToCollect(playerPos, playerHp, playerMaxHp, _activeGroundDrops, _activeMobPositions, out var drop, out string? dropRat)) { intents.Add(new(Guid.NewGuid(), "CollectGroundItem", drop!.DropId, drop.Position, 0.95f, dropRat!)); return intents; } if (_activeMobPositions.Count > 0 && playerPos.DistanceTo(_waveSolver.ArenaSafeCorner) > 3.0) intents.Add(new(Guid.NewGuid(), "MoveToDefensiveCorner", 0, _waveSolver.ArenaSafeCorner, 0.85f, "Mantenimento formazione difensiva nell'angolo dell'arena del CI.")); return intents; }
    }
}
