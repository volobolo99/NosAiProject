namespace NosAi.Adapter.DirectEngine;

/// <summary>
/// The direct engine as it stands: every capability declared, every check in
/// place, and nothing carried out yet.
/// </summary>
/// <remarks>
/// <para>
/// <b>This refuses, and the refusal is the point.</b> The gate chain a real
/// implementation has to pass is built and exercised here — request well formed,
/// caller authorised, profile present, profile valid, capability declared,
/// signature located — and only after all of it does the last step report
/// <see cref="EngineRefusalCode.NotImplemented"/>. Wiring the call sequences in
/// later means replacing that final step, not inserting checks around code that
/// already acts. A boundary written the other way round is one where the checks
/// are optional in practice.
/// </para>
/// <para>
/// <b>Order of checks.</b> Authorisation comes before anything is looked up,
/// immediately after the request is checked for coherence. An unauthorised caller
/// learns nothing about the client from asking, and no work is done on behalf of
/// someone who may not ask for it. Malformed requests are refused even earlier
/// because they are wrong regardless of who sent them.
/// </para>
/// <para>
/// The clock is injected rather than read from <see cref="DateTime"/> ambiently, so
/// the timestamps in a result are as testable as the refusals are.
/// </para>
/// </remarks>
public sealed class DirectEngineAdapter : IDirectEngineAdapter
{
    /// <summary>Reported when every gate passed and the call sequence does not exist yet.</summary>
    /// <remarks>
    /// Deliberately specific. "Not implemented" alone would read as a capability that
    /// was dropped; naming the missing piece says what has to be built and that the
    /// contract above it is already complete.
    /// </remarks>
    public const string ExecutionSeamReason = "engine_call_not_implemented";

    /// <summary>Reported when a state read passes every gate and the memory path is not wired.</summary>
    public const string ReadSeamReason = "engine_state_read_not_implemented";

    private readonly IEngineProfileResolver _resolver;
    private readonly IEngineAuthorizationGate _authorization;
    private readonly Func<DateTime> _utcNow;

    /// <param name="resolver">How a candidate profile becomes addresses. Defaults to <see cref="EngineProfileResolver"/>.</param>
    /// <param name="authorization">
    /// The safety authority. Defaults to <see cref="ClosedEngineAuthorizationGate"/>,
    /// which refuses everything: an adapter nobody has decided about does nothing.
    /// </param>
    /// <param name="utcNow">The clock, for request and result timestamps.</param>
    public DirectEngineAdapter(
        IEngineProfileResolver? resolver = null,
        IEngineAuthorizationGate? authorization = null,
        Func<DateTime>? utcNow = null)
    {
        _resolver = resolver ?? new EngineProfileResolver();
        _authorization = authorization ?? ClosedEngineAuthorizationGate.Instance;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public EngineResolvedProfile? Profile { get; private set; }

    public IReadOnlyList<EngineCapability> DeclaredCapabilities => EngineCapabilities.All;

    public bool TryLoadProfile(
        EngineClientProfile candidate,
        ReadOnlySpan<byte> moduleImage,
        nuint moduleBase,
        int processId,
        EngineArchitecture architecture,
        out EngineRefusal? refusal)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        refusal = _authorization.Authorize(EngineCapability.ResolvePattern);
        if (refusal is not null)
            return false;

        EngineProfileResolution resolution =
            _resolver.Resolve(candidate, moduleImage, moduleBase, processId, architecture, _utcNow());

        if (!resolution.Ok)
        {
            // A failed load leaves the previous profile alone rather than clearing it:
            // a rejected candidate is not a reason to forget a profile that resolved.
            refusal = resolution.Refusal
                ?? new EngineRefusal(EngineRefusalCode.ProfileInvalid, "profile_not_resolved");
            return false;
        }

        Profile = resolution.Profile;
        refusal = null;
        return true;
    }

    public bool IsAvailable(EngineCapability capability, out EngineRefusal? refusal)
    {
        refusal = CheckGates(capability);
        return refusal is null;
    }

    public EngineStateResult ReadState()
    {
        EngineRefusal? refusal = CheckGates(EngineCapability.ReadState);
        if (refusal is not null)
            return EngineStateResult.Refused(refusal);

        return EngineStateResult.Refused(new EngineRefusal(EngineRefusalCode.NotImplemented, ReadSeamReason));
    }

