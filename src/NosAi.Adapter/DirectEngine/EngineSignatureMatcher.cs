namespace NosAi.Adapter.DirectEngine;

/// <summary>Finds a signature inside a copy of the client module.</summary>
/// <remarks>
/// <para>
/// A pure function over bytes, on purpose. The reference scanned by dereferencing
/// addresses in its own address space while it was scanning them, which is both
/// the reason it could only ever run injected and the reason nothing about it
/// could be tested. Here the caller supplies the bytes — from
/// <see cref="IGameProcessAdapter.ReadRegion"/> against a live client, or from a
/// captured module in a test — and the matching itself has no opinion about where
/// they came from.
/// </para>
/// <para>
/// <b>Ambiguity is a failure, not a first hit.</b> The reference returned the first
/// address that matched and never looked further, so a signature loose enough to
/// occur twice resolved to whichever came first in memory. This reports how many
/// matches exist, and the resolver refuses a signature that does not identify one
/// place uniquely.
/// </para>
/// </remarks>
public static class EngineSignatureMatcher
{
    /// <summary>Stops counting past this many matches; two is already a refusal.</summary>
    private const int MatchCountCeiling = 8;

    /// <summary>
    /// Where <paramref name="signature"/> occurs in <paramref name="image"/>, and how often.
    /// </summary>
    /// <param name="image">The module bytes, starting at its base.</param>
    /// <param name="signature">The pattern and mask to look for. Must be well formed.</param>
    /// <param name="offset">Offset of the first match from the start of <paramref name="image"/>.</param>
    /// <returns>
    /// The number of matches found, capped at <see cref="MatchCountCeiling"/>. Zero
    /// means unresolved, one means located, more than one means the signature does
    /// not identify anything.
    /// </returns>
    public static int Find(ReadOnlySpan<byte> image, EngineSignature signature, out long offset)
    {
        ArgumentNullException.ThrowIfNull(signature);

        offset = -1;

        if (!signature.IsWellFormed(out _))
            return 0;

        ReadOnlySpan<byte> pattern = signature.Pattern;
        string mask = signature.Mask;
        int length = pattern.Length;

        if (image.Length < length)
            return 0;

        int matches = 0;
        int last = image.Length - length;
        for (int start = 0; start <= last; start++)
        {
            if (!MatchesAt(image, start, pattern, mask))
                continue;

            if (matches == 0)
                offset = start;

            if (++matches >= MatchCountCeiling)
                break;
        }

        return matches;
    }

    private static bool MatchesAt(ReadOnlySpan<byte> image, int start, ReadOnlySpan<byte> pattern, string mask)
    {
        for (int i = 0; i < pattern.Length; i++)
        {
            if (mask[i] == '?')
                continue;

            if (image[start + i] != pattern[i])
                return false;
        }

        return true;
    }
}
