namespace NosAi.Runtime.Observability;

/// <summary>One stage's last known outcome, flattened for a dump or a panel.</summary>
/// <param name="Ok">
/// Null when that stage has never produced a result. Unknown, not a quiet false.
/// </param>
public sealed record StageOutcomeDump(string Stage, bool? Ok, string? Fault);

/// <summary>
/// The last outcome of each stage on the canonical critical path.
/// </summary>
/// <remarks>
/// A dump that omitted the stages would photograph the breaker and not the cycle
/// that drove it. Stages that have not run stay unnamed as unknown rather than
/// as a passing false: the halt is the moment the runtime stopped trusting itself,
/// and inventing successes for stages that never ran would be the opposite of that.
/// </remarks>
public sealed class PipelineStageBoard
{
    /// <summary>
    /// The canonical path, named rather than numbered so a dump is readable without
    /// the enum. The names match <c>NosAi.Core.PipelineStage</c>; this board does
    /// not take a project reference on Core just to spell them.
    /// </summary>
    public static readonly IReadOnlyList<string> Stages =
    [
        "Observe", "WorldState", "Simulation", "Ranking", "Orchestrator",
        "Planner", "Guard", "Trust", "Safety", "Execute", "Verify"
    ];

    public const string NeverRanFault = "stage_never_ran";

    private readonly object _lock = new();
    private readonly Dictionary<string, StageOutcomeDump> _last = new(StringComparer.Ordinal);

    /// <summary>Records one stage's outcome, replacing whatever it last said.</summary>
    public void Record(string stage, bool ok, string? fault = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        lock (_lock)
        {
            _last[stage] = new StageOutcomeDump(stage, ok, ok ? null : fault);
        }
    }

    /// <summary>Every canonical stage, unknown where nothing has run yet.</summary>
    public IReadOnlyList<StageOutcomeDump> Snapshot()
    {
        lock (_lock)
        {
            var list = new StageOutcomeDump[Stages.Count];
            for (int i = 0; i < Stages.Count; i++)
            {
                string name = Stages[i];
                list[i] = _last.TryGetValue(name, out StageOutcomeDump? dump)
                    ? dump
                    : new StageOutcomeDump(name, null, NeverRanFault);
            }

            return list;
        }
    }

    /// <summary>All stages unknown. What a dump carries when no cycle has run.</summary>
    public static IReadOnlyList<StageOutcomeDump> UnknownAll()
    {
        var list = new StageOutcomeDump[Stages.Count];
        for (int i = 0; i < Stages.Count; i++)
            list[i] = new StageOutcomeDump(Stages[i], null, NeverRanFault);
        return list;
    }
}

/// <summary>The last commit-point refusal, if any, at the moment of a halt.</summary>
public sealed record CommitPointRefusalDump(string Reason, DateTime? AtUtc);

/// <summary>The last session-authority verdict, flattened for a dump.</summary>
public sealed record SessionAuthorityDump(
    bool IsActuating,
    string? RefusalReason,
    bool IsTerminal,
    string RuntimeIntegrity,
    string ClientIntegrity,
    bool WasProbed);

/// <summary>Sources photographed at a halt transition, besides the breaker itself.</summary>
public sealed class HaltDiagnosticsContext
{
    public Func<CommitPointRefusalDump?> LastCommitPointRefusal { get; init; } = () => null;
    public Func<SessionAuthorityDump?> LastSessionAuthority { get; init; } = () => null;
    public Func<IReadOnlyList<StageOutcomeDump>> LastStageOutcomes { get; init; } = PipelineStageBoard.UnknownAll;
}
