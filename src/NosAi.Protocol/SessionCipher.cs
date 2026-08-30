using System.Buffers.Binary;
using System.Security.Cryptography;

namespace NosAi.Runtime.Gate1;

/// <summary>
/// The ephemeral key agreement that gives the Gate 1 session its keys.
/// </summary>
/// <remarks>
/// <para>
/// ADR-0009. Each side generates a P-256 key pair per session and sends the
/// public half inside the handshake. Both halves are covered by the transcript
/// that each side already signs with its long-term RSA key, so the agreement is
/// authenticated without a second handshake: substituting an ephemeral key
/// invalidates the signature that carried it.
/// </para>
/// <para>
/// P-256 rather than X25519 because this assembly is deliberately
/// dependency-free — it compiles for a Windows runtime and an Android
/// application — and the .NET BCL has no Curve25519. Two divergent
/// implementations of one agreement would be worse than the curve choice.
/// </para>
/// <para>
/// The private half is never persisted. That is the point: a long-term key file
/// stolen later cannot decrypt traffic recorded today.
/// </para>
/// </remarks>
public sealed class EphemeralKeyExchange : IDisposable
{
    /// <summary>Uncompressed X9.62 point marker.</summary>
    private const byte UncompressedPointPrefix = 0x04;

    private const int CoordinateLength = 32;

    /// <summary>HKDF info string. Shared verbatim with the Python side.</summary>
    private static ReadOnlySpan<byte> KeyScheduleInfo => "NOSAI-GUARD-SESSION-V3"u8;

    /// <summary>Bytes of key material the schedule produces: two directional keys.</summary>
    public const int SessionMaterialLength = SessionCipher.KeyLength * 2;

    private readonly ECDiffieHellman _key;
    private bool _disposed;

    private EphemeralKeyExchange(ECDiffieHellman key)
    {
        _key = key;
        ECParameters parameters = key.ExportParameters(includePrivateParameters: false);
        PublicKey = new byte[SessionTranscript.EphemeralKeyLength];
        PublicKey[0] = UncompressedPointPrefix;
        // X and Y are already fixed-width for a named curve, but a leading-zero
        // coordinate must still land right-aligned or the peer derives a different
        // point from the same key.
        parameters.Q.X!.CopyTo(PublicKey.AsSpan(1 + CoordinateLength - parameters.Q.X!.Length));
        parameters.Q.Y!.CopyTo(PublicKey.AsSpan(1 + CoordinateLength + CoordinateLength - parameters.Q.Y!.Length));
    }

    /// <summary>This side's ephemeral public key, as it appears on the wire.</summary>
    public byte[] PublicKey { get; }

