namespace NosAi.Core.WorldModel;

/// <summary>The kind of a depletable/regenerating pool a <see cref="Player"/> or <see cref="Mob"/> tracks.</summary>
public enum ResourceKind
{
    Health = 0,
    Mana = 1,
    Stamina = 2,
    Experience = 3,
    Currency = 4,

    /// <summary>A game-specific pool not covered above; <see cref="Resource.CustomName"/> names it.</summary>
    Custom = 5
}

/// <summary>
/// One named, bounded resource pool (HP, MP, stamina, currency, ...). Both
/// bounds are independently classified: a HUD showing current HP without a
/// visible max (or vice versa) must not fabricate the missing half.
/// </summary>
public sealed record Resource
{
    public ResourceKind Kind { get; init; }
    public WorldFact<double> Current { get; init; }
    public WorldFact<double> Maximum { get; init; }
    public string? CustomName { get; init; }

    public Resource(ResourceKind kind, WorldFact<double> current, WorldFact<double> maximum, string? customName = null)
    {
        if (kind == ResourceKind.Custom)
            ArgumentException.ThrowIfNullOrWhiteSpace(customName);

        Kind = kind;
        Current = current;
        Maximum = maximum;
        CustomName = customName;
    }
}
