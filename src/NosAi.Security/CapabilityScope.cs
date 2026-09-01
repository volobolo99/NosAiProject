namespace NosAi.Security;

/// <summary>
/// Scope bits carried by a <see cref="CapabilityToken"/>. Gate 1 only ever
/// requests <see cref="Observe"/>: INV-01 forbids this spine from holding
/// execution authority.
/// </summary>
public static class CapabilityScope
{
    public const uint Observe = 1u << 0;
    public const uint Execute = 1u << 1;
}
