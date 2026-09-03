using NosAi.Adapter.DirectEngine;
using Xunit;

namespace NosAi.Runtime.Tests.DirectEngine;

/// <summary>
/// What a profile has to survive before any address derived from it may be used.
/// </summary>
/// <remarks>
/// Two failures, kept apart on purpose: a profile can be wrong about every client
/// that will ever exist (a mask that does not match its pattern) or wrong only about
/// this one (a signature that has been patched away). The first is
/// <see cref="EngineRefusalCode.ProfileInvalid"/>, the second is
/// <see cref="EngineRefusalCode.SignatureUnresolved"/>, and an operator who cannot
/// tell them apart cannot know whether to fix the profile or update it.
/// </remarks>
public sealed class EngineProfileResolverTests
{
    private static readonly DateTime At = new(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc);

    private static EngineProfileResolver Resolver() => new();

    [Fact]
    public void LegacyProfileStartsUnvalidated()
    {
        EngineClientProfile profile = NosTaleLegacyProfile.Create();

        // The transcription is a claim about one client build. Nothing may treat it
        // as checked until something has checked it.
        Assert.Equal(EngineValidationState.Unvalidated, profile.Validation.State);
        Assert.Equal(EngineArchitecture.X86, profile.Architecture);
    }

    [Fact]
    public void LegacyProfileIsStructurallyValid()
    {
        EngineProfileValidation validation = Resolver().Validate(NosTaleLegacyProfile.Create());

        Assert.True(validation.IsValid, validation.ToString());
        Assert.Empty(validation.Problems);
    }

    [Fact]
    public void EveryCommandingLegacyCapabilityIsDeclared()
    {
        EngineClientProfile profile = NosTaleLegacyProfile.Create();

        foreach (EngineCapability capability in EngineCapabilities.All.Where(EngineCapabilities.Commands))
            Assert.True(profile.Declares(capability), $"{capability} is not declared by the legacy profile.");

        Assert.True(profile.Declares(EngineCapability.ReadState));
        Assert.True(profile.Declares(EngineCapability.ResolvePattern));
    }

    [Fact]
    public void MaskLongerThanItsPatternIsRejected()
    {
        // The reference's own ATTACK_THIS signature: seven pattern bytes, an
        // eight-character mask. Its scanner took the length from the mask and read
        // past the array on every candidate address.
        var profile = new EngineClientProfile(
            "mask-longer-than-pattern",
            EngineArchitecture.X86,
            "NostaleClientX.exe",
            new[]
            {
                new EngineSignature(
                    EngineCapability.Attack,
                    "attack_this",
                    new byte[] { 0x48, 0x00, 0x8C, 0x00, 0x00, 0x00, 0x8C },
                    "x?xxx??x")
            });

        EngineProfileValidation validation = Resolver().Validate(profile);

        Assert.Equal(EngineValidationState.Invalid, validation.State);
        Assert.Contains(validation.Problems, p => p.StartsWith("signature_mask_length_mismatch:", StringComparison.Ordinal));
    }

    [Fact]
    public void AllWildcardSignatureIsRejected()
    {
        var profile = new EngineClientProfile(
            "all-wildcards",
            EngineArchitecture.X86,
            "NostaleClientX.exe",
            new[] { new EngineSignature(EngineCapability.Move, "move", new byte[] { 0x55, 0x8B }, "??") });

        EngineProfileValidation validation = Resolver().Validate(profile);

        // A mask of nothing but wildcards matches the first address scanned: a
        // confident wrong answer, which is worse than no answer.
        Assert.Equal(EngineValidationState.Invalid, validation.State);
        Assert.Contains("signature_all_wildcards:move", validation.Problems);
    }

    [Fact]
    public void ArchitectureMustBeStated()
    {
        var profile = new EngineClientProfile(
            "no-architecture",
            EngineArchitecture.Unknown,
            "NostaleClientX.exe",
            new[] { new EngineSignature(EngineCapability.Move, "move", new byte[] { 0x55, 0x8B }, "xx") });

        Assert.Contains("architecture_not_stated", Resolver().Validate(profile).Problems);
    }

