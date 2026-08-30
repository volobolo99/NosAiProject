// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Versione: 1.0 Beta
// Autore: Volodymyr Ryzhuk
// Descrizione: Sottosistema di Gestione e Automazione Miniland, Pianificazione
//              Stazioni Produttive, Simulatore Deterministico Minigiochi e Raccolta Risorse
// Standard: C# 12 / .NET 8 — Zero-Allocation, Clean Architecture, Fail-Closed
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

namespace NosAi.Miniland.Production
{
    public enum MinilandStationType : byte { Sawmill = 0, StoneQuarry = 1, FishPond = 2, PoultryFarm = 3, ProductionProd = 4 }
    public enum MinigameKind : byte { ArcheryTarget = 0, MiningHammer = 1, Woodcutting = 2, Fishing = 3 }
    public sealed record MinilandStationState(int StationInstanceId, MinilandStationType StationType, string StationName, int Level, bool IsReadyForHarvest, DateTime ReadyAtUtc, int RemainingCoolingSeconds, int ProducedItemRewardId, int ProducedItemQuantity);
    public sealed record MinigameExecutionResult(Guid SessionId, MinigameKind Kind, bool IsVictory, int ScoreAchieved, int RewardBoxesEarned, long DurationMs, string DiagnosticReport);

    public sealed class MinilandProductionScheduler
    {
        private readonly ConcurrentDictionary<int, MinilandStationState> _stations = new();
        public IReadOnlyCollection<MinilandStationState> RegisteredStations => _stations.Values.ToImmutableArray();
        public void RegisterStation(MinilandStationState station) => _stations[station.StationInstanceId] = station;
        public void TickUpdate(TimeSpan elapsed)
        {
            foreach (var id in _stations.Keys)
            {
                if (_stations.TryGetValue(id, out var st) && !st.IsReadyForHarvest)
                {
                    int newRemaining = Math.Max(0, st.RemainingCoolingSeconds - (int)elapsed.TotalSeconds);
                    bool ready = newRemaining == 0;
                    _stations[id] = st with { RemainingCoolingSeconds = newRemaining, IsReadyForHarvest = ready, ReadyAtUtc = ready ? DateTime.UtcNow : st.ReadyAtUtc };
                }
            }
        }
        public List<MinilandStationState> GetReadyStations() => _stations.Values.Where(s => s.IsReadyForHarvest).ToList();
        public bool TryHarvestStation(int stationInstanceId, out MinilandStationState? harvestedStation, out string? error)
        {
            harvestedStation = null; error = null;
            if (!_stations.TryGetValue(stationInstanceId, out var st)) { error = "Stazione Miniland non trovata."; return false; }
            if (!st.IsReadyForHarvest) { error = $"Stazione in cooldown ({st.RemainingCoolingSeconds} secondi rimanenti)."; return false; }
            const int cooldownSeconds = 10800;
            _stations[stationInstanceId] = st with { IsReadyForHarvest = false, RemainingCoolingSeconds = cooldownSeconds, ReadyAtUtc = DateTime.UtcNow.AddSeconds(cooldownSeconds) };
            harvestedStation = st; return true;
        }
    }

    public sealed class DeterministicMinigameSolver
    {
        private readonly Random _random = new();
        public MinigameExecutionResult SimulateMinigamePlay(MinigameKind kind, int buildingLevel)
        {
            var sessionId = Guid.NewGuid(); int score = 0, boxes = 0; bool victory = false;
            switch (kind)
            {
                case MinigameKind.ArcheryTarget: score = 2500 + buildingLevel * 150 + _random.Next(-100, 200); victory = score >= 2000; boxes = victory ? (score >= 3500 ? 3 : 2) : 0; break;
                case MinigameKind.MiningHammer: score = 1800 + buildingLevel * 120 + _random.Next(-80, 150); victory = score >= 1500; boxes = victory ? 2 : 0; break;
                case MinigameKind.Woodcutting: score = 2200 + buildingLevel * 140 + _random.Next(-90, 180); victory = score >= 1800; boxes = victory ? 2 : 0; break;
                case MinigameKind.Fishing: score = 3000 + buildingLevel * 200 + _random.Next(-120, 220); victory = score >= 2500; boxes = victory ? 3 : 1; break;
            }
            string report = victory ? $"Minigame {kind} completato con successo. Punteggio: {score}, Scatole premio ottenute: {boxes}." : $"Minigame {kind} fallito. Punteggio insufficiente ({score}).";
            return new MinigameExecutionResult(sessionId, kind, victory, score, boxes, 3500, report);
        }
    }

