using NosAi.Adapter.DirectEngine;

namespace NosAi.Runtime.Tests.DirectEngine;

/// <summary>
/// Builds the bytes a client module would present to a scan.
/// </summary>
/// <remarks>
/// <para>
/// Not a stand-in for a client: it is the real signatures from
/// <see cref="NosTaleLegacyProfile"/> laid into a buffer, so what the resolver
/// matches against here is byte-for-byte what it would match against in a live
/// process. The only thing supplied is the padding around them, and it is
/// <c>0xCC</c> — the byte MSVC fills unused code space with, and one that starts
/// none of these patterns, so the padding cannot produce a match of its own.
/// </para>
/// <para>
/// Identical patterns are laid down once. Pet and partner share an entry point in
/// the client, and duplicating it here would make both signatures ambiguous for a
/// reason the client does not have.
/// </para>
/// </remarks>
internal static class ClientModuleImage
{
    private const byte Padding = 0xCC;
    private const byte WildcardFiller = 0xCC;
    private const int Gap = 64;

    /// <summary>An image containing every signature the profile declares.</summary>
    internal static byte[] Containing(EngineClientProfile profile) =>
        Containing(profile, Array.Empty<EngineCapability>());

    /// <summary>
    /// An image containing every signature except those for <paramref name="omit"/>.
    /// </summary>
    /// <remarks>
    /// Omission is how "the profile is for another build" is reproduced honestly: the
    /// function simply is not there, which is exactly what a scan finds after a
    /// client patch.
    /// </remarks>
    internal static byte[] Containing(EngineClientProfile profile, IReadOnlyCollection<EngineCapability> omit)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(omit);

        var blocks = new List<byte[]>();
        foreach (KeyValuePair<EngineCapability, EngineSignature> entry in profile.Signatures)
        {
            if (omit.Contains(entry.Key))
                continue;

            byte[] block = Materialize(entry.Value);
            if (!blocks.Any(existing => existing.SequenceEqual(block)))
                blocks.Add(block);
        }

        var image = new List<byte>(Enumerable.Repeat(Padding, Gap));
        foreach (byte[] block in blocks)
        {
            image.AddRange(block);
            image.AddRange(Enumerable.Repeat(Padding, Gap));
        }

        return image.ToArray();
    }

    /// <summary>The bytes a client would hold for this signature, wildcards filled in.</summary>
    private static byte[] Materialize(EngineSignature signature)
    {
        var block = new byte[signature.Length];
        ReadOnlySpan<byte> pattern = signature.Pattern;
        for (int i = 0; i < block.Length; i++)
            block[i] = signature.Mask[i] == '?' ? WildcardFiller : pattern[i];

        return block;
    }

    /// <summary>Where a signature's bytes start in an image built by <see cref="Containing(EngineClientProfile)"/>.</summary>
    internal static int OffsetOf(byte[] image, EngineSignature signature)
    {
        byte[] block = Materialize(signature);
        for (int start = 0; start + block.Length <= image.Length; start++)
        {
            if (image.AsSpan(start, block.Length).SequenceEqual(block))
                return start;
        }

        return -1;
    }
}