    public static EphemeralKeyExchange Create()
        => new(ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256));

    /// <summary>
    /// Derives the 64 bytes of session material from the peer's ephemeral key.
    /// </summary>
    /// <remarks>
    /// The handshake binding is the HKDF salt, so the keys are tied to the exact
    /// nonces and ephemeral keys both sides signed. A peer that saw anything else
    /// derives different keys and cannot decrypt — a mismatch shows up as a failed
    /// tag, never as a plausible-looking wrong plaintext.
    /// </remarks>
    /// <exception cref="CryptographicException">
    /// The peer key is not a valid point on P-256.
    /// </exception>
    public byte[] DeriveSessionMaterial(ReadOnlySpan<byte> peerPublicKey, ReadOnlySpan<byte> binding)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!TryImportPeer(peerPublicKey, out ECDiffieHellman? peer))
            throw new CryptographicException("The peer ephemeral key is not a valid P-256 public point.");

        using (peer)
        {
            // DeriveKeyFromHash with no prepend/append is SHA-256(Z), where Z is the
            // raw X coordinate of the agreement. Python computes the same digest over
            // the same 32 bytes; the parity is pinned by tests on both sides.
            byte[] agreementDigest = _key.DeriveKeyFromHash(peer.PublicKey, HashAlgorithmName.SHA256);
            try
            {
                return Schedule(agreementDigest, binding);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(agreementDigest);
            }
        }
    }

    /// <summary>
    /// The key schedule itself: HKDF over the agreement digest, salted with the
    /// handshake binding.
    /// </summary>
    /// <remarks>
    /// Separate from the agreement so a known-answer test can pin it without
    /// having to manufacture a fixed "ephemeral" key, which would undermine the
    /// one property — freshness — the ephemeral exchange exists to provide.
    /// </remarks>
    public static byte[] Schedule(ReadOnlySpan<byte> agreementDigest, ReadOnlySpan<byte> binding)
        => HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            agreementDigest.ToArray(),
            SessionMaterialLength,
            salt: binding.ToArray(),
            info: KeyScheduleInfo.ToArray());

    /// <summary>
    /// Whether the bytes are a well-formed uncompressed P-256 point.
    /// </summary>
    /// <remarks>
    /// Validation is not a formality: an unchecked point lets a peer pick a
    /// small-order or off-curve value and steer the agreement.
    /// </remarks>
    public static bool IsValidPublicKey(ReadOnlySpan<byte> publicKey)
    {
        if (!TryImportPeer(publicKey, out ECDiffieHellman? peer))
            return false;
        peer.Dispose();
        return true;
    }

    private static bool TryImportPeer(ReadOnlySpan<byte> publicKey, out ECDiffieHellman peer)
    {
        peer = null!;
        if (publicKey.Length != SessionTranscript.EphemeralKeyLength || publicKey[0] != UncompressedPointPrefix)
            return false;

        var parameters = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = publicKey.Slice(1, CoordinateLength).ToArray(),
                Y = publicKey.Slice(1 + CoordinateLength, CoordinateLength).ToArray()
            }
        };

        try
        {
            // Validate rejects a point that is not on the curve; Create rejects what
            // the platform will not agree over.
            parameters.Validate();
            peer = ECDiffieHellman.Create(parameters);
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _key.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// Authenticated encryption of the Gate 1 session payload (ADR-0009).
/// </summary>
/// <remarks>
/// <para>
/// The 12-byte header stays in clear — the stream cannot be framed otherwise —
/// and is passed as associated data, so the message type, declared length and
/// sequence number are authenticated even though they are readable. The payload
/// becomes <c>nonce(12) || ciphertext || tag(16)</c>.
/// </para>
/// <para>
/// Keys are directional. With a single key a frame captured in one direction
/// could be replayed down the other and would decrypt.
/// </para>
/// <para>
/// The nonce is a per-direction counter that never wraps: at exhaustion the
/// sender refuses to encrypt rather than repeat one, because a repeated nonce
/// under GCM forfeits both confidentiality and integrity. The receiver requires
/// the nonce to be exactly the one it expects, which leaves the peer no freedom
/// to choose it while keeping a captured frame decryptable on its own.
/// </para>
/// </remarks>
public sealed class SessionCipher : IDisposable
{
    public const int KeyLength = 32;
    public const int NonceLength = 12;
    public const int TagLength = 16;

    /// <summary>Bytes a sealed payload adds to the plaintext.</summary>
    public const int Overhead = NonceLength + TagLength;

    /// <summary>Largest plaintext that still fits the uint16 payload length.</summary>
    public const int MaxPlaintextLength = WireHeader.MaxPayloadLength - Overhead;

    /// <summary>Counter width inside the nonce, big-endian, after four zero bytes.</summary>
    private const int CounterOffset = 4;

    private readonly AesGcm _send;
    private readonly AesGcm _receive;
    private readonly object _sync = new();
    private ulong _sendCounter;
    private ulong _receiveCounter;
    private bool _disposed;

    private SessionCipher(ReadOnlySpan<byte> sendKey, ReadOnlySpan<byte> receiveKey)
    {
        _send = new AesGcm(sendKey, TagLength);
        _receive = new AesGcm(receiveKey, TagLength);
    }

    /// <summary>The runtime's half: sends server→client, receives client→server.</summary>
    public static SessionCipher ForRuntime(ReadOnlySpan<byte> sessionMaterial)
    {
        RequireMaterial(sessionMaterial);
        return new SessionCipher(sessionMaterial[KeyLength..], sessionMaterial[..KeyLength]);
    }

    /// <summary>The phone's half: sends client→server, receives server→client.</summary>
    public static SessionCipher ForPhone(ReadOnlySpan<byte> sessionMaterial)
    {
        RequireMaterial(sessionMaterial);
        return new SessionCipher(sessionMaterial[..KeyLength], sessionMaterial[KeyLength..]);
    }

    private static void RequireMaterial(ReadOnlySpan<byte> material)
    {
        if (material.Length != EphemeralKeyExchange.SessionMaterialLength)
            throw new ArgumentException(
                $"Session material must be {EphemeralKeyExchange.SessionMaterialLength} bytes.",
                nameof(material));
    }

    /// <summary>
    /// Builds one complete encrypted frame: header in clear, payload sealed under it.
    /// </summary>
    /// <remarks>
    /// The header is written here rather than taken from the caller so the
    /// associated data is guaranteed to be the very bytes that go on the wire.
    /// A frame authenticated against a header it was not sent with would be a
    /// silent hole.
    /// </remarks>
    public byte[] SealFrame(WireMessageType type, uint sequence, ReadOnlySpan<byte> plaintext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (plaintext.Length > MaxPlaintextLength)
            throw new InvalidDataException($"payload_too_large:{plaintext.Length}");

        var frame = new byte[WireHeader.HeaderSize + Overhead + plaintext.Length];
        var header = frame.AsSpan(0, WireHeader.HeaderSize);
        new WireHeader(type, checked((ushort)(Overhead + plaintext.Length)), sequence).WriteTo(header);

        lock (_sync)
        {
            if (_sendCounter == ulong.MaxValue)
                throw new InvalidOperationException("nonce_space_exhausted");

            var nonce = frame.AsSpan(WireHeader.HeaderSize, NonceLength);
            BinaryPrimitives.WriteUInt64BigEndian(nonce[CounterOffset..], _sendCounter);
            var ciphertext = frame.AsSpan(WireHeader.HeaderSize + NonceLength, plaintext.Length);
            var tag = frame.AsSpan(WireHeader.HeaderSize + NonceLength + plaintext.Length, TagLength);

            _send.Encrypt(nonce, plaintext, ciphertext, tag, header);
            _sendCounter++;
        }

        return frame;
    }

    /// <summary>
    /// Opens a sealed payload against the header it arrived with.
    /// </summary>
    /// <remarks>
    /// Fail-closed and structured: <paramref name="reason"/> names what was wrong
    /// so the session terminates with something an operator can act on, and the
    /// receive counter only advances on a frame that actually authenticated.
    /// </remarks>
    public bool TryOpenFrame(
        ReadOnlySpan<byte> headerBytes,
        ReadOnlySpan<byte> payload,
        out byte[] plaintext,
        out string? reason)
    {
        plaintext = Array.Empty<byte>();
        reason = null;

        if (_disposed)
        {
            reason = "cipher_disposed";
            return false;
        }

        if (headerBytes.Length != WireHeader.HeaderSize)
        {
            reason = "invalid_header_length";
            return false;
        }

        if (payload.Length < Overhead)
        {
            reason = "encrypted_payload_too_short";
            return false;
        }

        lock (_sync)
        {
            Span<byte> expectedNonce = stackalloc byte[NonceLength];
            BinaryPrimitives.WriteUInt64BigEndian(expectedNonce[CounterOffset..], _receiveCounter);

            var nonce = payload[..NonceLength];
            if (!CryptographicOperations.FixedTimeEquals(nonce, expectedNonce))
            {
                reason = "nonce_out_of_order";
                return false;
            }

            int cipherLength = payload.Length - Overhead;
            var ciphertext = payload.Slice(NonceLength, cipherLength);
            var tag = payload[(NonceLength + cipherLength)..];
            var opened = new byte[cipherLength];

            try
            {
                _receive.Decrypt(nonce, ciphertext, tag, opened, headerBytes);
            }
            catch (CryptographicException)
            {
                // Wrong key, tampered ciphertext, or a header that does not match
                // what the sender authenticated. All of them are the same answer.
                reason = "authentication_failed";
                return false;
            }

            if (_receiveCounter == ulong.MaxValue)
            {
                reason = "nonce_space_exhausted";
                return false;
            }

            _receiveCounter++;
            plaintext = opened;
            return true;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _send.Dispose();
        _receive.Dispose();
        _disposed = true;
    }
}
