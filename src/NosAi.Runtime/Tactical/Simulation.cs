using NosAi.Runtime.Contracts;
using NosAi.Runtime.WorldModel;

namespace NosAi.Runtime.Tactical;

public interface IActionSimulator
{
    double Evaluate(WorldState state, CandidateAction action);
}

/// <summary>Deterministic baseline simulator. It does not interact with the game.</summary>
public sealed class DeterministicActionSimulator : IActionSimulator
{
    public double Evaluate(WorldState state, CandidateAction action)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(action);
        return action.Kind switch
        {
            ActionKind.NoOp => 0.0,
            ActionKind.Recovery => 100.0 * (1.0 - state.PlayerHpRatio),
            _ => action.UtilityScore
        };
    }
}
