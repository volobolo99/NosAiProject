using System.Buffers.Binary;
using System.Security.Cryptography;

namespace NosAi.Security;

/// <summary>
/// A CapBAC capability grant (docs/ROADMAP_ESECUTIVA.md S:2.2-2.3): a subject,
/// a scope bitmask, a validity window, and a MAC binding all three together.
/// </summary>
/// <param name="Mac">
/// <c>HMAC-SHA256(K_root, canonical form)</c> over the full 32-byte digest
/// (unlike the frame <see cref="NosFrameHeader.Tag"/>, CapBAC's tag is not
/// truncated: <see cref="SequenceGuard"/> is what makes truncating the frame
/// tag to 32 bits safe against brute force, and nothing plays that role here).
/// </param>
public readonly record struct CapabilityToken(
    ulong SubjectId,
    uint Scope,
    long NotBeforeUnixMs,
    long NotAfterUnixMs,
    ReadOnlyMemory<byte> Mac)
{
    /// <summary>Fixed length of the canonical, big-endian form the Mac signs: SubjectId(8) + Scope(4) + NotBefore(8) + NotAfter(8).</summary>
    public const int CanonicalLength = 8 + 4 + 8 + 8;

    /// <summary>On-the-wire size of a token: canonical form plus the 32-byte HMAC.</summary>
    public const int WireLength = CanonicalLength + 32;

    /// <summary>Writes the canonical big-endian form this token's Mac is computed over.</summary>
    public void WriteCanonicalForm(Span<byte> destination)
    {
        if (destination.Length < CanonicalLength)
            throw new ArgumentException($"Destination must be at least {CanonicalLength} bytes.", nameof(destination));

        BinaryPrimitives.WriteUInt64BigEndian(destination[..8], SubjectId);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(8, 4), Scope);
        BinaryPrimitives.WriteInt64BigEndian(destination.Slice(12, 8), NotBeforeUnixMs);
        BinaryPrimitives.WriteInt64BigEndian(destination.Slice(20, 8), NotAfterUnixMs);
    }

    /// <summary>
    /// Issues a token signed under <paramref name="rootKey"/>. This is
    /// operator/test tooling, not a production issuance path: Gate 1 defines
    /// only validation (<see cref="ICapabilityValidator"/>), not an issuance
    /// service.
    /// </summary>
    public static CapabilityToken Issue(ulong subjectId, uint scope, long notBeforeUnixMs, long notAfterUnixMs, ReadOnlySpan<byte> rootKey)
    {
        var token = new CapabilityToken(subjectId, scope, notBeforeUnixMs, notAfterUnixMs, ReadOnlyMemory<byte>.Empty);

        Span<byte> canonical = stackalloc byte[CanonicalLength];
        token.WriteCanonicalForm(canonical);

        byte[] mac = HMACSHA256.HashData(rootKey, canonical);
        return token with { Mac = mac };
    }

    /// <summary>Writes the 60-byte wire form (canonical fields + MAC) into <paramref name="destination"/>.</summary>
    public int WriteTo(Span<byte> destination)
    {
        if (destination.Length < WireLength)
            throw new ArgumentException($"Destination must be at least {WireLength} bytes.", nameof(destination));

        WriteCanonicalForm(destination);
        ReadOnlySpan<byte> mac = Mac.Span;
        if (mac.Length != 32)
            throw new InvalidOperationException("A capability token on the wire must carry a 32-byte MAC.");

        mac.CopyTo(destination.Slice(CanonicalLength, 32));
        return WireLength;
    }

    public static bool TryRead(ReadOnlySpan<byte> source, out CapabilityToken token)
    {
        token = default;
        if (source.Length != WireLength)
            return false;

        token = new CapabilityToken(
            BinaryPrimitives.ReadUInt64BigEndian(source[..8]),
            BinaryPrimitives.ReadUInt32BigEndian(source.Slice(8, 4)),
            BinaryPrimitives.ReadInt64BigEndian(source.Slice(12, 8)),
            BinaryPrimitives.ReadInt64BigEndian(source.Slice(20, 8)),
            source.Slice(CanonicalLength, 32).ToArray());
        return true;
    }
}
