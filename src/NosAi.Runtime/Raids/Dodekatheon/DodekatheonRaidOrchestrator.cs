// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Versione: 1.0 Beta
// Autore: Volodymyr Ryzhuk
// Descrizione: Sottosistema Specializzato per Raid Celestiali ed Endgame
//              (Dodekatheon / Atto 8): Meccaniche Boss, Barra Stagger, Scudi
//              Elementali, Schivata Laser e Coordinamento Sinergico di Squadra
// Standard: C# 12 / .NET 8 — Zero-Allocation, Determinismo, Fail-Closed Security
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NosAi.Raids.Dodekatheon
{
    #region 1. Contratti di Dominio e Modelli di Stato Celestiale

    public enum CelestialElement : byte { Neutral = 0, Fire = 1, Water = 2, Light = 3, Shadow = 4 }
    public enum DodekaBossPhase : byte { Phase1_OrbCleansing = 1, Phase2_ElementalShields = 2, Phase3_StaggerDpsCheck = 3, Phase4_CataclysmFrenzy = 4, InvulnerableStasis = 5 }
    public enum CelestialTelegraphKind : byte { SweepingLaserBeam = 0, MeteorFallCircle = 1, ElementalShockwave = 2, SafeSanctuaryDome = 3 }

    public readonly record struct Position2D(double X, double Y)
    {
        public double DistanceTo(Position2D other) { double dx = X - other.X, dy = Y - other.Y; return Math.Sqrt(dx * dx + dy * dy); }
        public static Position2D Zero => new(0, 0);
    }

    public sealed record ProjectedCelestialTelegraph(Guid TelegraphId, CelestialTelegraphKind Kind, Position2D Center, double RadiusOrLengthTiles, double AngleDegrees, DateTime TriggerAtUtc, int WarningDurationMs, bool IsInstantKill)
    {
        public bool IsActive => DateTime.UtcNow <= TriggerAtUtc;
        public bool ContainsPoint(Position2D point)
        {
            if (Kind is CelestialTelegraphKind.MeteorFallCircle or CelestialTelegraphKind.ElementalShockwave or CelestialTelegraphKind.SafeSanctuaryDome) return Center.DistanceTo(point) <= RadiusOrLengthTiles;
            double dist = Center.DistanceTo(point); if (dist > RadiusOrLengthTiles) return false;
            double angleToPoint = Math.Atan2(point.Y - Center.Y, point.X - Center.X) * (180.0 / Math.PI); if (angleToPoint < 0) angleToPoint += 360.0;
            double diff = Math.Abs(angleToPoint - AngleDegrees); if (diff > 180.0) diff = 360.0 - diff;
            return diff <= 25.0;
        }
    }

    public sealed record BossStaggerGauge(int CurrentStaggerPoints, int MaxStaggerThreshold, bool IsBrokenAndVulnerable, DateTime BrokenUntilUtc)
    { public double StaggerPercentage => (double)CurrentStaggerPoints / Math.Max(1, MaxStaggerThreshold); }

    public sealed record DodekaTacticalIntent(Guid IntentId, string ActionType, long TargetEntityId, Position2D TargetPosition, CelestialElement RequiredAttackElement, float PriorityScore, string TacticalRationale);

    #endregion

    #region 2. Analizzatore Fasi Boss Dodekatheon & Stagger Gauge Engine
    public sealed class DodekaBossMechanicsEngine
    {
        private DodekaBossPhase _currentPhase = DodekaBossPhase.Phase1_OrbCleansing;
        private CelestialElement _activeShieldElement = CelestialElement.Light;
        private BossStaggerGauge _staggerGauge;
        private bool _isInvulnerable;
        private readonly object _lock = new();
        public DodekaBossPhase CurrentPhase { get { lock (_lock) return _currentPhase; } }
        public CelestialElement ActiveShieldElement { get { lock (_lock) return _activeShieldElement; } }
        public BossStaggerGauge StaggerGauge { get { lock (_lock) return _staggerGauge; } }
        public bool IsInvulnerable { get { lock (_lock) return _isInvulnerable; } }
        public DodekaBossMechanicsEngine(int staggerThreshold = 1000) { _staggerGauge = new BossStaggerGauge(0, staggerThreshold, false, DateTime.MinValue); }
        public (DodekaBossPhase NewPhase, string AlertMessage) UpdateHealth(int currentHp, int maxHp)
        {
            lock (_lock)
            {
                double hpPercent = (double)currentHp / Math.Max(1, maxHp); DodekaBossPhase previous = _currentPhase;
                if (hpPercent > 0.75) { _currentPhase = DodekaBossPhase.Phase1_OrbCleansing; _activeShieldElement = CelestialElement.Light; }
                else if (hpPercent > 0.50) { _currentPhase = DodekaBossPhase.Phase2_ElementalShields; _activeShieldElement = (currentHp % 2 == 0) ? CelestialElement.Shadow : CelestialElement.Fire; }
                else if (hpPercent > 0.25) { _currentPhase = DodekaBossPhase.Phase3_StaggerDpsCheck; _activeShieldElement = CelestialElement.Water; }
                else { _currentPhase = DodekaBossPhase.Phase4_CataclysmFrenzy; _activeShieldElement = CelestialElement.Neutral; }
                if (_currentPhase != previous) return (_currentPhase, $"[DODEKATHEON] Transizione Fase Boss: {_currentPhase} (HP {hpPercent:P0}) - Scudo Elementale: {_activeShieldElement}");
                return (_currentPhase, "Stato fase invariato.");
            }
        }
        public bool ApplyStaggerDamage(int staggerDamage, out string? breakReport)
        {
            lock (_lock)
            {
                breakReport = null;
                if (_staggerGauge.IsBrokenAndVulnerable)
                {
                    if (DateTime.UtcNow >= _staggerGauge.BrokenUntilUtc) _staggerGauge = new BossStaggerGauge(0, _staggerGauge.MaxStaggerThreshold, false, DateTime.MinValue);
                    else return true;
                }
                int newPoints = _staggerGauge.CurrentStaggerPoints + staggerDamage;
                if (newPoints >= _staggerGauge.MaxStaggerThreshold)
                {
                    _staggerGauge = new BossStaggerGauge(_staggerGauge.MaxStaggerThreshold, _staggerGauge.MaxStaggerThreshold, true, DateTime.UtcNow.AddSeconds(8));
                    breakReport = "[STAGGER BREAK] Guardia del Boss infranta! Danno maggiorato del 150% per 8 secondi."; return true;
                }
                _staggerGauge = _staggerGauge with { CurrentStaggerPoints = newPoints }; return false;
            }
        }
        public void SetInvulnerableStasis(bool invulnerable) { lock (_lock) { _isInvulnerable = invulnerable; } }
    }
    #endregion

    #region 3. AoE Radar & Safe-Spot Resolver Celestiale
    public sealed class CelestialSafeSpotResolver
    {
        public bool TryResolveSafePosition(Position2D currentPos, IReadOnlyList<ProjectedCelestialTelegraph> activeTelegraphs, out Position2D safePosition, out string? dodgeRationale)
        {
            safePosition = currentPos; dodgeRationale = null;
            var sanctuaryDome = activeTelegraphs.FirstOrDefault(t => t.IsActive && t.Kind == CelestialTelegraphKind.SafeSanctuaryDome);
            if (sanctuaryDome != null)
            {
                if (!sanctuaryDome.ContainsPoint(currentPos)) { safePosition = sanctuaryDome.Center; dodgeRationale = "[PRIORITÀ ASSOLUTA SANCTUARY]: Riposizionamento immediato all'interno della Cupola Protettiva per evitare morte istantanea."; return true; }
                return false;
            }
            var dangerousOverlaps = activeTelegraphs.Where(t => t.IsActive && t.ContainsPoint(currentPos)).ToList(); if (dangerousOverlaps.Count == 0) return false;
            double avgDangerX = dangerousOverlaps.Average(t => t.Center.X), avgDangerY = dangerousOverlaps.Average(t => t.Center.Y), maxRange = dangerousOverlaps.Max(t => t.RadiusOrLengthTiles);
            double dirX = currentPos.X - avgDangerX, dirY = currentPos.Y - avgDangerY, len = Math.Sqrt(dirX * dirX + dirY * dirY); if (len < 0.001) { dirX = 1.0; dirY = 0.0; len = 1.0; }
            double safeMargin = maxRange + 2.0;
            safePosition = new Position2D(avgDangerX + (dirX / len) * safeMargin, avgDangerY + (dirY / len) * safeMargin);
            dodgeRationale = $"[SCHIVATA CELESTIALE]: Allontanamento da {dangerousOverlaps.Count} indicatori letali verso ({safePosition.X:F1}, {safePosition.Y:F1})."; return true;
        }
    }
    #endregion

    #region 4. Sincronizzatore Sinergie di Squadra & Debuff Stacking
    public sealed class CelestialTeamCoordinator
    {
        private readonly ConcurrentDictionary<string, int> _activeDebuffStacks = new();
        public CelestialElement GetOppositeCounterElement(CelestialElement shieldElement) => shieldElement switch { CelestialElement.Fire => CelestialElement.Water, CelestialElement.Water => CelestialElement.Fire, CelestialElement.Light => CelestialElement.Shadow, CelestialElement.Shadow => CelestialElement.Light, _ => CelestialElement.Neutral };
        public void ApplyDebuffStack(string debuffName, int maxStacks = 5) { _activeDebuffStacks.AddOrUpdate(debuffName, 1, (_, current) => Math.Min(maxStacks, current + 1)); }
        public int GetDebuffStacks(string debuffName) => _activeDebuffStacks.TryGetValue(debuffName, out int stacks) ? stacks : 0;
        public void ResetDebuffs() => _activeDebuffStacks.Clear();
    }
    #endregion

    #region 5. Orchestratore Principale Raid Dodekatheon
    public sealed class DodekatheonRaidOrchestrator
    {
        private readonly DodekaBossMechanicsEngine _mechanicsEngine; private readonly CelestialSafeSpotResolver _safeSpotResolver; private readonly CelestialTeamCoordinator _teamCoordinator; private readonly List<ProjectedCelestialTelegraph> _activeTelegraphs = new();
        public DodekaBossMechanicsEngine Mechanics => _mechanicsEngine; public CelestialSafeSpotResolver SafeSpotResolver => _safeSpotResolver; public CelestialTeamCoordinator TeamCoordinator => _teamCoordinator;
        public DodekatheonRaidOrchestrator(int staggerThreshold = 1000) { _mechanicsEngine = new DodekaBossMechanicsEngine(staggerThreshold); _safeSpotResolver = new CelestialSafeSpotResolver(); _teamCoordinator = new CelestialTeamCoordinator(); }
        public void RegisterTelegraph(ProjectedCelestialTelegraph telegraph) { _activeTelegraphs.Add(telegraph); }
        public void CleanExpiredTelegraphs() { _activeTelegraphs.RemoveAll(t => !t.IsActive); }
        public IReadOnlyList<DodekaTacticalIntent> EvaluateRaidTick(Position2D playerPos, int bossCurrentHp, int bossMaxHp, int teamLivesRemaining, bool hasTotemToActivate)
        {
            var intents = new List<DodekaTacticalIntent>(); CleanExpiredTelegraphs();
            if (teamLivesRemaining <= 0) { intents.Add(new DodekaTacticalIntent(Guid.NewGuid(), "AbortRaidFailClosed", 0, playerPos, CelestialElement.Neutral, 1.0f, "ABORT FAIL-CLOSED: 0 vite di squadra residue nel Raid Dodekatheon.")); return intents; }
            if (_safeSpotResolver.TryResolveSafePosition(playerPos, _activeTelegraphs, out Position2D safePos, out string? dodgeRationale)) { intents.Add(new DodekaTacticalIntent(Guid.NewGuid(), "MoveToSafeSpot", 0, safePos, CelestialElement.Neutral, 0.99f, dodgeRationale!)); return intents; }
            var (phase, alert) = _mechanicsEngine.UpdateHealth(bossCurrentHp, bossMaxHp);
            if (_mechanicsEngine.IsInvulnerable && hasTotemToActivate) { intents.Add(new DodekaTacticalIntent(Guid.NewGuid(), "InteractWithCelestialTotem", 8888, new Position2D(playerPos.X + 2, playerPos.Y + 2), CelestialElement.Neutral, 0.95f, "Boss in Stasi Invulnerabile: Attivazione totem per spezzare l'immunità.")); return intents; }
            CelestialElement counterElement = _teamCoordinator.GetOppositeCounterElement(_mechanicsEngine.ActiveShieldElement);
            intents.Add(new DodekaTacticalIntent(Guid.NewGuid(), _mechanicsEngine.StaggerGauge.IsBrokenAndVulnerable ? "ExecuteBurstCombo" : "AttackBossWithCounterElement", 9999, new Position2D(50, 50), counterElement, 0.90f, $"Ingaggio offensivo: Scudo Boss {_mechanicsEngine.ActiveShieldElement} -> Elemento di contrasto {counterElement}. {alert}")); return intents;
        }
    }
    #endregion

    #region 6. Suite di Test Automatica per il Modulo Dodekatheon
    public static class DodekatheonRaidTestRunner
    {
        public static async Task<bool> RunAllTestsAsync()
        {
            Console.WriteLine("=== Dodekatheon raid checks ===");
            bool allPassed = true;
            allPassed &= RunTest("Test 1: Transizioni di Fase Dodekatheon & Scudi Elementali", TestDodekaPhaseTransitions);
            allPassed &= RunTest("Test 2: Meccanica Stagger Gauge & Stordimento 8 Secondi", TestStaggerGaugeBreak);
            allPassed &= RunTest("Test 3: Priorità Assoluta Cupola SafeSanctuaryDome", TestSafeSanctuaryDomePriority);
            allPassed &= RunTest("Test 4: Calcolo Elemento Opposto di Contrasto (TeamCoordinator)", TestOppositeElementCounter);
            allPassed &= RunTest("Test 5: Abort Fail-Closed con 0 Vite Residue del Team", TestFailClosedZeroLivesAbort);
            allPassed &= RunTest("Test 6: Invariante Architetturale (Dodeka Non-Executable)", TestDodekaSecurityInvariant);
            Console.WriteLine(allPassed ? "=== Dodekatheon raid checks passed. Local only: this is not real-environment verification. ===" : "=== Dodekatheon raid checks FAILED. See the lines marked FAIL above. ===");
            await Task.CompletedTask; return allPassed;
        }
        private static bool RunTest(string testName, Func<bool> testFunc) { try { bool result = testFunc(); PrintResult(testName, result); return result; } catch (Exception ex) { PrintResult(testName, false, ex.Message); return false; } }
        private static void PrintResult(string name, bool passed, string? error = null) { Console.Write($"[{(passed ? "PASS" : "FAIL")}] {name,-62}"); if (passed) { Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine(" [OK]"); } else { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine($" [ERRORE: {error ?? "Asserzione fallita"}]"); } Console.ResetColor(); }
        private static bool TestDodekaPhaseTransitions() { var engine = new DodekaBossMechanicsEngine(1000); var (p1, _) = engine.UpdateHealth(90000, 100000); var (p2, _) = engine.UpdateHealth(65000, 100000); var (p3, _) = engine.UpdateHealth(35000, 100000); var (p4, _) = engine.UpdateHealth(10000, 100000); return p1 == DodekaBossPhase.Phase1_OrbCleansing && p2 == DodekaBossPhase.Phase2_ElementalShields && p3 == DodekaBossPhase.Phase3_StaggerDpsCheck && p4 == DodekaBossPhase.Phase4_CataclysmFrenzy; }
        private static bool TestStaggerGaugeBreak() { var engine = new DodekaBossMechanicsEngine(500); bool break1 = engine.ApplyStaggerDamage(200, out _); bool break2 = engine.ApplyStaggerDamage(350, out string? report); return !break1 && break2 && engine.StaggerGauge.IsBrokenAndVulnerable && report != null && report.Contains("STAGGER BREAK"); }
        private static bool TestSafeSanctuaryDomePriority() { var resolver = new CelestialSafeSpotResolver(); var playerPos = new Position2D(10, 10); var dome = new ProjectedCelestialTelegraph(Guid.NewGuid(), CelestialTelegraphKind.SafeSanctuaryDome, new Position2D(30, 30), 5.0, 0, DateTime.UtcNow.AddSeconds(2), 2000, true); bool needMove = resolver.TryResolveSafePosition(playerPos, new[] { dome }, out Position2D targetSafe, out string? rationale); return needMove && targetSafe == new Position2D(30, 30) && rationale != null && rationale.Contains("SANCTUARY"); }
        private static bool TestOppositeElementCounter() { var coordinator = new CelestialTeamCoordinator(); bool lightVsShadow = coordinator.GetOppositeCounterElement(CelestialElement.Light) == CelestialElement.Shadow; bool shadowVsLight = coordinator.GetOppositeCounterElement(CelestialElement.Shadow) == CelestialElement.Light; bool fireVsWater = coordinator.GetOppositeCounterElement(CelestialElement.Fire) == CelestialElement.Water; bool waterVsFire = coordinator.GetOppositeCounterElement(CelestialElement.Water) == CelestialElement.Fire; return lightVsShadow && shadowVsLight && fireVsWater && waterVsFire; }
        private static bool TestFailClosedZeroLivesAbort() { var orchestrator = new DodekatheonRaidOrchestrator(); var intents = orchestrator.EvaluateRaidTick(new Position2D(50, 50), 50000, 100000, 0, false); return intents.Count == 1 && intents[0].ActionType == "AbortRaidFailClosed" && intents[0].PriorityScore == 1.0f; }
        private static bool TestDodekaSecurityInvariant() { var types = typeof(DodekatheonRaidOrchestrator).Assembly.GetTypes().Where(t => t.Namespace != null && t.Namespace.Contains("NosAi.Raids.Dodekatheon")); bool hasExecution = types.Any(t => t.GetMethods().Any(m => m.Name.ToLowerInvariant().Contains("click") || m.Name.ToLowerInvariant().Contains("sendpacket"))); return !hasExecution; }
    }
    #endregion

    #region 7. Entry Point
    // The subsystem's own Program.Main used to live here. It was dead code: the
    // pinned StartupObject in the .csproj makes every other Main in the assembly
    // unreachable, which is why this suite had never run. It is reachable now
    // through the flag table in Program.cs; keeping a second entry point would
    // only suggest a way to run it that does not work.
    #endregion
}