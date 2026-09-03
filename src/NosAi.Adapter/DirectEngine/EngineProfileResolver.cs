namespace NosAi.Adapter.DirectEngine;

/// <summary>
/// The resolver: structural validation, then a scan of the module image.
/// </summary>
/// <remarks>
/// Stateless, so two attaches never share a cached address. That is not tidiness:
/// caching a resolved address across attaches is exactly how a bot ends up calling
/// into whatever now occupies an address a previous client used, and the reference
/// is a demonstration of that failure — it resolved once at start-up and pinned
/// three of its call targets to absolute constants that assume the client loaded at
/// <c>0x400000</c>.
/// </remarks>
public sealed class EngineProfileResolver : IEngineProfileResolver
{
    public EngineProfileValidation Validate(EngineClientProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var problems = new List<string>();

        if (profile.Architecture == EngineArchitecture.Unknown)
            problems.Add("architecture_not_stated");

        if (profile.Signatures.Count == 0)
            problems.Add("no_signatures_declared");

        foreach (EngineCapability duplicate in profile.DuplicateCapabilities)
            problems.Add($"capability_declared_twice:{duplicate}");

        foreach (string duplicate in profile.DuplicatePointerPaths)
            problems.Add($"pointer_path_declared_twice:{duplicate}");

        foreach (KeyValuePair<EngineCapability, EngineSignature> entry in profile.Signatures)
        {
            if (!EngineCapabilities.RequiresSignature(entry.Key))
            {
                // ReadState and ResolvePattern are reached without a scan; a signature
                // for either is a profile saying something it cannot mean.
                problems.Add($"capability_does_not_take_a_signature:{entry.Key}");
                continue;
            }

            if (!entry.Value.IsWellFormed(out string? problem) && problem is not null)
                problems.Add(problem);
        }

        foreach (EnginePointerPath path in profile.PointerPaths.Values)
        {
            if (!path.IsWellFormed(out string? problem) && problem is not null)
                problems.Add(problem);
        }

        foreach (KeyValuePair<EngineCapability, uint> context in profile.ContextOffsets)
        {
            if (context.Value == 0)
                problems.Add($"context_offset_zero:{context.Key}");

            if (!profile.Signatures.ContainsKey(context.Key))
                problems.Add($"context_offset_without_signature:{context.Key}");
        }

        return problems.Count == 0
            ? EngineProfileValidation.Valid
            : EngineProfileValidation.Invalid(problems);
    }

    public EngineProfileResolution Resolve(
        EngineClientProfile profile,
        ReadOnlySpan<byte> moduleImage,
        nuint moduleBase,
        int processId,
        EngineArchitecture expectedArchitecture,
        DateTime resolvedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(profile);

        EngineProfileValidation validation = Validate(profile);
        if (!validation.IsValid)
        {
            return new EngineProfileResolution(
                null,
                new EngineRefusal(EngineRefusalCode.ProfileInvalid, validation.ToString()),
                validation);
        }

        if (expectedArchitecture == EngineArchitecture.Unknown)
        {
            return new EngineProfileResolution(
                null,
                new EngineRefusal(EngineRefusalCode.ArchitectureMismatch, "target_architecture_unknown"),
                validation);
        }

        if (profile.Architecture != expectedArchitecture)
        {
            return new EngineProfileResolution(
                null,
                new EngineRefusal(
                    EngineRefusalCode.ArchitectureMismatch,
                    $"profile_is_{profile.Architecture}_target_is_{expectedArchitecture}"),
                validation);
        }

        if (moduleBase == 0)
        {
            return new EngineProfileResolution(
                null,
                new EngineRefusal(EngineRefusalCode.NotAttached, "module_base_unknown"),
                validation);
        }

        if (moduleImage.IsEmpty)
        {
            // An empty image resolves nothing, and reporting every signature as
            // "absent from this build" would blame the profile for a failed read.
            return new EngineProfileResolution(
                null,
                new EngineRefusal(EngineRefusalCode.NotAttached, "module_image_empty"),
                validation);
        }

        var resolutions = new List<EngineSignatureResolution>(profile.Signatures.Count);
        foreach (KeyValuePair<EngineCapability, EngineSignature> entry in profile.Signatures)
        {
            EngineSignature signature = entry.Value;
            int matches = EngineSignatureMatcher.Find(moduleImage, signature, out long offset);

            EngineSignatureResolution resolution = matches switch
            {
                1 => new EngineSignatureResolution(
                    entry.Key, signature.Name, moduleBase + (nuint)offset, 1, null),

                0 => new EngineSignatureResolution(
                    entry.Key, signature.Name, 0, 0,
                    new EngineRefusal(
                        EngineRefusalCode.SignatureUnresolved, $"signature_not_found:{signature.Name}")),

                _ => new EngineSignatureResolution(
                    entry.Key, signature.Name, 0, matches,
                    new EngineRefusal(
                        EngineRefusalCode.SignatureUnresolved,
                        $"signature_ambiguous:{signature.Name}:matches={matches}"))
            };

            resolutions.Add(resolution);
        }

        var resolved = new EngineResolvedProfile(
            profile.WithValidation(validation), processId, moduleBase, moduleImage.Length, resolutions, resolvedAtUtc);

        // A profile that located nothing is not a resolution, it is a mismatch: the
        // usual cause is a client build the profile was never written for, and saying
        // so here is more useful than eleven identical per-capability refusals later.
        return resolved.ResolvedCapabilities.Any()
            ? new EngineProfileResolution(resolved, null, validation)
            : new EngineProfileResolution(
                null,
                new EngineRefusal(
                    EngineRefusalCode.SignatureUnresolved,
                    $"no_signature_resolved:{profile.ClientVersion}"),
                validation);
    }
}
