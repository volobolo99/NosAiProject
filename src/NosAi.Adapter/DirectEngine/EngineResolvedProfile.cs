namespace NosAi.Adapter.DirectEngine;

/// <summary>What became of one signature when it was looked for.</summary>
/// <param name="Capability">The power it locates.</param>
/// <param name="Name">The signature's label, for diagnostics.</param>
/// <param name="Address">The absolute address in the attached process, or zero when unresolved.</param>
/// <param name="Matches">
/// How many places matched. One is located; zero is absent; more than one means the
/// signature identifies nothing and is treated as unresolved.
/// </param>
/// <param name="Refusal">Non-null exactly when <see cref="IsResolved"/> is false.</param>
public sealed record EngineSignatureResolution(
    EngineCapability Capability,
    string Name,
    nuint Address,
    int Matches,
    EngineRefusal? Refusal)
{
    public bool IsResolved => Refusal is null && Matches == 1 && Address != 0;
}

/// <summary>
/// A profile restated as addresses in one attached process, at one moment.
/// </summary>
/// <remarks>
/// <para>
/// This is the short-lived half of the pair. The profile it came from describes a
/// client build and is worth keeping; these addresses describe one loaded copy of
/// it and stop being true the moment that process exits. Nothing here should
/// outlive the attach that produced it, which is why <see cref="ProcessId"/> and
/// <see cref="ModuleBase"/> travel with the addresses rather than beside them.
/// </para>
/// <para>
/// A capability whose signature did not resolve is present in
/// <see cref="Signatures"/> with its refusal, not absent. "I looked and did not
/// find it" and "I never looked" are different answers and the operator needs both.
/// </para>
/// </remarks>
public sealed class EngineResolvedProfile
{
    private readonly Dictionary<EngineCapability, EngineSignatureResolution> _signatures;

    public EngineResolvedProfile(
        EngineClientProfile profile,
        int processId,
        nuint moduleBase,
        long moduleSize,
        IEnumerable<EngineSignatureResolution> signatures,
        DateTime resolvedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(signatures);

        Profile = profile;
        ProcessId = processId;
        ModuleBase = moduleBase;
        ModuleSize = moduleSize;
        ResolvedAtUtc = resolvedAtUtc;

        _signatures = new Dictionary<EngineCapability, EngineSignatureResolution>();
        foreach (EngineSignatureResolution resolution in signatures)
        {
            ArgumentNullException.ThrowIfNull(resolution);
            _signatures[resolution.Capability] = resolution;
        }
    }

    /// <summary>The build description these addresses came from.</summary>
    public EngineClientProfile Profile { get; }

    /// <summary>The process the addresses are valid in. They mean nothing in any other.</summary>
    public int ProcessId { get; }

    public nuint ModuleBase { get; }

    public long ModuleSize { get; }

    public DateTime ResolvedAtUtc { get; }

    public IReadOnlyDictionary<EngineCapability, EngineSignatureResolution> Signatures => _signatures;

    /// <summary>Capabilities whose entry point was located exactly once.</summary>
    public IEnumerable<EngineCapability> ResolvedCapabilities =>
        _signatures.Where(pair => pair.Value.IsResolved).Select(pair => pair.Key);

    /// <summary>
    /// The address of a capability's entry point, or the reason there is none.
    /// </summary>
    public bool TryGetAddress(EngineCapability capability, out nuint address, out EngineRefusal? refusal)
    {
        address = 0;

        if (!_signatures.TryGetValue(capability, out EngineSignatureResolution? resolution))
        {
            refusal = new EngineRefusal(
                EngineRefusalCode.CapabilityNotDeclared, $"capability_not_declared:{capability}");
            return false;
        }

        if (!resolution.IsResolved)
        {
            refusal = resolution.Refusal
                ?? new EngineRefusal(EngineRefusalCode.SignatureUnresolved, $"signature_unresolved:{resolution.Name}");
            return false;
        }

        address = resolution.Address;
        refusal = null;
        return true;
    }

    /// <summary>
    /// The absolute address of a module offset in this process.
    /// </summary>
    /// <remarks>
    /// The one place the profile's module-relative numbers become real pointers, so
    /// that no caller is tempted to reintroduce the reference's absolute constants.
    /// </remarks>
    public nuint Absolute(uint moduleOffset) => ModuleBase + moduleOffset;
}
