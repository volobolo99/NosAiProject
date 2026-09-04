// SOURCE: luxkun/ReGoap
// UPSTREAM PATH: ReGoap/Core/ReGoapCondition.cs
// UPSTREAM REVISION (blob SHA): e2cca83565b10ffe0bf3f43ac963c766b253874a
// LICENSE: Apache-2.0
// STATUS: reference copy; NOT wired into NosAi runtime

using System;

namespace ReGoap.Core
{
    public enum ReGoapConditionOperator
    {
        Equal,
        NotEqual,
        GreaterOrEqual,
        LessOrEqual
    }

    public sealed class ReGoapCondition : IEquatable<ReGoapCondition>
    {
        public ReGoapConditionOperator Operator { get; }
        public object Value { get; }

        private ReGoapCondition(ReGoapConditionOperator op, object value)
        {
            Operator = op;
            Value = value;
        }

        public static ReGoapCondition Equal(object value) => new(ReGoapConditionOperator.Equal, value);
        public static ReGoapCondition NotEqual(object value) => new(ReGoapConditionOperator.NotEqual, value);
        public static ReGoapCondition GreaterOrEqual(object value) => new(ReGoapConditionOperator.GreaterOrEqual, value);
        public static ReGoapCondition LessOrEqual(object value) => new(ReGoapConditionOperator.LessOrEqual, value);

        public bool IsSatisfiedBy(object candidate) => IsMatch(this, candidate);

        public static bool IsMatch(object required, object candidate)
        {
            if (required is ReGoapCondition requiredCondition)
                return requiredCondition.IsSatisfiedByRaw(candidate);
            if (candidate is ReGoapCondition candidateCondition)
                return candidateCondition.IsSatisfiedByRaw(required);
            return Equals(required, candidate);
        }

        public static bool AreCompatible(object left, object right)
        {
            if (left is ReGoapCondition leftCondition && right is ReGoapCondition rightCondition)
                return leftCondition.IsCompatibleWith(rightCondition);
            if (left is ReGoapCondition leftOnly)
                return leftOnly.IsSatisfiedByRaw(right);
            if (right is ReGoapCondition rightOnly)
                return rightOnly.IsSatisfiedByRaw(left);
            return Equals(left, right);
        }

        private bool IsSatisfiedByRaw(object candidate)
        {
            return Operator switch
            {
                ReGoapConditionOperator.Equal => Equals(candidate, Value),
                ReGoapConditionOperator.NotEqual => !Equals(candidate, Value),
                ReGoapConditionOperator.GreaterOrEqual => Compare(candidate, Value, out var ge) && ge >= 0,
                ReGoapConditionOperator.LessOrEqual => Compare(candidate, Value, out var le) && le <= 0,
                _ => false
            };
        }

        private bool IsCompatibleWith(ReGoapCondition other)
        {
            if (Operator == ReGoapConditionOperator.Equal)
                return other.IsSatisfiedByRaw(Value);
            if (other.Operator == ReGoapConditionOperator.Equal)
                return IsSatisfiedByRaw(other.Value);
            if (Operator == ReGoapConditionOperator.NotEqual)
            {
                if (other.Operator == ReGoapConditionOperator.NotEqual)
                    return true;
                return !Equals(Value, other.Value);
            }
            if (other.Operator == ReGoapConditionOperator.NotEqual)
                return other.IsCompatibleWith(this);
            if ((Operator == ReGoapConditionOperator.GreaterOrEqual || Operator == ReGoapConditionOperator.LessOrEqual) &&
                (other.Operator == ReGoapConditionOperator.GreaterOrEqual || other.Operator == ReGoapConditionOperator.LessOrEqual))
            {
                if (!Compare(Value, other.Value, out var cmp))
                    return false;
                if (Operator == ReGoapConditionOperator.GreaterOrEqual && other.Operator == ReGoapConditionOperator.LessOrEqual)
                    return cmp <= 0;
                if (Operator == ReGoapConditionOperator.LessOrEqual && other.Operator == ReGoapConditionOperator.GreaterOrEqual)
                    return cmp >= 0;
                return true;
            }
            return false;
        }

        private static bool Compare(object left, object right, out int result)
        {
            result = 0;
            if (left == null || right == null)
                return false;
            if (left is IComparable comparable)
            {
                try
                {
                    result = comparable.CompareTo(right);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }

        public override string ToString() => Operator switch
        {
            ReGoapConditionOperator.Equal => "== " + Value,
            ReGoapConditionOperator.NotEqual => "!= " + Value,
            ReGoapConditionOperator.GreaterOrEqual => ">= " + Value,
            ReGoapConditionOperator.LessOrEqual => "<= " + Value,
            _ => "? " + Value
        };

        public bool Equals(ReGoapCondition other) => other != null && Operator == other.Operator && Equals(Value, other.Value);
        public override bool Equals(object obj) => Equals(obj as ReGoapCondition);
        public override int GetHashCode() => HashCode.Combine((int)Operator, Value);
    }
}
