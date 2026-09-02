namespace NosAi.Runtime.Contracts;

/// <summary>How much autonomy the runtime is currently trusted with.</summary>
/// <remarks>
/// The scale starts at <see cref="Tier0_ReadOnly"/>. Tiers 1–4 coincide
/// numerically with the former <c>Contracts.TrustTier</c> members
/// (<c>Tier1</c>…<c>Tier4</c>), which this type absorbed: a comparison or a
/// serialization by number does not change meaning.
/// </remarks>
public enum TrustTier : byte
{
    Tier0_ReadOnly = 0,
    Tier1_Assisted = 1,
    Tier2_SemiAutonomous = 2,
    Tier3_AutonomousRestricted = 3,
    Tier4_FullAutonomous = 4
}
