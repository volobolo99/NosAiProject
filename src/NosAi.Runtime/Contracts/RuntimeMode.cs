namespace NosAi.Runtime.Contracts;

/// <summary>The operating state that gates which actions may run at all.</summary>
public enum RuntimeMode : byte
{
    Normal = 0,
    Degraded = 1,
    Recovery = 2,
    Cooling = 3,
    Stopped = 4
}
