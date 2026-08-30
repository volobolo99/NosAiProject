using System.Security.Cryptography;

namespace NosAi.Runtime.Gate1;

/// <summary>Which side of the handshake produced a signature.</summary>
public enum HandshakeRole : byte
{
    /// <summary>The runtime, proving itself to the phone.</summary>
    Server = 0x01,

    /// <summary>The phone, proving itself to the runtime.</summary>
    Client = 0x02
}

/// <summary>
/// What each side actually signs during the Gate 1 handshake.
/// </summary>
/// <remarks>
/// <para>
/// Version 1 had the phone sign the server's raw 32-byte challenge. Three things
/// were wrong with that, and this type fixes all three.
/// </para>
/// <para>
/// <b>The phone was a signing oracle.</b> It signed whatever 32 bytes it was
/// handed, so anything that could get a phone to connect could obtain its
/// signature over a value of its choosing. Signing a hash of a fixed-format
/// transcript instead means a peer would need a preimage to steer the signed
/// value, which it cannot produce.
/// </para>
/// <para>
/// <b>Signatures were not bound to a session.</b> Including both nonces means a
/// signature captured from one session proves nothing in another.
/// </para>
/// <para>
/// <b>Signatures were not bound to a direction.</b> Without the role byte, a
/// signature harvested from the phone could be replayed back as the runtime's
/// proof, and the phone would accept its own signature as evidence it was talking
/// to a genuine runtime.
/// </para>
/// <para>
/// Version 3 (ADR-0009) adds both ephemeral key-agreement public keys to the
/// transcript. That is what authenticates the key exchange: an attacker who
/// substitutes an ephemeral key invalidates the signature carrying it, so the
/// session keys derived from it inherit the identities the RSA keys already
/// proved. No second handshake, no second trust root.
/// </para>
/// </remarks>
public static class SessionTranscript
{
    /// <summary>
    /// Domain separator. Changing it invalidates every existing signature, which
    /// is the point: a transcript from another protocol, or another version of
    /// this one, must never verify here.
    /// </summary>
    private static ReadOnlySpan<byte> Label => "NOSAI-GUARD-HANDSHAKE-V3"u8;

    /// <summary>Required length of each side's nonce.</summary>
    public const int NonceLength = 32;

    /// <summary>
    /// Length of an ephemeral P-256 public key on the wire: an uncompressed
    /// X9.62 point, <c>0x04 || X(32) || Y(32)</c>.
    /// </summary>
    public const int EphemeralKeyLength = 65;

    /// <summary>
    /// Role byte used when deriving keys rather than signing.
    /// </summary>
    /// <remarks>
    /// Not a valid <see cref="HandshakeRole"/>, deliberately: a key-derivation
    /// input can then never collide with a digest either side would put a
    /// signature on.
    /// </remarks>
    private const byte BindingRole = 0x00;

    public static byte[] CreateNonce() => RandomNumberGenerator.GetBytes(NonceLength);

    /// <summary>
    /// The digest the given role signs for this handshake.
    /// </summary>
    /// <remarks>
    /// Every field is included in the same order regardless of role, so the two
    /// sides compute over identical material and only the role byte differs. That
    /// is what keeps the two signatures non-interchangeable.
    /// </remarks>
    public static byte[] Compute(
        HandshakeRole role,
        ReadOnlySpan<byte> clientNonce,
        ReadOnlySpan<byte> serverNonce,
        ReadOnlySpan<byte> clientEphemeral,
        ReadOnlySpan<byte> serverEphemeral)
        => Digest((byte)role, clientNonce, serverNonce, clientEphemeral, serverEphemeral);

    /// <summary>
    /// The handshake binding used as HKDF salt when deriving session keys.
    /// </summary>
    /// <remarks>
    /// Identical material to <see cref="Compute"/> with the role byte set to
    /// <see cref="BindingRole"/>, so the derived keys are tied to exactly the
    /// handshake both sides signed. A peer that saw a different nonce or a
    /// different ephemeral key derives different keys and simply cannot decrypt.
    /// </remarks>
    public static byte[] ComputeBinding(
        ReadOnlySpan<byte> clientNonce,
        ReadOnlySpan<byte> serverNonce,
        ReadOnlySpan<byte> clientEphemeral,
        ReadOnlySpan<byte> serverEphemeral)
        => Digest(BindingRole, clientNonce, serverNonce, clientEphemeral, serverEphemeral);

    private static byte[] Digest(
        byte role,
        ReadOnlySpan<byte> clientNonce,
        ReadOnlySpan<byte> serverNonce,
        ReadOnlySpan<byte> clientEphemeral,
        ReadOnlySpan<byte> serverEphemeral)
    {
        if (clientNonce.Length != NonceLength)
            throw new ArgumentException($"Client nonce must be {NonceLength} bytes.", nameof(clientNonce));
        if (serverNonce.Length != NonceLength)
            throw new ArgumentException($"Server nonce must be {NonceLength} bytes.", nameof(serverNonce));
        if (clientEphemeral.Length != EphemeralKeyLength)
            throw new ArgumentException($"Client ephemeral key must be {EphemeralKeyLength} bytes.", nameof(clientEphemeral));
        if (serverEphemeral.Length != EphemeralKeyLength)
            throw new ArgumentException($"Server ephemeral key must be {EphemeralKeyLength} bytes.", nameof(serverEphemeral));

        Span<byte> buffer = stackalloc byte[Label.Length + 3 + (NonceLength * 2) + (EphemeralKeyLength * 2)];
        var offset = 0;

        Label.CopyTo(buffer[offset..]);
        offset += Label.Length;

        // Separators keep the fields unambiguous: without them a different split
        // of the same bytes could produce the same digest.
        buffer[offset++] = 0x00;
        buffer[offset++] = role;
        buffer[offset++] = 0x00;

        clientNonce.CopyTo(buffer[offset..]);
        offset += NonceLength;
        serverNonce.CopyTo(buffer[offset..]);
        offset += NonceLength;
        clientEphemeral.CopyTo(buffer[offset..]);
        offset += EphemeralKeyLength;
        serverEphemeral.CopyTo(buffer[offset..]);

        return SHA256.HashData(buffer);
    }

    public static byte[] Sign(
        RSA key,
        HandshakeRole role,
        ReadOnlySpan<byte> clientNonce,
        ReadOnlySpan<byte> serverNonce,
        ReadOnlySpan<byte> clientEphemeral,
        ReadOnlySpan<byte> serverEphemeral)
        => key.SignHash(
            Compute(role, clientNonce, serverNonce, clientEphemeral, serverEphemeral),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

    public static bool Verify(
        RSA key,
        HandshakeRole role,
        ReadOnlySpan<byte> clientNonce,
        ReadOnlySpan<byte> serverNonce,
        ReadOnlySpan<byte> clientEphemeral,
        ReadOnlySpan<byte> serverEphemeral,
        ReadOnlySpan<byte> signature)
    {
        try
        {
            return key.VerifyHash(
                Compute(role, clientNonce, serverNonce, clientEphemeral, serverEphemeral),
                signature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