    public EngineActionResult Execute(in EngineActionRequest request)
    {
        if (!request.IsWellFormed(out EngineRefusal? malformed) && malformed is not null)
            return EngineActionResult.Refused(request, malformed, _utcNow());

        EngineRefusal? refusal = CheckGates(request.Capability);
        if (refusal is not null)
            return EngineActionResult.Refused(request, refusal, _utcNow());

        return EngineActionResult.Refused(
            request,
            new EngineRefusal(EngineRefusalCode.NotImplemented, $"{ExecutionSeamReason}:{request.Capability}"),
            _utcNow());
    }

    public EngineActionResult Move(int mapX, int mapY, string correlationId)
    {
        EngineActionRequest request = EngineActionRequest.Move(mapX, mapY, _utcNow(), correlationId);
        return Execute(request);
    }

    public EngineActionResult Attack(nuint targetHandle, short skillId, string correlationId)
    {
        EngineActionRequest request = EngineActionRequest.Attack(targetHandle, skillId, _utcNow(), correlationId);
        return Execute(request);
    }

    public EngineActionResult AttackRun(nuint targetHandle, string correlationId)
    {
        EngineActionRequest request = EngineActionRequest.AttackRun(targetHandle, _utcNow(), correlationId);
        return Execute(request);
    }

    public EngineActionResult Collect(nuint itemHandle, string correlationId)
    {
        EngineActionRequest request = EngineActionRequest.Collect(itemHandle, _utcNow(), correlationId);
        return Execute(request);
    }

    public EngineActionResult Rest(string correlationId)
    {
        EngineActionRequest request = EngineActionRequest.Rest(_utcNow(), correlationId);
        return Execute(request);
    }

    public EngineActionResult MovePet(int mapX, int mapY, string correlationId)
    {
        EngineActionRequest request = EngineActionRequest.MovePet(mapX, mapY, _utcNow(), correlationId);
        return Execute(request);
    }

    public EngineActionResult MovePartner(int mapX, int mapY, string correlationId)
    {
        EngineActionRequest request = EngineActionRequest.MovePartner(mapX, mapY, _utcNow(), correlationId);
        return Execute(request);
    }

    public EngineActionResult AttackWithPet(nuint targetHandle, string correlationId)
    {
        EngineActionRequest request = EngineActionRequest.AttackWithPet(targetHandle, _utcNow(), correlationId);
        return Execute(request);
    }

    public EngineActionResult AttackWithPartner(nuint targetHandle, string correlationId)
    {
        EngineActionRequest request = EngineActionRequest.AttackWithPartner(targetHandle, _utcNow(), correlationId);
        return Execute(request);
    }

    /// <summary>
    /// Every condition a capability has to satisfy before it could be exercised, in
    /// the order they are asked.
    /// </summary>
    /// <returns>The first refusal, or null when the capability is clear to proceed.</returns>
    private EngineRefusal? CheckGates(EngineCapability capability)
    {
        if (capability == EngineCapability.None)
            return new EngineRefusal(EngineRefusalCode.InvalidRequest, "capability_not_stated");

        EngineRefusal? denied = _authorization.Authorize(capability);
        if (denied is not null)
            return denied;

        EngineResolvedProfile? profile = Profile;
        if (profile is null)
            return new EngineRefusal(EngineRefusalCode.ProfileMissing, $"no_profile_loaded:{capability}");

        if (!profile.Profile.Validation.IsValid)
        {
            return new EngineRefusal(
                EngineRefusalCode.ProfileInvalid, $"profile_not_valid:{profile.Profile.Validation}");
        }

        if (!profile.Profile.Declares(capability))
            return new EngineRefusal(EngineRefusalCode.CapabilityNotDeclared, $"capability_not_declared:{capability}");

        // Reading state and scanning need no located entry point: the first goes
        // through pointer paths, the second is the scan itself.
        if (!EngineCapabilities.RequiresSignature(capability))
            return null;

        return profile.TryGetAddress(capability, out _, out EngineRefusal? unresolved) ? null : unresolved;
    }
}
