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
        if (output.IsEmpty) { fault = FaultCode.Timeout; return false; }

        var states = new List<Node>(_maxNodes);
        var root = new FactState(initial);
        states.Add(new Node(root, null, -1, 0));
        var expanded = 0;

        for (var i = 0; i < states.Count && expanded < _maxNodes; i++)
        {
            var current = states[i];
            if (current.State.Satisfies(goal))
            {
                var reverse = new List<int>();
                for (var n = i; n != 0; n = states[n].Parent) reverse.Add(states[n].ActionIndex);
                if (reverse.Count > output.Length) { fault = FaultCode.Timeout; return false; }
                reverse.Reverse();
                for (var j = 0; j < reverse.Count; j++) output[j] = _actions[reverse[j]].Step;
                count = reverse.Count;
                return true;
            }

            expanded++;
            for (var a = 0; a < _actions.Count; a++)
            {
                var action = _actions[a];
                if (!current.State.Satisfies(action.Preconditions.Span)) continue;
                var next = current.State.Apply(action.Effects.Span);
                if (states.Exists(n => n.State.Equals(next))) continue;
                states.Add(new Node(next, i, a, current.Cost + action.Cost));
                if (states.Count >= _maxNodes) break;
            }
        }

        fault = FaultCode.Timeout;
        return false;
    }

    private sealed record Node(FactState State, int? Parent, int ActionIndex, int Cost);

    private readonly struct FactState : IEquatable<FactState>
    {
        private readonly Dictionary<string, int> _facts;
        public FactState(ReadOnlySpan<GoapFact> facts) { _facts = facts.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal); }
        private FactState(Dictionary<string, int> facts) { _facts = facts; }
        public bool Satisfies(ReadOnlySpan<GoapFact> facts) => facts.ToArray().All(x => _facts.TryGetValue(x.Key, out var v) && v == x.Value);
        public FactState Apply(ReadOnlySpan<GoapFact> effects)
        {
            var copy = new Dictionary<string, int>(_facts, StringComparer.Ordinal);
            foreach (var effect in effects) copy[effect.Key] = effect.Value;
            return new FactState(copy);
        }
        public bool Equals(FactState other) => _facts.Count == other._facts.Count && _facts.All(x => other._facts.TryGetValue(x.Key, out var v) && v == x.Value);
        public override bool Equals(object? obj) => obj is FactState other && Equals(other);
        public override int GetHashCode() => _facts.OrderBy(x => x.Key, StringComparer.Ordinal).Aggregate(17, (h, x) => HashCode.Combine(h, x.Key, x.Value));
    }
}
