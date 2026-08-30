// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Versione: 1.0 Beta
// Sicurezza — Sessione cifrata a chiavi effimere: X25519 + HKDF-SHA256 +
//             ChaCha20-Poly1305 (variante v1.9, controparte C# di
//             nosai/security/ephemeral_session.py)
// ============================================================================
//
// Questo modulo implementa il nucleo effimero (chiave statica server + chiave
// effimera client, derivazione di sessione, AEAD). NON è un'implementazione
// completa del Noise Protocol Framework: un handshake Noise KK/IK conforme
// richiede una macchina a stati del pattern e test di interoperabilità
// (docs/CRITTOGRAFIA_NOISE_E_CHIAVI_EFFIMERE.md). Il formato wire è compatibile
// con la controparte Python: nonce(12) || ciphertext || tag(16).

using System;
using System.Security.Cryptography;
using Org.BouncyCastle.Math.EC.Rfc7748;

namespace NosAi.Runtime.Security;

/// <summary>
/// An X25519 key. The private scalar is held in a rented buffer and zeroed on
/// dispose. Private key bytes must never be committed or logged; callers persist
/// them only through a local secrets store
/// (docs/CRITTOGRAFIA_NOISE_E_CHIAVI_EFFIMERE.md).
/// </summary>
public sealed class X25519Identity : IDisposable
{
    public const int KeySize = 32;

    private readonly byte[] _privateKey;
    private bool _disposed;

    private X25519Identity(byte[] privateKey)
    {
        _privateKey = privateKey;
        PublicKey = new byte[KeySize];
        X25519.ScalarMultBase(_privateKey, 0, PublicKey, 0);
    }

    /// <summary>The raw 32-byte X25519 public key, safe to share.</summary>
    public byte[] PublicKey { get; }

    public static X25519Identity Generate()
    {
        byte[] priv = new byte[KeySize];
        RandomNumberGenerator.Fill(priv);
        // RFC 7748 clamping. BouncyCastle also clamps internally, but clamping the
        // stored scalar keeps the persisted private key canonical.
        priv[0] &= 0xF8;
        priv[31] &= 0x7F;
        priv[31] |= 0x40;
        return new X25519Identity(priv);
    }

    public static X25519Identity FromPrivateKey(ReadOnlySpan<byte> privateKey)
    {
        if (privateKey.Length != KeySize)
            throw new ArgumentException($"An X25519 private key is {KeySize} bytes.", nameof(privateKey));
        return new X25519Identity(privateKey.ToArray());
    }

    /// <summary>
    /// Exports the raw private key. It must be handled as a secret: never logged,
    /// never committed. Prefer <see cref="Agree"/> over exporting when possible.
    /// </summary>
    public byte[] ExportPrivateKey()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return (byte[])_privateKey.Clone();
    }

    /// <summary>Raw X25519 Diffie–Hellman with a peer public key.</summary>
    public byte[] Agree(ReadOnlySpan<byte> peerPublicKey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (peerPublicKey.Length != KeySize)
            throw new ArgumentException($"An X25519 public key is {KeySize} bytes.", nameof(peerPublicKey));
        byte[] shared = new byte[KeySize];
        if (!X25519.CalculateAgreement(_privateKey, 0, peerPublicKey.ToArray(), 0, shared, 0))
        {
            CryptographicOperations.ZeroMemory(shared);
            throw new CryptographicException("X25519 agreement failed (peer public key is a small-order point).");
        }
        return shared;
    }

    public void Dispose()
    {
        if (_disposed) return;
        CryptographicOperations.ZeroMemory(_privateKey);
        _disposed = true;
    }
}

/// <summary>
/// Authenticated session over a 32-byte key derived from X25519 + HKDF-SHA256.
/// The monotonic counter guarantees nonce uniqueness in one direction; peer
/// identity must be authenticated by project policy before a session is trusted.
/// </summary>
public sealed class EphemeralSession : IDisposable
{
    /// <summary>HKDF info string, shared with the Python side for wire parity.</summary>
    public static readonly byte[] Prologue = "NOS_AI_PROTOCOL_V1"u8.ToArray();

    public const int NonceSize = 12;
    public const int TagSize = 16;
    public const int KeySize = 32;

