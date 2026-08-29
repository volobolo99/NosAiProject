// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Versione: 1.0 Beta
// Autore: Volodymyr Ryzhuk
// Descrizione: Implementazione del Gate 4 (Progression Engine V2, DAG Missioni,
//              Sblocco SP, Aggiornamenti Bayesiani Beta-Binomiali, UCB1/MAUT
//              e Knowledge Base Strategica con Ciclo di Vita Mastery)
// Standard: C# 12 / .NET 8 — Zero-Allocation, Determinismo, Clean Code
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NosAi.Runtime.Gate4
{
    public enum GoalType : byte { MainQuest = 0, TimeSpace = 1, SpecialistCardUnlock = 2, ResourceFarming = 3, LevelingGrind = 4, EquipmentUpgrade = 5 }
    public enum StrategyLifecycleStatus : byte { Candidate = 0, Evaluating = 1, Verified = 2, Mastered = 3, Deprecated = 4 }
    public enum SpecialistCardType : byte
    {
        None = 0, SP1_Warrior_Ranger_RedMage = 1, SP2_Blade_Assassin_HolyMage = 2,
        SP3_Crusader_Destroyer_BlueMage = 3, SP4_Berserker_WildKeeper_DarkGunner = 4,
        SP5_Gladiator_Cannoneer_Volcano = 5, SP6_BattleMonk_Scout_TideLord = 6,
        SP7_DeathReaper_DemonHunter_Seer = 7, SP8_Renegade_AvengingAngel_Archmage = 8
    }

    public sealed record ResourceInventory(long Gold, int AngelFeathers, int FullMoonCrystals, int SoulGems, int GillionStones, int NormalPotionsCount, int BigPotionsCount);
    public sealed record CharacterProgressionProfile(long CharacterId, string Name, int CombatLevel, int JobLevel, int CurrentAct, int CurrentChapter, ResourceInventory Inventory, ImmutableHashSet<SpecialistCardType> UnlockedCards, ImmutableHashSet<string> CompletedQuestIds);
    public sealed record QuestDependencyNode(string QuestId, string Title, GoalType Type, int RequiredCombatLevel, int RequiredJobLevel, ImmutableArray<string> PrerequisiteQuestIds, ImmutableDictionary<string, int> RequiredItems, long RequiredGold, SpecialistCardType UnlocksSpecialist, int EstimatedDurationMinutes, float BaseDifficulty);

    public sealed record BetaBinomialEvidence(double Alpha, double Beta, int TotalTrials)
    {
        public static BetaBinomialEvidence CreateUniformPrior() => new(1.0, 1.0, 0);
        public double ExpectedSuccessRate => Alpha / (Alpha + Beta);
        public double Variance => (Alpha * Beta) / (Math.Pow(Alpha + Beta, 2) * (Alpha + Beta + 1));
        public float ConfidenceScore
        {
            get
            {
                if (TotalTrials == 0) return 0.0f;
                double confidence = 1.0 - Math.Exp(-0.15 * TotalTrials);
                return (float)Math.Clamp(confidence, 0.0, 1.0);
            }
        }
        public BetaBinomialEvidence RecordTrial(bool isSuccess) => new(isSuccess ? Alpha + 1.0 : Alpha, isSuccess ? Beta : Beta + 1.0, TotalTrials + 1);
    }

    public sealed record StrategyRecord(Guid StrategyId, string Name, GoalType TargetGoalType, string TargetKey, BetaBinomialEvidence Evidence, StrategyLifecycleStatus Status, int EstimatedDurationMs, long EstimatedResourceCostGold, float MasteryScore, DateTime LastEvaluatedUtc);

    public sealed class Ucb1StrategySelector
    {
        private readonly double _explorationWeight;
        public Ucb1StrategySelector(double explorationWeight = 1.41421356) => _explorationWeight = explorationWeight;
        public StrategyRecord SelectBestStrategy(IReadOnlyList<StrategyRecord> candidateStrategies, int totalGlobalDecisions, CharacterProgressionProfile profile)
        {
            if (candidateStrategies == null || candidateStrategies.Count == 0) throw new ArgumentException("Nessuna strategia disponibile per la selezione.", nameof(candidateStrategies));
            if (candidateStrategies.Count == 1) return candidateStrategies[0];
            StrategyRecord bestStrategy = candidateStrategies[0]; double maxUcbScore = double.MinValue;
            foreach (var strategy in candidateStrategies)
            {
                double baseUtility = CalculateMautUtility(strategy, profile);
                double explorationTerm = strategy.Evidence.TotalTrials > 0 && totalGlobalDecisions > 0
                    ? _explorationWeight * Math.Sqrt(Math.Log(totalGlobalDecisions) / strategy.Evidence.TotalTrials)
                    : 2.0;
                double finalScore = baseUtility + explorationTerm;
                if (finalScore > maxUcbScore) { maxUcbScore = finalScore; bestStrategy = strategy; }
            }
            return bestStrategy;
        }
        private double CalculateMautUtility(StrategyRecord strategy, CharacterProgressionProfile profile)
        {
            double successRate = strategy.Evidence.ExpectedSuccessRate;
            double timeScore = Math.Clamp(1.0 - (strategy.EstimatedDurationMs / 600000.0), 0.0, 1.0);
            double costScore = profile.Inventory.Gold >= strategy.EstimatedResourceCostGold ? 1.0 : 0.2;
            double confScore = strategy.Evidence.ConfidenceScore;
            return (0.45 * successRate) + (0.25 * timeScore) + (0.15 * costScore) + (0.15 * confScore);
        }
    }

    public sealed class KnowledgeBaseManager
    {
        private readonly ConcurrentDictionary<Guid, StrategyRecord> _strategies = new();
        private readonly object _mutationLock = new();
        public IReadOnlyCollection<StrategyRecord> GetAllStrategies() => _strategies.Values.ToList();
        public void RegisterStrategy(StrategyRecord strategy) => _strategies.TryAdd(strategy.StrategyId, strategy);
        public StrategyRecord UpdateStrategyEvidence(Guid strategyId, bool trialSuccess)
        {
            lock (_mutationLock)
            {
                if (!_strategies.TryGetValue(strategyId, out var existing)) throw new KeyNotFoundException($"Strategia {strategyId} non trovata nella Knowledge Base.");
                var updatedEvidence = existing.Evidence.RecordTrial(trialSuccess);
                float mastery = (float)((updatedEvidence.ExpectedSuccessRate * 0.70) + (updatedEvidence.ConfidenceScore * 0.30));
                StrategyLifecycleStatus newStatus;
                if (updatedEvidence.TotalTrials >= 5 && mastery >= 0.90f && updatedEvidence.ExpectedSuccessRate >= 0.92) newStatus = StrategyLifecycleStatus.Mastered;
                else if (updatedEvidence.TotalTrials >= 3 && updatedEvidence.ExpectedSuccessRate >= 0.70) newStatus = StrategyLifecycleStatus.Verified;
                else if (updatedEvidence.TotalTrials >= 5 && updatedEvidence.ExpectedSuccessRate < 0.40) newStatus = StrategyLifecycleStatus.Deprecated;
                else newStatus = StrategyLifecycleStatus.Evaluating;
                var updated = existing with { Evidence = updatedEvidence, MasteryScore = mastery, Status = newStatus, LastEvaluatedUtc = DateTime.UtcNow };
                _strategies[strategyId] = updated;
                return updated;
            }
        }
    }

    public sealed class ProgressionEngineV2
    {
        private readonly Dictionary<string, QuestDependencyNode> _questDag = new();
        private readonly KnowledgeBaseManager _knowledgeBase;
        private readonly Ucb1StrategySelector _selector;
        private int _totalDecisionsCount;
        public ProgressionEngineV2(KnowledgeBaseManager knowledgeBase) { _knowledgeBase = knowledgeBase; _selector = new Ucb1StrategySelector(); InitializeStandardNosTaleQuests(); }
        private void InitializeStandardNosTaleQuests()
        {
            AddQuest(new QuestDependencyNode("ACT1_Q1_NOSVILLE_START", "Atto 1-1: Il mistero dei Kovolt", GoalType.MainQuest, 1, 1, ImmutableArray<string>.Empty, ImmutableDictionary<string, int>.Empty, 0, SpecialistCardType.None, 5, 0.1f));
            AddQuest(new QuestDependencyNode("ACT1_Q2_TS_12", "Atto 1-2: Pietra Spazio-Tempo 12", GoalType.TimeSpace, 12, 5, ImmutableArray.Create("ACT1_Q1_NOSVILLE_START"), ImmutableDictionary.CreateRange(new[] { new KeyValuePair<string, int>("GillionStone", 2) }), 500, SpecialistCardType.None, 8, 0.3f));
            AddQuest(new QuestDependencyNode("SP1_QUEST_UNLOCK", "Gemma dell'Anima Misteriosa: Sblocco SP1", GoalType.SpecialistCardUnlock, 36, 20, ImmutableArray.Create("ACT1_Q2_TS_12"), ImmutableDictionary.CreateRange(new[] { new KeyValuePair<string, int>("AngelFeathers", 5), new KeyValuePair<string, int>("SoulGems", 1) }), 15000, SpecialistCardType.SP1_Warrior_Ranger_RedMage, 25, 0.65f));
            AddQuest(new QuestDependencyNode("SP2_QUEST_UNLOCK", "Santuario Sacro: Sblocco SP2", GoalType.SpecialistCardUnlock, 46, 35, ImmutableArray.Create("SP1_QUEST_UNLOCK"), ImmutableDictionary.CreateRange(new[] { new KeyValuePair<string, int>("AngelFeathers", 15), new KeyValuePair<string, int>("FullMoonCrystals", 3) }), 50000, SpecialistCardType.SP2_Blade_Assassin_HolyMage, 40, 0.80f));
        }
        public void AddQuest(QuestDependencyNode node) => _questDag[node.QuestId] = node;
        public IReadOnlyList<QuestDependencyNode> GetAvailableQuests(CharacterProgressionProfile profile)
        {
            var available = new List<QuestDependencyNode>();
            foreach (var node in _questDag.Values)
            {
                if (profile.CompletedQuestIds.Contains(node.QuestId)) continue;
                if (profile.CombatLevel < node.RequiredCombatLevel || profile.JobLevel < node.RequiredJobLevel) continue;
                if (!node.PrerequisiteQuestIds.All(id => profile.CompletedQuestIds.Contains(id))) continue;
                if (profile.Inventory.Gold < node.RequiredGold) continue;
                available.Add(node);
            }
            return available;
        }
        public (QuestDependencyNode SelectedQuest, StrategyRecord SelectedStrategy) PlanNextProgressionStep(CharacterProgressionProfile profile)
        {
            var available = GetAvailableQuests(profile);
            if (available.Count == 0) throw new InvalidOperationException("Nessuna missione accessibile nel DAG: prerequisiti o livelli insufficienti.");
            var prioritizedQuest = available.OrderByDescending(q => q.Type == GoalType.SpecialistCardUnlock ? 3 : q.Type == GoalType.TimeSpace ? 2 : 1).ThenBy(q => q.RequiredCombatLevel).First();
            var candidateStrategies = _knowledgeBase.GetAllStrategies().Where(s => s.TargetKey == prioritizedQuest.QuestId && s.Status != StrategyLifecycleStatus.Deprecated).ToList();
            if (candidateStrategies.Count == 0)
            {
                var newStrategy = new StrategyRecord(Guid.NewGuid(), $"Strategia Standard per {prioritizedQuest.Title}", prioritizedQuest.Type, prioritizedQuest.QuestId, BetaBinomialEvidence.CreateUniformPrior(), StrategyLifecycleStatus.Candidate, prioritizedQuest.EstimatedDurationMinutes * 60 * 1000, prioritizedQuest.RequiredGold, 0.5f, DateTime.UtcNow);
                _knowledgeBase.RegisterStrategy(newStrategy); candidateStrategies.Add(newStrategy);
            }
            Interlocked.Increment(ref _totalDecisionsCount);
            return (prioritizedQuest, _selector.SelectBestStrategy(candidateStrategies, _totalDecisionsCount, profile));
        }
    }

    // The supplied source terminates at Gate4TestRunner.RunAllTestsAsync().
    // The missing remainder is intentionally not fabricated during import.
}
