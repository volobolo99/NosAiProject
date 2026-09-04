namespace NosAi.Core.Planning;

public interface IRoutineNode
{
    bool Evaluate(in WorldState state);
}

public sealed class SequenceRoutine : IRoutineNode
{
    private readonly IReadOnlyList<IRoutineNode> _children;
    public SequenceRoutine(IReadOnlyList<IRoutineNode> children) => _children = children ?? throw new ArgumentNullException(nameof(children));
    public bool Evaluate(in WorldState state)
    {
        foreach (var child in _children) if (!child.Evaluate(state)) return false;
        return true;
    }
}

public sealed class SelectorRoutine : IRoutineNode
{
    private readonly IReadOnlyList<IRoutineNode> _children;
    public SelectorRoutine(IReadOnlyList<IRoutineNode> children) => _children = children ?? throw new ArgumentNullException(nameof(children));
    public bool Evaluate(in WorldState state)
    {
        foreach (var child in _children) if (child.Evaluate(state)) return true;
        return false;
    }
}
