// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Versione: 1.0 Beta
// AI — Motore decisionale a utilità e caricamento regole dal volume dedicato
// ============================================================================

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using NosAi.Runtime.Contracts;

namespace NosAi.Runtime.AI.Decision;

/// <summary>
/// Deterministic utility-based decision engine over an unbounded rule set.
/// </summary>
/// <remarks>
/// <para>
/// Selection is by priority class first, then utility, then rule id. The id
/// tie-break makes the engine fully deterministic: two rules with equal priority
/// and utility always resolve the same way, so a decision is replayable from the
/// same facts — which is what makes an audit trail worth keeping.
/// </para>
/// <para>
/// A rule whose facts were not observed is skipped and reported as
/// <see cref="RuleSkipReason.FactNotObserved"/>, never evaluated as false. That
/// distinction is the whole point: acting because HP is "0" when HP is actually
/// unknown is how an automation kills a character.
/// </para>
/// </remarks>
public sealed class UtilityDecisionEngine
{
    private readonly List<DecisionRule> _rules;

    public int RuleCount => _rules.Count;

    /// <summary>Rules in evaluation order (highest priority, then utility).</summary>
    public IReadOnlyList<DecisionRule> Rules => _rules;

    public UtilityDecisionEngine(IEnumerable<DecisionRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = new List<DecisionRule>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (DecisionRule rule in rules)
        {
            rule.Validate();
            if (!seen.Add(rule.Id))
                throw new ArgumentException($"Duplicate rule id '{rule.Id}': ids must be unique to stay auditable.");
            _rules.Add(rule);
        }
        _rules.Sort(Compare);
    }

    private static int Compare(DecisionRule a, DecisionRule b)
    {
        int byPriority = b.Priority.CompareTo(a.Priority);
        if (byPriority != 0) return byPriority;
        int byUtility = b.Utility.CompareTo(a.Utility);
        if (byUtility != 0) return byUtility;
        return string.CompareOrdinal(a.Id, b.Id);
    }

    /// <summary>Evaluates every rule against the context and returns the winner.</summary>
    public DecisionOutcome Decide(DecisionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var skipped = ImmutableArray.CreateBuilder<SkippedRule>();
        foreach (DecisionRule rule in _rules)
        {
            bool fired = true;
            // A decision is only as trustworthy as its weakest input, so the
            // provenance is folded across every fact the rule actually reads.
            DataSourceKind weakest = DataSourceKind.Live;
            string? unobservedFact = null;

            foreach (RuleCondition condition in rule.Conditions)
            {
                bool holds = condition.Evaluate(context, out bool evaluable, out DataSourceKind source);
                if (!evaluable)
                {
                    unobservedFact = condition.Fact;
                    fired = false;
                    break;
                }
                weakest = Weaker(weakest, source);
                if (!holds)
                {
                    fired = false;
                    break;
                }
            }

            if (!fired)
            {
                skipped.Add(unobservedFact is null
                    ? new SkippedRule(rule.Id, RuleSkipReason.ConditionFalse, null)
                    : new SkippedRule(rule.Id, RuleSkipReason.FactNotObserved, unobservedFact));
                continue;
            }

            // Rules are pre-sorted, so the first that fires is the best one.
            string rationale = rule.Rationale ?? $"rule '{rule.Id}' fired";
            return new DecisionOutcome(true, rule.Action, rule.Id, rule.Utility, rule.Priority,
                rule.Conditions.IsEmpty ? DataSourceKind.Derived : weakest,
                rationale, skipped.ToImmutable());
        }

        return DecisionOutcome.None(
            _rules.Count == 0 ? "no rules loaded" : "no rule fired on the observed facts",
            skipped.ToImmutable());
    }

    private static DataSourceKind Weaker(DataSourceKind a, DataSourceKind b)
    {
        // Ordering by trust: Live > Derived > Cached > Simulated > Unknown.
        static int Rank(DataSourceKind kind) => kind switch
        {
            DataSourceKind.Live => 4,
            DataSourceKind.Derived => 3,
            DataSourceKind.Cached => 2,
            DataSourceKind.Simulated => 1,
            _ => 0,
        };
        return Rank(a) <= Rank(b) ? a : b;
    }
}

