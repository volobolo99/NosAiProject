using NosAi.Core.Knowledge;

namespace NosAi.Core.Progression;

/// <summary>
/// Converts validated/verified strategy memory into planner candidates without granting execution authority.
/// All safety-sensitive capabilities default to false and must be explicitly evidenced by the source record.
/// </summary>
public sealed class KnowledgeMissionStrategyAdapter
{
    private readonly IStrategyMemory _strategyMemory;

    public KnowledgeMissionStrategyAdapter(IStrategyMemory strategyMemory)
    {
        _strategyMemory = strategyMemory ?? throw new ArgumentNullException(nameof(strategyMemory));
    }

    public async ValueTask<IReadOnlyList<MissionStrategyCandidate>> GetCandidatesAsync(
        MissionObjective objective,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(objective);

        var items = await _strategyMemory.QueryAsync(objective.Id, objective.RulesetVersion, cancellationToken);
        var candidates = new List<MissionStrategyCandidate>(items.Count);

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryCreate(objective, item, out var candidate))
                continue;
            candidates.Add(candidate);
        }

        return candidates;
    }

    private static bool TryCreate(
        MissionObjective objective,
        StrategyMemoryItem item,
        out MissionStrategyCandidate candidate)
    {
        candidate = default!;

        if (!string.Equals(item.Objective, objective.Id, StringComparison.Ordinal))
            return false;

        if (!Enum.TryParse<MissionStrategyKind>(Get(item.Conditions, "strategy_kind"), true, out var kind))
            return false;

        var humanPlausible = GetBoolean(item.Conditions, "human_plausible");
        var permittedObservation = GetBoolean(item.Conditions, "ordinary_client_only")
            && GetBoolean(item.Conditions, "permitted_observation");
        var preconditions = GetBoolean(item.Conditions, "preconditions_satisfied");

        candidate = new MissionStrategyCandidate(
            item.KnowledgeId,
            objective.Id,
            kind,
            string.IsNullOrWhiteSpace(item.Strategy) ? item.Topic : item.Strategy,
            GetNonNegative(item.Conditions, "travel_seconds"),
            GetNonNegative(item.Conditions, "preparation_seconds"),
            GetNonNegative(item.Conditions, "execution_seconds"),
            GetNonNegative(item.Conditions, "recovery_seconds"),
            GetNonNegative(item.Conditions, "retry_count"),
            GetNonNegative(item.Conditions, "retry_cost_seconds"),
            GetNonNegative(item.Conditions, "resource_cost"),
            GetProbability(item.Conditions, "success_probability", item.Confidence),
            Math.Clamp(item.Confidence, 0, 1),
            preconditions,
            humanPlausible,
            permittedObservation,
            item.RulesetVersion,
            item.Conditions);

        return true;
    }

    private static string Get(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) ? value : string.Empty;

    private static bool GetBoolean(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) && parsed;

    private static double GetNonNegative(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var value) ||
            !double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            return 0;

        return double.IsFinite(parsed) ? Math.Max(0, parsed) : 0;
    }

    private static double GetProbability(
        IReadOnlyDictionary<string, string> values,
        string key,
        double fallback)
    {
        var parsed = GetNonNegative(values, key);
        if (!values.ContainsKey(key))
            return Math.Clamp(fallback, 0, 1);
        return Math.Clamp(parsed, 0, 1);
    }
}
