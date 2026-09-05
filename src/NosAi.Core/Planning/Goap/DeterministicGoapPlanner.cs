namespace NosAi.Core.Planning.Goap;

/// <summary>Bounded deterministic forward-search GOAP planner.</summary>
public sealed class DeterministicGoapPlanner
{
    private readonly IReadOnlyList<GoapAction> _actions;
    private readonly int _maxNodes;

    public DeterministicGoapPlanner(IReadOnlyList<GoapAction> actions, int maxNodes = 256)
    {
        ArgumentNullException.ThrowIfNull(actions);
        if (maxNodes <= 0) throw new ArgumentOutOfRangeException(nameof(maxNodes));
        _actions = actions;
        _maxNodes = maxNodes;
    }

    public bool TryPlan(
        ReadOnlySpan<GoapFact> initial,
        ReadOnlySpan<GoapFact> goal,
        Span<PlanStep> output,
        out int count,
        out FaultCode fault)
    {
        count = 0;
        fault = FaultCode.None;
        if (output.IsEmpty)
        {
            fault = FaultCode.Timeout;
            return false;
        }

        var states = new List<Node>(_maxNodes);
        states.Add(new Node(new FactState(initial), -1, 0));
        var expanded = 0;

        for (var i = 0; i < states.Count && expanded < _maxNodes; i++)
        {
            var current = states[i];
            if (current.State.Satisfies(goal))
            {
                var reverse = new List<int>();
                var nodeIndex = i;
                while (nodeIndex != 0)
                {
                    var node = states[nodeIndex];
                    reverse.Add(node.ActionIndex);
                    nodeIndex = node.ParentIndex;
                }

                if (reverse.Count > output.Length)
                {
                    fault = FaultCode.Timeout;
                    return false;
                }

                reverse.Reverse();
                for (var j = 0; j < reverse.Count; j++)
                    output[j] = _actions[reverse[j]].Step;

                count = reverse.Count;
                return true;
            }

            expanded++;
            for (var actionIndex = 0; actionIndex < _actions.Count; actionIndex++)
            {
                var action = _actions[actionIndex];
                if (!current.State.Satisfies(action.Preconditions.Span))
                    continue;

                var next = current.State.Apply(action.Effects.Span);
                var duplicate = false;
                for (var stateIndex = 0; stateIndex < states.Count; stateIndex++)
                {
                    if (!states[stateIndex].State.Equals(next))
                        continue;
                    duplicate = true;
                    break;
                }

                if (duplicate)
                    continue;

                states.Add(new Node(next, i, actionIndex));
                if (states.Count >= _maxNodes)
                    break;
            }
        }

        fault = FaultCode.Timeout;
        return false;
    }

    private sealed record Node(FactState State, int ParentIndex, int ActionIndex);

    private readonly struct FactState : IEquatable<FactState>
    {
        private readonly Dictionary<string, int> _facts;

        public FactState(ReadOnlySpan<GoapFact> facts)
        {
            _facts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var fact in facts)
                _facts[fact.Key] = fact.Value;
        }

        private FactState(Dictionary<string, int> facts) => _facts = facts;

        public bool Satisfies(ReadOnlySpan<GoapFact> facts)
        {
            foreach (var fact in facts)
            {
                if (!_facts.TryGetValue(fact.Key, out var value) || value != fact.Value)
                    return false;
            }

            return true;
        }

        public FactState Apply(ReadOnlySpan<GoapFact> effects)
        {
            var copy = new Dictionary<string, int>(_facts, StringComparer.Ordinal);
            foreach (var effect in effects)
                copy[effect.Key] = effect.Value;
            return new FactState(copy);
        }

        public bool Equals(FactState other)
        {
            if (_facts.Count != other._facts.Count)
                return false;

            foreach (var pair in _facts)
            {
                if (!other._facts.TryGetValue(pair.Key, out var value) || value != pair.Value)
                    return false;
            }

            return true;
        }

        public override bool Equals(object? obj) => obj is FactState other && Equals(other);

        public override int GetHashCode()
        {
            var keys = _facts.Keys.ToArray();
            Array.Sort(keys, StringComparer.Ordinal);

            var hash = new HashCode();
            foreach (var key in keys)
            {
                hash.Add(key, StringComparer.Ordinal);
                hash.Add(_facts[key]);
            }

            return hash.ToHashCode();
        }
    }
}
