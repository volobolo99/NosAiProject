namespace NosAi.Core.Planning;

public readonly record struct GoalId(uint Value)
{
    public static GoalId None => default;
    public override string ToString() => Value.ToString();
}

public enum GoalClass : byte
{
    Opportunistic = 0,
    Objective = 1,
    Survival = 2,
    Safety = 3
}
