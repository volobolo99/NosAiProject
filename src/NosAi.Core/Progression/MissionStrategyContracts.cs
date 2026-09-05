namespace NosAi.Core.Progression;

public enum MissionStrategyKind
{
    MainQuest,
    TimeSpace,
    TimeSpaceOptimization,
    SpecialistMission,
    Farm,
    Acquire,
    Hybrid
}

public sealed record MissionObjective(
    string Id,
    MissionStrategyKind Kind,
    string Name,
    string? RulesetVersion = null,
    int RequiredLevel = 0,
    int RequiredJobLevel = 0,
    IReadOnlySet<string>? Prerequisites = null,
    double ExpectedRewardValue = 0,
    string? Notes = null);

public sealed record MissionStrategyCandidate(
    string Id,
    string ObjectiveId,
    MissionStrategyKind Kind,
    string Name,
    double EstimatedTravelSeconds,
    double EstimatedPreparationSeconds,
    double EstimatedExecutionSeconds,
    double EstimatedRecoverySeconds,
    double ExpectedRetryCount,
    double RetryCostSeconds,
    double ResourceCost,
    double SuccessProbability,
    double Confidence,
    bool PreconditionsSatisfied,
    bool HumanPlausible,
    bool UsesOnlyPermittedObservation,
    string? RulesetVersion = null,
    IReadOnlyDictionary<string, string>? Evidence = null)
{
    public double ExpectedTimeToGoalSeconds
    {
        get
        {
            var probability = Math.Clamp(SuccessProbability, 0.01, 1.0);
            var attemptSeconds = EstimatedTravelSeconds + EstimatedPreparationSeconds + EstimatedExecutionSeconds + EstimatedRecoverySeconds;
            return attemptSeconds / probability + Math.Max(0, ExpectedRetryCount) * Math.Max(0, RetryCostSeconds);
        }
    }

    public bool IsExecutable => PreconditionsSatisfied && HumanPlausible && UsesOnlyPermittedObservation && SuccessProbability > 0;
}

public sealed record MissionStrategyScore(
    MissionStrategyCandidate Candidate,
    double ExpectedTimeToGoalSeconds,
    double ResourceCost,
    double Confidence,
    double CompositeScore);

public interface IMissionStrategyOptimizer
{
    MissionStrategyScore? SelectBest(
        MissionObjective objective,
        IReadOnlyList<MissionStrategyCandidate> candidates);
}

public sealed class DeterministicMissionStrategyOptimizer : IMissionStrategyOptimizer
{
    public MissionStrategyScore? SelectBest(
        MissionObjective objective,
        IReadOnlyList<MissionStrategyCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(objective);
        ArgumentNullException.ThrowIfNull(candidates);

        var compatibleRuleset = objective.RulesetVersion;
        var ranked = candidates
            .Where(c => c.ObjectiveId == objective.Id)
            .Where(c => c.IsExecutable)
            .Where(c => compatibleRuleset is null || string.Equals(c.RulesetVersion, compatibleRuleset, StringComparison.OrdinalIgnoreCase))
            .Select(c => Score(c))
            .OrderByDescending(s => s.CompositeScore)
            .ThenBy(s => s.ExpectedTimeToGoalSeconds)
            .ThenByDescending(s => s.Confidence)
            .ThenBy(s => s.Candidate.Id, StringComparer.Ordinal)
            .ToList();

        return ranked.FirstOrDefault();
    }

    private static MissionStrategyScore Score(MissionStrategyCandidate candidate)
    {
        var time = Math.Max(0.001, candidate.ExpectedTimeToGoalSeconds);
        var resource = Math.Max(0, candidate.ResourceCost);
        var confidence = Math.Clamp(candidate.Confidence, 0, 1);
        var reward = candidate.Evidence is not null && candidate.Evidence.TryGetValue("reward_value", out var rawReward)
            && double.TryParse(rawReward, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsedReward)
            ? Math.Max(0, parsedReward)
            : 0;

        var rewardFactor = reward > 0 ? 1 + Math.Min(2, reward / 1000.0) : 1;
        var efficiency = rewardFactor / (time + resource * 2);
        var composite = efficiency * (0.5 + confidence * 0.5);
        return new MissionStrategyScore(candidate, time, resource, confidence, composite);
    }
}