    private readonly ChaCha20Poly1305 _aead;
    private readonly byte[] _sessionKey;
    private ulong _counter;
    private bool _disposed;

    public EphemeralSession(ReadOnlySpan<byte> sessionKey)
    {
        if (sessionKey.Length != KeySize)
            throw new ArgumentException($"The session key must be {KeySize} bytes.", nameof(sessionKey));
        if (!ChaCha20Poly1305.IsSupported)
            throw new PlatformNotSupportedException("ChaCha20-Poly1305 is not available on this platform.");
        _sessionKey = sessionKey.ToArray();
        _aead = new ChaCha20Poly1305(_sessionKey);
    }

    /// <summary>
    /// Derives a session from a local X25519 key and a peer public key. Both ends
    /// compute the same shared secret, so both derive the same session key.
    /// </summary>
    public static EphemeralSession FromX25519(X25519Identity local, ReadOnlySpan<byte> peerPublicKey)
    {
        ArgumentNullException.ThrowIfNull(local);
        byte[] shared = local.Agree(peerPublicKey);
        try
        {
            return new EphemeralSession(DeriveSessionKey(shared));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(shared);
        }
    }

    /// <summary>HKDF-SHA256(shared, salt=zeros, info=prologue) → 32-byte key.</summary>
    public static byte[] DeriveSessionKey(ReadOnlySpan<byte> sharedSecret, ReadOnlySpan<byte> prologue = default)
    {
        byte[] info = prologue.IsEmpty ? Prologue : prologue.ToArray();
        // salt: default (null) means HashLen zeros per RFC 5869, matching the
        // Python cryptography HKDF(salt=None) used on the other end.
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret.ToArray(), KeySize, salt: null, info: info);
    }

    private byte[] NextNonce()
    {
        // Nonce layout matches the Python side: 4 zero bytes + 8-byte big-endian
        // counter. The counter must never repeat under one key.
        if (_counter == ulong.MaxValue)
            throw new InvalidOperationException("Nonce space exhausted: establish a new session.");
        byte[] nonce = new byte[NonceSize];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), _counter);
        _counter++;
        return nonce;
    }

    /// <summary>Encrypts a payload; returns nonce(12) || ciphertext || tag(16).</summary>
    public byte[] Encrypt(ReadOnlySpan<byte> payload, ReadOnlySpan<byte> associatedData = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        byte[] nonce = NextNonce();
        byte[] packet = new byte[NonceSize + payload.Length + TagSize];
        nonce.CopyTo(packet.AsSpan(0, NonceSize));
        var ciphertext = packet.AsSpan(NonceSize, payload.Length);
        var tag = packet.AsSpan(NonceSize + payload.Length, TagSize);
        _aead.Encrypt(nonce, payload, ciphertext, tag, associatedData);
        return packet;
    }

    /// <summary>
    /// Decrypts a nonce(12) || ciphertext || tag(16) packet. A tampered packet or
    /// tampered associated data fails authentication and throws.
    /// </summary>
    public byte[] Decrypt(ReadOnlySpan<byte> packet, ReadOnlySpan<byte> associatedData = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (packet.Length < NonceSize + TagSize)
            throw new ArgumentException("Encrypted packet is too short.", nameof(packet));
        var nonce = packet[..NonceSize];
        int cipherLength = packet.Length - NonceSize - TagSize;
        var ciphertext = packet.Slice(NonceSize, cipherLength);
        var tag = packet.Slice(NonceSize + cipherLength, TagSize);
        byte[] plaintext = new byte[cipherLength];
        _aead.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
        return plaintext;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _aead.Dispose();
        CryptographicOperations.ZeroMemory(_sessionKey);
        _disposed = true;
    }
}

/// <summary>Static X25519 keypair generation for enrollment/onboarding.</summary>
public static class StaticKeyProvisioning
{
    /// <summary>
    /// Generates a static X25519 keypair in raw form. The private key must never
    /// be committed to the repository; store it in a local secrets store.
    /// </summary>
    public static (byte[] PrivateKey, byte[] PublicKey) GenerateStaticKeypair()
    {
        using var identity = X25519Identity.Generate();
        return (identity.ExportPrivateKey(), identity.PublicKey);
    }
}
