namespace NosAi.Adapter.DirectEngine;

/// <summary>
/// Whether a capability may be exercised at all, asked of whoever holds that
/// authority.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a one-method contract owned by this project rather than a reference
/// to the runtime's own gate. The adapter must stay reusable and must not depend on
/// <c>NosAi.Runtime</c>, but the runtime is the authority for safety and
/// authorisation — so the adapter states the question and the runtime answers it,
/// by implementing this over its own <c>CapabilityAuthorizationGate</c>. Nothing
/// here decides anything; it only refuses to act unasked.
/// </para>
/// <para>
/// Null means allowed. A gate that cannot answer must refuse, not return null:
/// that is the direction failure has to fall.
/// </para>
/// </remarks>
public interface IEngineAuthorizationGate
{
    /// <summary>The reason this capability may not be exercised, or null when it may.</summary>
    EngineRefusal? Authorize(EngineCapability capability);
}

/// <summary>
/// The gate an adapter gets when nobody supplied one: it refuses everything.
/// </summary>
/// <remarks>
/// The default has to be this. An adapter constructed without an authority is an
/// adapter nobody has decided about, and the safe reading of "nobody decided" is
/// no, in a component whose capabilities include calling functions inside a live
/// game client.
/// </remarks>
public sealed class ClosedEngineAuthorizationGate : IEngineAuthorizationGate
{
    public const string Reason = "no_authorization_authority_configured";

    public static ClosedEngineAuthorizationGate Instance { get; } = new();

    public EngineRefusal? Authorize(EngineCapability capability) =>
        new(EngineRefusalCode.NotAuthorized, $"{Reason}:{capability}");
}

/// <summary>
/// A gate that asks a delegate, so the runtime can supply its own decision without
/// this project referencing it.
/// </summary>
/// <remarks>
/// A delegate that throws is treated as a refusal rather than allowed to escape:
/// an authority that failed to answer has not said yes.
/// </remarks>
public sealed class DelegatedEngineAuthorizationGate : IEngineAuthorizationGate
{
    private readonly Func<EngineCapability, EngineRefusal?> _decide;

    public DelegatedEngineAuthorizationGate(Func<EngineCapability, EngineRefusal?> decide)
    {
        _decide = decide ?? throw new ArgumentNullException(nameof(decide));
    }

    public EngineRefusal? Authorize(EngineCapability capability)
    {
        try
        {
            return _decide(capability);
        }
        catch (Exception ex)
        {
            return new EngineRefusal(
                EngineRefusalCode.NotAuthorized, $"authorization_authority_faulted:{ex.GetType().Name}");
        }
    }
}
