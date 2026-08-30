using System.Security.Cryptography;
using System.Text;

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
/// </remarks>
public static class SessionTranscript
{
    /// <summary>
    /// Domain separator. Changing it invalidates every existing signature, which
    /// is the point: a transcript from another protocol, or another version of
    /// this one, must never verify here.
    /// </summary>
    private static ReadOnlySpan<byte> Label => "NOSAI-GUARD-HANDSHAKE-V2"u8;

    /// <summary>Required length of each side's nonce.</summary>
    public const int NonceLength = 32;

    public static byte[] CreateNonce() => RandomNumberGenerator.GetBytes(NonceLength);

    /// <summary>
    /// The digest the given role signs for this pair of nonces.
    /// </summary>
    /// <remarks>
    /// Both nonces are always included in the same order regardless of role, so
    /// the two sides compute over identical material and only the role byte
    /// differs. That is what keeps the two signatures non-interchangeable.
    /// </remarks>
    public static byte[] Compute(HandshakeRole role, ReadOnlySpan<byte> clientNonce, ReadOnlySpan<byte> serverNonce)
    {
        if (clientNonce.Length != NonceLength)
            throw new ArgumentException($"Client nonce must be {NonceLength} bytes.", nameof(clientNonce));
        if (serverNonce.Length != NonceLength)
            throw new ArgumentException($"Server nonce must be {NonceLength} bytes.", nameof(serverNonce));

        Span<byte> buffer = stackalloc byte[Label.Length + 1 + 1 + 1 + NonceLength + NonceLength];
        var offset = 0;

        Label.CopyTo(buffer[offset..]);
        offset += Label.Length;

        // Separators keep the fields unambiguous: without them a different split
        // of the same bytes could produce the same digest.
        buffer[offset++] = 0x00;
        buffer[offset++] = (byte)role;
        buffer[offset++] = 0x00;

        clientNonce.CopyTo(buffer[offset..]);
        offset += NonceLength;
        serverNonce.CopyTo(buffer[offset..]);

        return SHA256.HashData(buffer);
    }

    public static byte[] Sign(RSA key, HandshakeRole role, ReadOnlySpan<byte> clientNonce, ReadOnlySpan<byte> serverNonce)
        => key.SignHash(Compute(role, clientNonce, serverNonce), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

    public static bool Verify(RSA key, HandshakeRole role, ReadOnlySpan<byte> clientNonce, ReadOnlySpan<byte> serverNonce, ReadOnlySpan<byte> signature)
    {
        try
        {
            return key.VerifyHash(
                Compute(role, clientNonce, serverNonce),
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
