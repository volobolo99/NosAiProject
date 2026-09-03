using NosAi.Adapter.DirectEngine;
using NosAi.Core;
using Xunit;

namespace NosAi.Runtime.Tests.DirectEngine;

/// <summary>
/// The fail-closed chain: every reason the direct engine declines to act, and the
/// fact that it declines by default.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here executes anything, and that is what is being pinned. The contract
/// covers every capability the reference bot had, and an implementation that cannot
/// yet carry one out says <see cref="EngineRefusalCode.NotImplemented"/> — it does
/// not drop the capability from the surface, and it never reports
/// <see cref="EngineOutcome.Executed"/> for an act that did not happen.
/// </para>
/// <para>
/// The gates are built from the production pieces: <see cref="ClosedEngineAuthorizationGate"/>
/// is the real default, and <see cref="DelegatedEngineAuthorizationGate"/> is the
/// real seam the runtime will supply its own decision through. Neither is a stand-in.
/// </para>
/// </remarks>
public sealed class DirectEngineAdapterTests
{
    private static readonly DateTime At = new(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>A gate that permits exactly the named capabilities and refuses the rest.</summary>
    private static IEngineAuthorizationGate Permitting(params EngineCapability[] permitted) =>
        new DelegatedEngineAuthorizationGate(capability => permitted.Contains(capability)
            ? null
            : new EngineRefusal(EngineRefusalCode.NotAuthorized, $"not_granted:{capability}"));

    private static IEngineAuthorizationGate PermittingEverything() =>
        new DelegatedEngineAuthorizationGate(_ => null);

    private static DirectEngineAdapter Adapter(IEngineAuthorizationGate? gate = null) =>
        new(new EngineProfileResolver(), gate, () => At);

    /// <summary>An adapter with the legacy profile resolved against a module that has every function.</summary>
    private static DirectEngineAdapter Loaded(
        IEngineAuthorizationGate gate, params EngineCapability[] omitFromModule)
    {
        EngineClientProfile profile = NosTaleLegacyProfile.Create();
        byte[] image = ClientModuleImage.Containing(profile, omitFromModule);
        DirectEngineAdapter adapter = Adapter(gate);

        Assert.True(
            adapter.TryLoadProfile(profile, image, 0x0040_0000, 4242, EngineArchitecture.X86, out EngineRefusal? refusal),
            refusal?.ToString());

        return adapter;
    }

    // ---- the surface covers what the reference had -------------------------

    [Fact]
    public void EveryLegacyCapabilityIsNamedOnTheContract()
    {
        // CallFunction.h exported nine acts and reached the client's data through
        // memscan.c; all eleven are on the contract, so none can be exercised
        // without the runtime having a name to authorise or refuse.
        Assert.Equal(
            new[]
            {
                EngineCapability.ReadState,
                EngineCapability.ResolvePattern,
                EngineCapability.Move,
                EngineCapability.Attack,
                EngineCapability.AttackRun,
                EngineCapability.Collect,
                EngineCapability.Rest,
                EngineCapability.MovePet,
                EngineCapability.MovePartner,
                EngineCapability.AttackWithPet,
                EngineCapability.AttackWithPartner
            },
            Adapter().DeclaredCapabilities);
    }

    [Fact]
    public void ReadingIsSeparatedFromCommanding()
    {
        Assert.False(EngineCapabilities.Commands(EngineCapability.ReadState));
        Assert.False(EngineCapabilities.Commands(EngineCapability.ResolvePattern));

        foreach (EngineCapability capability in EngineCapabilities.All
                     .Except(new[] { EngineCapability.ReadState, EngineCapability.ResolvePattern }))
        {
            Assert.True(EngineCapabilities.Commands(capability), $"{capability} should command the client.");
        }
    }

    [Fact]
    public void EveryNamedMethodCarriesItsOwnCapability()
    {
        DirectEngineAdapter adapter = Loaded(PermittingEverything());

        Assert.Equal(EngineCapability.Move, adapter.Move(10, 20, "c").Capability);
        Assert.Equal(EngineCapability.Attack, adapter.Attack(0x1000, 3, "c").Capability);
        Assert.Equal(EngineCapability.AttackRun, adapter.AttackRun(0x1000, "c").Capability);
        Assert.Equal(EngineCapability.Collect, adapter.Collect(0x1000, "c").Capability);
        Assert.Equal(EngineCapability.Rest, adapter.Rest("c").Capability);
        Assert.Equal(EngineCapability.MovePet, adapter.MovePet(10, 20, "c").Capability);
        Assert.Equal(EngineCapability.MovePartner, adapter.MovePartner(10, 20, "c").Capability);
        Assert.Equal(EngineCapability.AttackWithPet, adapter.AttackWithPet(0x1000, "c").Capability);
        Assert.Equal(EngineCapability.AttackWithPartner, adapter.AttackWithPartner(0x1000, "c").Capability);
    }

    // ---- fail closed -------------------------------------------------------

    [Fact]
    public void AnAdapterNobodyDecidedAboutRefusesEverything()
    {
        var adapter = new DirectEngineAdapter();

        foreach (EngineCapability capability in adapter.DeclaredCapabilities)
        {
            Assert.False(adapter.IsAvailable(capability, out EngineRefusal? refusal));
            Assert.Equal(EngineRefusalCode.NotAuthorized, refusal!.Code);
            Assert.Contains(ClosedEngineAuthorizationGate.Reason, refusal.Detail, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ScanningIsRefusedSeparatelyFromCalling()
    {
        // Locating a function is not calling it, so an operator who has permitted
        // every act but not the scan gets no profile and therefore no addresses.
        DirectEngineAdapter adapter = Adapter(Permitting(EngineCapability.Move, EngineCapability.Attack));

        bool loaded = adapter.TryLoadProfile(
            NosTaleLegacyProfile.Create(),
            ClientModuleImage.Containing(NosTaleLegacyProfile.Create()),
            0x0040_0000,
            4242,
            EngineArchitecture.X86,
            out EngineRefusal? refusal);

        Assert.False(loaded);
        Assert.Null(adapter.Profile);
        Assert.Equal(EngineRefusalCode.NotAuthorized, refusal!.Code);
        Assert.Equal(FaultCode.ScopeDenied, refusal.Fault);
    }

    [Fact]
    public void WithoutAProfileNothingIsAvailable()
    {
        DirectEngineAdapter adapter = Adapter(PermittingEverything());

        Assert.Null(adapter.Profile);
        Assert.False(adapter.IsAvailable(EngineCapability.Move, out EngineRefusal? refusal));
        Assert.Equal(EngineRefusalCode.ProfileMissing, refusal!.Code);

        EngineActionResult result = adapter.Move(10, 20, "no-profile");
        Assert.Equal(EngineOutcome.Refused, result.Outcome);
        Assert.Equal(EngineRefusalCode.ProfileMissing, result.Refusal!.Code);
        Assert.Equal(FaultCode.AttachFailed, result.Refusal.Fault);
    }

    [Fact]
    public void AnInvalidProfileIsNeverLoaded()
    {
        var profile = new EngineClientProfile(
            "invalid",
            EngineArchitecture.X86,
            "NostaleClientX.exe",
            new[] { new EngineSignature(EngineCapability.Move, "move", new byte[] { 0x55, 0x8B }, "xxx") });

        DirectEngineAdapter adapter = Adapter(PermittingEverything());

        Assert.False(adapter.TryLoadProfile(
            profile, new byte[] { 0x55, 0x8B }, 0x0040_0000, 4242, EngineArchitecture.X86, out EngineRefusal? refusal));
        Assert.Equal(EngineRefusalCode.ProfileInvalid, refusal!.Code);
        Assert.Null(adapter.Profile);
    }

    [Fact]
    public void ARejectedCandidateDoesNotDiscardAWorkingProfile()
    {
        DirectEngineAdapter adapter = Loaded(PermittingEverything());
        EngineResolvedProfile before = adapter.Profile!;

        var broken = new EngineClientProfile(
            "broken",
            EngineArchitecture.X86,
            "NostaleClientX.exe",
            new[] { new EngineSignature(EngineCapability.Move, "move", new byte[] { 0x55 }, "??") });

        Assert.False(adapter.TryLoadProfile(
            broken, new byte[] { 0x55 }, 0x0040_0000, 4242, EngineArchitecture.X86, out _));
        Assert.Same(before, adapter.Profile);
    }

    [Fact]
    public void ACapabilityWhoseSignatureIsAbsentIsRefusedByName()
    {
        // The client was patched and Rest moved; every other capability is intact.
        DirectEngineAdapter adapter = Loaded(PermittingEverything(), EngineCapability.Rest);

        Assert.False(adapter.IsAvailable(EngineCapability.Rest, out EngineRefusal? refusal));
        Assert.Equal(EngineRefusalCode.SignatureUnresolved, refusal!.Code);
        Assert.Equal("signature_not_found:rest", refusal.Detail);

        EngineActionResult result = adapter.Rest("rest-after-patch");
        Assert.Equal(EngineOutcome.Refused, result.Outcome);
        Assert.Equal(EngineRefusalCode.SignatureUnresolved, result.Refusal!.Code);

        Assert.True(adapter.IsAvailable(EngineCapability.Move, out _));
    }

    [Fact]
    public void ACapabilityTheProfileDoesNotDeclareIsRefusedByName()
    {
        var walkOnly = new EngineClientProfile(
            "walk-only",
            EngineArchitecture.X86,
            "NostaleClientX.exe",
            new[] { NosTaleLegacyProfile.Create().Signatures[EngineCapability.Move] });

        DirectEngineAdapter adapter = Adapter(PermittingEverything());
        Assert.True(adapter.TryLoadProfile(
            walkOnly,
            ClientModuleImage.Containing(walkOnly),
            0x0040_0000,
            4242,
            EngineArchitecture.X86,
            out _));

        Assert.False(adapter.IsAvailable(EngineCapability.Collect, out EngineRefusal? refusal));
        Assert.Equal(EngineRefusalCode.CapabilityNotDeclared, refusal!.Code);
        Assert.Equal("capability_not_declared:Collect", refusal.Detail);
    }

    [Fact]
    public void AuthorizationIsAskedBeforeAnythingIsLookedUp()
    {
        // No profile and no permission. The answer names the permission, because an
        // unauthorised caller must learn nothing about the client by asking.
        DirectEngineAdapter adapter = Adapter(Permitting(EngineCapability.ReadState));

        Assert.False(adapter.IsAvailable(EngineCapability.Attack, out EngineRefusal? refusal));
        Assert.Equal(EngineRefusalCode.NotAuthorized, refusal!.Code);
    }

    [Fact]
    public void AnAuthorityThatFaultsIsARefusal()
    {
        var adapter = new DirectEngineAdapter(
            authorization: new DelegatedEngineAuthorizationGate(
                _ => throw new InvalidOperationException("policy store unreachable")));

        Assert.False(adapter.IsAvailable(EngineCapability.Move, out EngineRefusal? refusal));
        Assert.Equal(EngineRefusalCode.NotAuthorized, refusal!.Code);
        Assert.Contains("authorization_authority_faulted", refusal.Detail, StringComparison.Ordinal);
    }

    // ---- the seam ----------------------------------------------------------

    [Fact]
    public void EveryGatePassedStillDoesNotExecute()
    {
        DirectEngineAdapter adapter = Loaded(PermittingEverything());

        Assert.True(adapter.IsAvailable(EngineCapability.Move, out _));

        EngineActionResult result = adapter.Move(42, 77, "walk-to-77-42");

        // The capability is not gone: it is located, authorised and waiting for the
        // call sequence. That is a different answer from "refused" and from "done".
        Assert.Equal(EngineOutcome.Refused, result.Outcome);
        Assert.False(result.Executed);
        Assert.Equal(EngineRefusalCode.NotImplemented, result.Refusal!.Code);
        Assert.Equal($"{DirectEngineAdapter.ExecutionSeamReason}:Move", result.Refusal.Detail);
    }

    [Fact]
    public void ReadingStateHasItsOwnSeam()
    {
        DirectEngineAdapter adapter = Loaded(PermittingEverything());

        EngineStateResult result = adapter.ReadState();

        Assert.False(result.Ok);
        Assert.Equal(EngineRefusalCode.NotImplemented, result.Refusal!.Code);
        Assert.Equal(DirectEngineAdapter.ReadSeamReason, result.Refusal.Detail);

        // Nothing is guessed in the meantime: unknown is absent, not zero.
        Assert.True(result.Snapshot.IsEmpty);
        Assert.False(result.Snapshot.HasPosition);
        Assert.Null(result.Snapshot.Hp);
    }

    [Fact]
    public void ResultsCarryTheirRequestThroughToTheAnswer()
    {
        DirectEngineAdapter adapter = Loaded(PermittingEverything());

        EngineActionResult result = adapter.Attack(0xDEAD, 2, "engage-42");

        Assert.Equal("engage-42", result.CorrelationId);
        Assert.Equal(At, result.RequestedAtUtc);
        Assert.Equal(At, result.CompletedAtUtc);
        Assert.Equal(TimeSpan.Zero, result.Elapsed);
    }

    // ---- malformed requests ------------------------------------------------

    [Fact]
    public void ADestinationOffTheMapIsRefusedBeforeAuthorization()
    {
        // Checked first because it is wrong regardless of who is asking: the packed
        // word carries two 16-bit halves, and a larger coordinate does not overflow
        // loudly, it lands somewhere else on the map.
        DirectEngineAdapter adapter = Adapter(Permitting());

        EngineActionResult result = adapter.Move(70_000, 5, "off-map");

        Assert.Equal(EngineRefusalCode.InvalidRequest, result.Refusal!.Code);
        Assert.Equal("destination_out_of_range:70000,5", result.Refusal.Detail);
    }

    [Fact]
    public void AnActionWithNoTargetIsRefusedByName()
    {
        DirectEngineAdapter adapter = Adapter(PermittingEverything());

        Assert.Equal(
            "target_handle_missing:Attack",
            adapter.Attack(0, 1, "no-target").Refusal!.Detail);
        Assert.Equal(
            "target_handle_missing:Collect",
            adapter.Collect(0, "no-item").Refusal!.Detail);
    }

    [Fact]
    public void ADestinationIsPackedTheWayTheClientWantsIt()
    {
        EngineActionRequest request = EngineActionRequest.Move(0x0042, 0x0077, At, "pack");

        Assert.True(request.HasDestination);
        Assert.Equal(0x0077_0042u, request.PackedDestination);
        Assert.Equal((uint)((0x0077 * 65536) + 0x0042), request.PackedDestination);
    }

    [Fact]
    public void ARequestWithNoDestinationRefusesToInventOne()
    {
        // (0,0) is a real corner of the map, so an absent destination cannot be
        // represented as one.
        EngineActionRequest rest = EngineActionRequest.Rest(At, "rest");

        Assert.False(rest.HasDestination);
        Assert.Throws<InvalidOperationException>(() => rest.PackedDestination);
    }

    [Fact]
    public void OriginIsAValidDestination()
    {
        EngineActionRequest request = EngineActionRequest.Move(0, 0, At, "origin");

        Assert.True(request.HasDestination);
        Assert.True(request.IsWellFormed(out EngineRefusal? refusal));
        Assert.Null(refusal);
        Assert.Equal(0u, request.PackedDestination);
    }
}