    [Fact]
    public void InvalidProfileIsRefusedBeforeAnythingIsScanned()
    {
        var profile = new EngineClientProfile(
            "invalid",
            EngineArchitecture.X86,
            "NostaleClientX.exe",
            new[] { new EngineSignature(EngineCapability.Move, "move", new byte[] { 0x55, 0x8B }, "xxx") });

        EngineProfileResolution resolution = Resolver()
            .Resolve(profile, new byte[] { 0x55, 0x8B }, 0x0040_0000, 4242, EngineArchitecture.X86, At);

        Assert.False(resolution.Ok);
        Assert.Null(resolution.Profile);
        Assert.Equal(EngineRefusalCode.ProfileInvalid, resolution.Refusal!.Code);
        Assert.Equal(EngineValidationState.Invalid, resolution.Validation.State);
    }

    [Fact]
    public void ProfileForAnotherArchitectureIsRefused()
    {
        EngineClientProfile profile = NosTaleLegacyProfile.Create();
        byte[] image = ClientModuleImage.Containing(profile);

        EngineProfileResolution resolution = Resolver()
            .Resolve(profile, image, 0x0040_0000, 4242, EngineArchitecture.X64, At);

        // The reference's call sequences are inline x86 assembly; against an x64
        // client every signature could match and the calls would still be nonsense.
        Assert.False(resolution.Ok);
        Assert.Equal(EngineRefusalCode.ArchitectureMismatch, resolution.Refusal!.Code);
        Assert.Contains("profile_is_X86_target_is_X64", resolution.Refusal.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolvedAddressesAreRelativeToTheModuleBase()
    {
        EngineClientProfile profile = NosTaleLegacyProfile.Create();
        byte[] image = ClientModuleImage.Containing(profile);
        const nuint moduleBase = 0x1234_0000;

        EngineProfileResolution resolution =
            Resolver().Resolve(profile, image, moduleBase, 4242, EngineArchitecture.X86, At);

        Assert.True(resolution.Ok, resolution.Refusal?.ToString());
        EngineResolvedProfile resolved = resolution.Profile!;

        int expected = ClientModuleImage.OffsetOf(image, profile.Signatures[EngineCapability.Move]);
        Assert.True(resolved.TryGetAddress(EngineCapability.Move, out nuint address, out _));
        Assert.Equal(moduleBase + (nuint)expected, address);

        // Nothing is remembered as an absolute constant, so a client that loads
        // elsewhere resolves elsewhere -- the failure the reference's hardcoded
        // 0x008F4904 cannot survive.
        Assert.Equal(moduleBase, resolved.ModuleBase);
        Assert.Equal(4242, resolved.ProcessId);
        Assert.Equal(At, resolved.ResolvedAtUtc);
    }

    [Fact]
    public void EveryLegacyEntryPointResolves()
    {
        EngineClientProfile profile = NosTaleLegacyProfile.Create();
        byte[] image = ClientModuleImage.Containing(profile);

        EngineProfileResolution resolution =
            Resolver().Resolve(profile, image, 0x0040_0000, 1, EngineArchitecture.X86, At);

        Assert.True(resolution.Ok, resolution.Refusal?.ToString());
        IReadOnlyList<EngineCapability> commanding =
            EngineCapabilities.All.Where(EngineCapabilities.Commands).ToArray();

        Assert.Equal(
            commanding.OrderBy(c => c).ToArray(),
            resolution.Profile!.ResolvedCapabilities.OrderBy(c => c).ToArray());
    }

    [Fact]
    public void PetAndPartnerResolveToTheSameEntryPoint()
    {
        EngineClientProfile profile = NosTaleLegacyProfile.Create();
        byte[] image = ClientModuleImage.Containing(profile);

        EngineResolvedProfile resolved = Resolver()
            .Resolve(profile, image, 0x0040_0000, 1, EngineArchitecture.X86, At).Profile!;

        Assert.True(resolved.TryGetAddress(EngineCapability.MovePet, out nuint pet, out _));
        Assert.True(resolved.TryGetAddress(EngineCapability.MovePartner, out nuint partner, out _));

        // One function in the client, two capabilities here: the boolean that chose
        // between them is a decision the runtime makes, not an argument it passes.
        Assert.Equal(pet, partner);
        Assert.NotEqual((nuint)0, pet);
    }

    [Fact]
    public void AbsentSignatureIsReportedWithoutBlamingTheProfile()
    {
        EngineClientProfile profile = NosTaleLegacyProfile.Create();
        byte[] patched = ClientModuleImage.Containing(profile, new[] { EngineCapability.Rest });

        EngineProfileResolution resolution =
            Resolver().Resolve(profile, patched, 0x0040_0000, 1, EngineArchitecture.X86, At);

        Assert.True(resolution.Ok, resolution.Refusal?.ToString());
        Assert.True(resolution.Validation.IsValid);

        EngineSignatureResolution rest = resolution.Profile!.Signatures[EngineCapability.Rest];
        Assert.False(rest.IsResolved);
        Assert.Equal(0, rest.Matches);
        Assert.Equal(EngineRefusalCode.SignatureUnresolved, rest.Refusal!.Code);
        Assert.Equal("signature_not_found:rest", rest.Refusal.Detail);

        // The rest of the build is still usable: one patched function does not
        // invalidate the ten that are still where the profile says.
        Assert.DoesNotContain(EngineCapability.Rest, resolution.Profile.ResolvedCapabilities);
        Assert.Contains(EngineCapability.Move, resolution.Profile.ResolvedCapabilities);
    }

    [Fact]
    public void AmbiguousSignatureCountsAsUnresolved()
    {
        var signature = new EngineSignature(
            EngineCapability.Move, "move", new byte[] { 0x55, 0x8B, 0xEC }, "xxx");
        var profile = new EngineClientProfile(
            "ambiguous", EngineArchitecture.X86, "NostaleClientX.exe", new[] { signature });

        byte[] image = { 0xCC, 0x55, 0x8B, 0xEC, 0xCC, 0xCC, 0x55, 0x8B, 0xEC, 0xCC };

        EngineProfileResolution resolution =
            Resolver().Resolve(profile, image, 0x0040_0000, 1, EngineArchitecture.X86, At);

        // The reference took the first hit and never looked again, so a loose
        // signature silently resolved to whichever copy came first in memory.
        Assert.False(resolution.Ok);
        Assert.Equal(EngineRefusalCode.SignatureUnresolved, resolution.Refusal!.Code);
    }

    [Fact]
    public void EmptyModuleImageIsAnAttachProblemNotAProfileProblem()
    {
        EngineClientProfile profile = NosTaleLegacyProfile.Create();

        EngineProfileResolution resolution =
            Resolver().Resolve(profile, ReadOnlySpan<byte>.Empty, 0x0040_0000, 1, EngineArchitecture.X86, At);

        Assert.False(resolution.Ok);
        Assert.Equal(EngineRefusalCode.NotAttached, resolution.Refusal!.Code);
        Assert.True(resolution.Validation.IsValid);
    }

    [Fact]
    public void PointerPathKeepsTheReferenceWalkItWasDerivedUnder()
    {
        EngineClientProfile profile = NosTaleLegacyProfile.Create();
        EnginePointerPath position = profile.PointerPaths[NosTaleLegacyProfile.PlayerPositionPath];

        // ReadPointer(0x004F4904, { 0x20, 0x0C }): the base is a module offset, 0x20
        // is dereferenced and 0x0C is added. Offsets that are read under a different
        // walk address something else entirely.
        Assert.Equal(NosTaleLegacyProfile.PlayerManagerOffset, position.ModuleOffset);
        Assert.Equal(new[] { 0x20, 0x0C }, position.Offsets);
        Assert.True(position.IsWellFormed(out _));
    }

    [Fact]
    public void ContextOffsetsAreRelativeNotAbsolute()
    {
        EngineClientProfile profile = NosTaleLegacyProfile.Create();

        // 0x008F4904 in the reference is this offset plus an assumed image base.
        Assert.Equal(
            0x008F_4904u,
            NosTaleLegacyProfile.PlayerManagerOffset + NosTaleLegacyProfile.AssumedImageBase);
        Assert.Equal(
            0x0076_5EA8u,
            NosTaleLegacyProfile.CollectContextOffset + NosTaleLegacyProfile.AssumedImageBase);
        Assert.Equal(
            NosTaleLegacyProfile.PlayerManagerOffset, profile.ContextOffsets[EngineCapability.Move]);
        Assert.Equal(
            NosTaleLegacyProfile.CollectContextOffset, profile.ContextOffsets[EngineCapability.Collect]);
    }
}