/// <summary>Wire shape of a rule file. Kept separate so the domain type stays clean.</summary>
internal sealed class RuleFileEntry
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("action")] public string? Action { get; set; }
    [JsonPropertyName("utility")] public double Utility { get; set; }
    [JsonPropertyName("priority")] public int Priority { get; set; }
    [JsonPropertyName("rationale")] public string? Rationale { get; set; }
    [JsonPropertyName("when")] public List<RuleFileCondition>? When { get; set; }
}

internal sealed class RuleFileCondition
{
    [JsonPropertyName("fact")] public string? Fact { get; set; }
    [JsonPropertyName("op")] public string? Op { get; set; }
    [JsonPropertyName("value")] public double Value { get; set; }
}

/// <summary>
/// Loads rule sets from the dedicated volume.
/// </summary>
/// <remarks>
/// Rules live on the NOSAI-SSD volume as data, not in the binary: the set can
/// grow without limit and be retuned without a rebuild. Loading is strict — a
/// malformed file is refused with the offending entry named, because a rule set
/// that silently loses half its rules is worse than one that fails to load.
/// </remarks>
public static class DecisionRuleLoader
{
    /// <summary>Default location under the runtime data root.</summary>
    public const string DefaultRelativePath = "config/decision_rules.json";

    private static readonly IReadOnlyDictionary<string, ConditionOperator> Operators =
        new Dictionary<string, ConditionOperator>(StringComparer.OrdinalIgnoreCase)
        {
            ["<"] = ConditionOperator.LessThan,
            ["lt"] = ConditionOperator.LessThan,
            ["<="] = ConditionOperator.LessOrEqual,
            ["lte"] = ConditionOperator.LessOrEqual,
            [">"] = ConditionOperator.GreaterThan,
            ["gt"] = ConditionOperator.GreaterThan,
            [">="] = ConditionOperator.GreaterOrEqual,
            ["gte"] = ConditionOperator.GreaterOrEqual,
            ["=="] = ConditionOperator.Equal,
            ["eq"] = ConditionOperator.Equal,
            ["!="] = ConditionOperator.NotEqual,
            ["ne"] = ConditionOperator.NotEqual,
        };

    public static ImmutableArray<DecisionRule> Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        List<RuleFileEntry>? entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<RuleFileEntry>>(json,
                new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The rule file is not valid JSON: {ex.Message}", ex);
        }

        if (entries is null) throw new InvalidDataException("The rule file is empty.");

        var rules = ImmutableArray.CreateBuilder<DecisionRule>(entries.Count);
        for (int i = 0; i < entries.Count; i++)
        {
            RuleFileEntry entry = entries[i];
            string id = entry.Id ?? throw new InvalidDataException($"Rule #{i} has no id.");
            string action = entry.Action ?? throw new InvalidDataException($"Rule '{id}' has no action.");

            var conditions = ImmutableArray.CreateBuilder<RuleCondition>();
            foreach (RuleFileCondition condition in entry.When ?? new List<RuleFileCondition>())
            {
                string fact = condition.Fact ?? throw new InvalidDataException($"Rule '{id}' has a condition with no fact.");
                string op = condition.Op ?? throw new InvalidDataException($"Rule '{id}' has a condition with no operator.");
                if (!Operators.TryGetValue(op, out ConditionOperator parsed))
                    throw new InvalidDataException($"Rule '{id}' uses an unknown operator '{op}'.");
                conditions.Add(new RuleCondition(fact, parsed, condition.Value));
            }

            var rule = new DecisionRule(id, action, conditions.ToImmutable(), entry.Utility, entry.Priority, entry.Rationale);
            rule.Validate();
            rules.Add(rule);
        }
        return rules.ToImmutable();
    }

    /// <summary>
    /// Loads from disk. Returns false with a named reason when the file is
    /// missing or malformed: the runtime must be able to report "no rule set"
    /// rather than start on an empty one it believes is complete.
    /// </summary>
    public static bool TryLoadFile(string path, out ImmutableArray<DecisionRule> rules, out string? failure)
    {
        rules = ImmutableArray<DecisionRule>.Empty;
        failure = null;
        try
        {
            if (!File.Exists(path))
            {
                failure = $"rule_file_not_found:{path}";
                return false;
            }
            rules = Parse(File.ReadAllText(path));
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            failure = $"rule_file_unreadable:{ex.Message}";
            return false;
        }
    }
}