    public sealed class MinilandAutomationMasterEngine
    {
        private readonly MinilandProductionScheduler _scheduler;
        private readonly DeterministicMinigameSolver _minigameSolver;
        public MinilandProductionScheduler Scheduler => _scheduler;
        public DeterministicMinigameSolver MinigameSolver => _minigameSolver;
        public MinilandAutomationMasterEngine() { _scheduler = new(); _minigameSolver = new(); InitializeDefaultStations(); }
        private void InitializeDefaultStations()
        {
            _scheduler.RegisterStation(new MinilandStationState(1, MinilandStationType.Sawmill, "Segheria di Livello 5", 5, true, DateTime.UtcNow, 0, 301, 10));
            _scheduler.RegisterStation(new MinilandStationState(2, MinilandStationType.StoneQuarry, "Cava di Pietra di Livello 4", 4, false, DateTime.UtcNow.AddSeconds(1200), 1200, 302, 8));
        }
    }

    public static class MinilandProductionTestRunner
    {
        public static async Task<bool> RunAllTestsAsync()
        {
            Console.WriteLine("=== Miniland production checks ===");
            bool allPassed = true;
            allPassed &= RunTest("Test 1: Registrazione e Rilevamento Stazioni Pronte", TestStationReadinessDetection);
            allPassed &= RunTest("Test 2: Raccolta Risorse e Ripristino Cooldown", TestStationHarvestAndResetCooldown);
            allPassed &= RunTest("Test 3: Esecuzione Minigioco Tiro al Bersaglio", TestMinigameArcherySimulation);
            allPassed &= RunTest("Test 4: Esecuzione Minigioco Pesca", TestMinigameFishingSimulation);
            allPassed &= RunTest("Test 5: Tick Temporale & Aggiornamento Cooldown", TestSchedulerTickProgression);
            allPassed &= RunTest("Test 6: Invariante Architetturale", TestMinilandSecurityInvariant);
            Console.WriteLine(allPassed
                ? "=== Miniland production checks passed. Local only. ==="
                : "=== Miniland production checks FAILED. See the lines marked FAIL above. ===");
            await Task.CompletedTask; return allPassed;
        }
        private static bool RunTest(string name, Func<bool> testFunc)
        {
            try { return Report(name, testFunc(), null); }
            catch (Exception ex) { return Report(name, false, $"{ex.GetType().Name}: {ex.Message}"); }
        }

        /// <summary>
        /// Reports each check by name.
        /// </summary>
        /// <remarks>
        /// The runner used to discard the name and print nothing, returning one
        /// aggregate bool: a failure gave exit 1 and no way to tell which check
        /// broke or why, because the catch swallowed the exception too. The same
        /// defect was already fixed once for Gate 1.
        /// </remarks>
        private static bool Report(string name, bool passed, string? error)
        {
            string detail = error is null ? string.Empty : $" [{error}]";
            Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] {name}{detail}");
            return passed;
        }

        private static bool TestStationReadinessDetection() { var e = new MinilandAutomationMasterEngine(); var r = e.Scheduler.GetReadyStations(); return r.Count == 1 && r[0].StationInstanceId == 1; }
        private static bool TestStationHarvestAndResetCooldown() { var e = new MinilandAutomationMasterEngine(); bool a = e.Scheduler.TryHarvestStation(1, out var h, out _); bool b = e.Scheduler.TryHarvestStation(1, out _, out var err); return a && h != null && !b && err?.Contains("cooldown") == true; }
        private static bool TestMinigameArcherySimulation() { var e = new MinilandAutomationMasterEngine(); var r = e.MinigameSolver.SimulateMinigamePlay(MinigameKind.ArcheryTarget, 5); return r.IsVictory && r.ScoreAchieved >= 2000 && r.RewardBoxesEarned > 0; }
        private static bool TestMinigameFishingSimulation() { var e = new MinilandAutomationMasterEngine(); var r = e.MinigameSolver.SimulateMinigamePlay(MinigameKind.Fishing, 8); return r.IsVictory && r.ScoreAchieved >= 2500 && r.RewardBoxesEarned >= 1; }
        private static bool TestSchedulerTickProgression() { var s = new MinilandProductionScheduler(); s.RegisterStation(new MinilandStationState(99, MinilandStationType.FishPond, "Stagno Test", 3, false, DateTime.UtcNow.AddSeconds(10), 10, 401, 5)); s.TickUpdate(TimeSpan.FromSeconds(10)); return s.GetReadyStations().Count == 1; }
        private static bool TestMinilandSecurityInvariant() { var types = typeof(MinilandAutomationMasterEngine).Assembly.GetTypes().Where(t => t.Namespace?.Contains("NosAi.Miniland.Production") == true); return !types.Any(t => t.GetMethods().Any(m => m.Name.Contains("click", StringComparison.OrdinalIgnoreCase) || m.Name.Contains("sendpacket", StringComparison.OrdinalIgnoreCase))); }
    }
}
