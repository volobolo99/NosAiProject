using System.Buffers.Binary;
using System.Security.Cryptography;
using NosAi.Core;

namespace NosAi.Security;

/// <summary>The result of a CapBAC check: whether it passed, why not if it did not, and the scope actually granted.</summary>
public readonly record struct CapabilityVerdict(bool Granted, FaultCode Fault, uint EffectiveScope)
{
    public const int WireLength = 1 + 2 + 4;

    public int WriteTo(Span<byte> destination)
    {
        if (destination.Length < WireLength)
            throw new ArgumentException($"Destination must be at least {WireLength} bytes.", nameof(destination));

        destination[0] = Granted ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(1, 2), (ushort)Fault);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(3, 4), EffectiveScope);
        return WireLength;
    }

    public static bool TryRead(ReadOnlySpan<byte> source, out CapabilityVerdict verdict)
    {
        verdict = default;
        if (source.Length != WireLength)
            return false;

        verdict = new CapabilityVerdict(
            source[0] != 0,
            (FaultCode)BinaryPrimitives.ReadUInt16BigEndian(source.Slice(1, 2)),
            BinaryPrimitives.ReadUInt32BigEndian(source.Slice(3, 4)));
        return true;
    }
}

/// <summary>Validates a <see cref="CapabilityToken"/> against a requested scope, at a given time.</summary>
public interface ICapabilityValidator
{
    CapabilityVerdict Validate(in CapabilityToken token, PipelineStage stage, uint requestedScope, long nowUnixMs);
}

/// <summary>
/// CapBAC validator (docs/ROADMAP_ESECUTIVA.md S:2.3). Three independent
/// checks, all mandatory: signature integrity, time window with clock-skew
/// tolerance, and scope containment (<c>(child &amp; ~parent) == 0</c>, applied
/// here as <c>requestedScope</c> being the child of the token's granted
/// <c>Scope</c>). There is no path that grants on a subset of the three.
/// </summary>
public sealed class HmacCapabilityValidator : ICapabilityValidator
{
    /// <summary>+/-2000 ms clock-skew tolerance; beyond it the token is rejected with <see cref="FaultCode.Timeout"/>.</summary>
    public const long ClockSkewToleranceMs = 2000;

    private readonly byte[] _rootKey;

    public HmacCapabilityValidator(ReadOnlySpan<byte> rootKey)
    {
        if (rootKey.IsEmpty)
            throw new ArgumentException("Root key must not be empty.", nameof(rootKey));

        _rootKey = rootKey.ToArray();
    }

    public CapabilityVerdict Validate(in CapabilityToken token, PipelineStage stage, uint requestedScope, long nowUnixMs)
    {
        // docs/ROADMAP_ESECUTIVA.md does not define a stage-to-scope-bit mapping
        // at Gate 1 (that belongs to whichever later Gate assigns scope bits to
        // stages); 'stage' is accepted for the caller's audit context only and
        // does not change the decision below.
        _ = stage;

        if (!VerifySignature(token))
            return new CapabilityVerdict(false, FaultCode.ScopeDenied, 0);

        if (nowUnixMs < token.NotBeforeUnixMs - ClockSkewToleranceMs || nowUnixMs > token.NotAfterUnixMs + ClockSkewToleranceMs)
            return new CapabilityVerdict(false, FaultCode.Timeout, 0);

        if ((requestedScope & ~token.Scope) != 0)
            return new CapabilityVerdict(false, FaultCode.ScopeDenied, 0);

        return new CapabilityVerdict(true, FaultCode.None, requestedScope & token.Scope);
    }

    private bool VerifySignature(in CapabilityToken token)
    {
        Span<byte> canonical = stackalloc byte[CapabilityToken.CanonicalLength];
        token.WriteCanonicalForm(canonical);

        Span<byte> expected = stackalloc byte[32];
        HMACSHA256.HashData(_rootKey, canonical, expected);

        ReadOnlySpan<byte> actual = token.Mac.Span;
        return actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
