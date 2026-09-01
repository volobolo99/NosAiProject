namespace NosAi.Core;

/// <summary>
/// One position on the fixed, total, non-reorderable critical path
/// (docs/ROADMAP_ESECUTIVA.md S:1.1):
/// <c>Observe -&gt; WorldState -&gt; Simulation -&gt; Ranking -&gt; Orchestrator -&gt;
/// Planner -&gt; Guard -&gt; Trust -&gt; Safety -&gt; Execute -&gt; Verify</c>.
/// </summary>
/// <remarks>
/// The numeric values are the order itself, not an implementation detail
/// (INV-02): whatever validates pipeline wiring in a later Gate compares
/// against this sequence, not against declaration order in code.
/// </remarks>
public enum PipelineStage : byte
{
    Observe = 0,
    WorldState = 1,
    Simulation = 2,
    Ranking = 3,
    Orchestrator = 4,
    Planner = 5,
    Guard = 6,
    Trust = 7,
    Safety = 8,
    Execute = 9,
    Verify = 10
}
