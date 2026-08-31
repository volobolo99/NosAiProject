// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Sicurezza — Noise_IK_25519_ChaChaPoly_SHA256: handshake conforme,
//             transport with explicit nonces and an anti-replay window
// ============================================================================
//
// An implementation of the Noise Protocol Framework (rev. 34) limited to the IK
// pattern, the one the v1.9 specification calls for
// (docs/CRITTOGRAFIA_NOISE_E_CHIAVI_EFFIMERE.md).
//
// Mind the nonce: Noise mandates a little-endian counter in the last 8 bytes of
// the 12-byte nonce. The legacy ephemeral session (EphemeralSession) uses
// big-endian instead, for parity with the Python counterpart: they are two
// distinct protocols and the two formats must not be mixed.

using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace NosAi.Runtime.Security;

/// <summary>Noise CipherState: an AEAD key plus its monotonic nonce counter.</summary>
public sealed class NoiseCipherState : IDisposable
{
    public const int KeySize = 32;
    public const int TagSize = 16;
    public const int NonceSize = 12;

    /// <summary>Noise reserves the maximum nonce; reaching it must end the session.</summary>
    public const ulong MaxNonce = ulong.MaxValue;

    private byte[]? _key;
    private ChaCha20Poly1305? _aead;
    private ulong _nonce;
    private bool _disposed;

    public bool HasKey => _key is not null;
    public ulong Nonce => _nonce;

    internal NoiseCipherState() { }

    internal NoiseCipherState(ReadOnlySpan<byte> key) => InitializeKey(key);

    internal void InitializeKey(ReadOnlySpan<byte> key)
    {
        if (key.Length != KeySize) throw new ArgumentException($"A Noise key is {KeySize} bytes.", nameof(key));
        _aead?.Dispose();
        if (_key is not null) CryptographicOperations.ZeroMemory(_key);
        _key = key.ToArray();
        _aead = new ChaCha20Poly1305(_key);
        _nonce = 0;
    }

    /// <summary>Noise nonce: 4 zero bytes then the counter, little-endian.</summary>
    private static void WriteNonce(Span<byte> destination, ulong counter)
    {
        destination[..4].Clear();
        BinaryPrimitives.WriteUInt64LittleEndian(destination[4..], counter);
    }

    internal byte[] EncryptWithAd(ReadOnlySpan<byte> associatedData, ReadOnlySpan<byte> plaintext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_aead is null) return plaintext.ToArray();   // no key yet: Noise passes plaintext through
        if (_nonce == MaxNonce) throw new InvalidOperationException("Noise nonce exhausted; rekey required.");

        byte[] output = new byte[plaintext.Length + TagSize];
        Span<byte> nonce = stackalloc byte[NonceSize];
        WriteNonce(nonce, _nonce);
        _aead.Encrypt(nonce, plaintext, output.AsSpan(0, plaintext.Length), output.AsSpan(plaintext.Length), associatedData);
        _nonce++;
        return output;
    }

    internal byte[] DecryptWithAd(ReadOnlySpan<byte> associatedData, ReadOnlySpan<byte> ciphertext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_aead is null) return ciphertext.ToArray();
        byte[] plaintext = DecryptAtNonce(_nonce, associatedData, ciphertext);
        _nonce++;
        return plaintext;
    }

    /// <summary>
    /// Decrypts at an explicit counter. The transport carries its sequence number
    /// on the wire, so a reordered or replayed frame is resolved by the replay
    /// window rather than by desynchronising an implicit counter.
    /// </summary>
    internal byte[] DecryptAtNonce(ulong counter, ReadOnlySpan<byte> associatedData, ReadOnlySpan<byte> ciphertext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_aead is null) return ciphertext.ToArray();
        if (ciphertext.Length < TagSize) throw new CryptographicException("Noise ciphertext is shorter than its tag.");

        byte[] plaintext = new byte[ciphertext.Length - TagSize];
        Span<byte> nonce = stackalloc byte[NonceSize];
        WriteNonce(nonce, counter);
        _aead.Decrypt(nonce, ciphertext[..plaintext.Length], ciphertext[plaintext.Length..], plaintext, associatedData);
        return plaintext;
    }

    /// <summary>Encrypts at an explicit counter, mirroring <see cref="DecryptAtNonce"/>.</summary>
    internal byte[] EncryptAtNonce(ulong counter, ReadOnlySpan<byte> associatedData, ReadOnlySpan<byte> plaintext)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_aead is null) return plaintext.ToArray();

        byte[] output = new byte[plaintext.Length + TagSize];
        Span<byte> nonce = stackalloc byte[NonceSize];
        WriteNonce(nonce, counter);
        _aead.Encrypt(nonce, plaintext, output.AsSpan(0, plaintext.Length), output.AsSpan(plaintext.Length), associatedData);
        return output;
    }

    /// <summary>Takes the next send counter, refusing to ever repeat one.</summary>
    internal ulong TakeNextNonce()
    {
        if (_nonce == MaxNonce) throw new InvalidOperationException("Noise nonce exhausted; rekey required.");
        return _nonce++;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _aead?.Dispose();
        if (_key is not null) CryptographicOperations.ZeroMemory(_key);
        _key = null;
    }
}

/// <summary>Noise SymmetricState: chaining key + handshake hash.</summary>
internal sealed class NoiseSymmetricState : IDisposable
{
    private const int HashLen = 32;

    private readonly byte[] _chainingKey = new byte[HashLen];
    private readonly byte[] _handshakeHash = new byte[HashLen];
    private readonly NoiseCipherState _cipher = new();

    internal ReadOnlySpan<byte> HandshakeHash => _handshakeHash;
    internal NoiseCipherState Cipher => _cipher;

    internal NoiseSymmetricState(string protocolName)
    {
        byte[] name = Encoding.ASCII.GetBytes(protocolName);
        if (name.Length <= HashLen) name.CopyTo(_handshakeHash, 0);   // zero-padded
        else SHA256.HashData(name).CopyTo(_handshakeHash, 0);
        _handshakeHash.CopyTo(_chainingKey, 0);
    }

    /// <summary>Noise HKDF: HMAC chain producing two or three outputs.</summary>
    private static void Hkdf(ReadOnlySpan<byte> chainingKey, ReadOnlySpan<byte> inputKeyMaterial,
        Span<byte> output1, Span<byte> output2)
    {
        Span<byte> tempKey = stackalloc byte[HashLen];
        HMACSHA256.HashData(chainingKey, inputKeyMaterial, tempKey);
        HMACSHA256.HashData(tempKey, stackalloc byte[] { 0x01 }, output1);

        Span<byte> second = stackalloc byte[HashLen + 1];
        output1.CopyTo(second);
        second[HashLen] = 0x02;
        HMACSHA256.HashData(tempKey, second, output2);
        CryptographicOperations.ZeroMemory(tempKey);
    }

    internal void MixKey(ReadOnlySpan<byte> inputKeyMaterial)
    {
        Span<byte> newChainingKey = stackalloc byte[HashLen];
        Span<byte> temporaryKey = stackalloc byte[HashLen];
        Hkdf(_chainingKey, inputKeyMaterial, newChainingKey, temporaryKey);
        newChainingKey.CopyTo(_chainingKey);
        _cipher.InitializeKey(temporaryKey);
        CryptographicOperations.ZeroMemory(temporaryKey);
    }

    internal void MixHash(ReadOnlySpan<byte> data)
    {
        Span<byte> buffer = stackalloc byte[HashLen + data.Length];
        _handshakeHash.CopyTo(buffer);
        data.CopyTo(buffer[HashLen..]);
        SHA256.HashData(buffer, _handshakeHash);
    }

    internal byte[] EncryptAndHash(ReadOnlySpan<byte> plaintext)
    {
        byte[] ciphertext = _cipher.EncryptWithAd(_handshakeHash, plaintext);
        MixHash(ciphertext);
        return ciphertext;
    }

    internal byte[] DecryptAndHash(ReadOnlySpan<byte> ciphertext)
    {
        byte[] plaintext = _cipher.DecryptWithAd(_handshakeHash, ciphertext);
        MixHash(ciphertext);
        return plaintext;
    }

    /// <summary>Final key split: (initiator→responder, responder→initiator).</summary>
    internal (NoiseCipherState Send, NoiseCipherState Receive) Split()
    {
        Span<byte> k1 = stackalloc byte[HashLen];
        Span<byte> k2 = stackalloc byte[HashLen];
        Hkdf(_chainingKey, ReadOnlySpan<byte>.Empty, k1, k2);
        var send = new NoiseCipherState(k1);
        var receive = new NoiseCipherState(k2);
        CryptographicOperations.ZeroMemory(k1);
        CryptographicOperations.ZeroMemory(k2);
        return (send, receive);
    }

    public void Dispose()
    {
        _cipher.Dispose();
        CryptographicOperations.ZeroMemory(_chainingKey);
        CryptographicOperations.ZeroMemory(_handshakeHash);
    }
}

/// <summary>
/// The <c>Noise_IK_25519_ChaChaPoly_SHA256</c> handshake.
/// </summary>
/// <remarks>
/// <para>
/// IK is the pattern the v1.9 specification calls for: the initiator already
/// knows the responder's static public key (it is provisioned during onboarding),
/// so the handshake completes in one round trip and the initiator's own static
/// identity is transmitted encrypted rather than in the clear.
/// </para>
/// <code>
///   &lt;- s                (pre-message: responder static, known in advance)
///   ...
///   -&gt; e, es, s, ss
///   &lt;- e, ee, se
/// </code>
/// <para>
/// The state machine is strict: each side accepts exactly one write and one read,
/// in order, and refuses anything else. A tampered message fails AEAD
/// authentication and aborts the handshake — there is no downgrade path and no
/// unauthenticated fallback.
/// </para>
/// </remarks>
public sealed class NoiseHandshakeState : IDisposable
{
    public const string ProtocolName = "Noise_IK_25519_ChaChaPoly_SHA256";
    public const int DhLen = 32;
    public const int TagSize = NoiseCipherState.TagSize;

    /// <summary>Message 1 = e(32) || encrypted static(32+16) || encrypted payload(+16).</summary>
    public const int Message1Overhead = DhLen + DhLen + TagSize + TagSize;

    /// <summary>Message 2 = e(32) || encrypted payload(+16).</summary>
    public const int Message2Overhead = DhLen + TagSize;

    private enum Step { WriteMessage1, ReadMessage1, WriteMessage2, ReadMessage2, Complete, Failed }

    private readonly NoiseSymmetricState _symmetric;
    private readonly bool _isInitiator;
    private readonly X25519Identity _staticKey;
    private X25519Identity? _ephemeralKey;
    private byte[]? _remoteStatic;
    private byte[]? _remoteEphemeral;
    private Step _step;
    private bool _disposed;

    public bool IsInitiator => _isInitiator;
    public bool IsComplete => _step == Step.Complete;

    /// <summary>The peer's authenticated static public key, available once complete.</summary>
    public byte[]? RemoteStaticPublicKey => _remoteStatic?.ToArray();

    /// <summary>The final handshake hash; identical on both sides of a good handshake.</summary>
    public byte[] HandshakeHash => _symmetric.HandshakeHash.ToArray();

    private NoiseHandshakeState(bool isInitiator, X25519Identity staticKey,
        ReadOnlySpan<byte> responderStaticPublicKey, ReadOnlySpan<byte> prologue)
    {
        _isInitiator = isInitiator;
        _staticKey = staticKey;
        _symmetric = new NoiseSymmetricState(ProtocolName);
        _symmetric.MixHash(prologue);
        // IK pre-message "<- s": both sides absorb the responder's static key.
        _symmetric.MixHash(responderStaticPublicKey);
        if (isInitiator) _remoteStatic = responderStaticPublicKey.ToArray();
        _step = isInitiator ? Step.WriteMessage1 : Step.ReadMessage1;
    }

    /// <summary>Creates the initiator, which must already know the responder's static key.</summary>
    public static NoiseHandshakeState CreateInitiator(X25519Identity staticKey,
        ReadOnlySpan<byte> responderStaticPublicKey, ReadOnlySpan<byte> prologue = default)
    {
        ArgumentNullException.ThrowIfNull(staticKey);
        if (responderStaticPublicKey.Length != DhLen)
            throw new ArgumentException($"A responder static key is {DhLen} bytes.", nameof(responderStaticPublicKey));
        return new NoiseHandshakeState(true, staticKey, responderStaticPublicKey, DefaultPrologue(prologue));
    }

    /// <summary>Creates the responder, whose own static key is the pre-message key.</summary>
    public static NoiseHandshakeState CreateResponder(X25519Identity staticKey, ReadOnlySpan<byte> prologue = default)
    {
        ArgumentNullException.ThrowIfNull(staticKey);
        return new NoiseHandshakeState(false, staticKey, staticKey.PublicKey, DefaultPrologue(prologue));
    }

    private static byte[] DefaultPrologue(ReadOnlySpan<byte> prologue) =>
        prologue.IsEmpty ? EphemeralSession.Prologue : prologue.ToArray();

    /// <summary>Writes handshake message 1: <c>e, es, s, ss</c>.</summary>
    public byte[] WriteMessage1(ReadOnlySpan<byte> payload = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Expect(Step.WriteMessage1);
        try
        {
            _ephemeralKey = X25519Identity.Generate();
            byte[] ephemeralPublic = _ephemeralKey.PublicKey;
            _symmetric.MixHash(ephemeralPublic);                                  // e

            MixDh(_ephemeralKey, _remoteStatic!);                                 // es
            byte[] encryptedStatic = _symmetric.EncryptAndHash(_staticKey.PublicKey);  // s
            MixDh(_staticKey, _remoteStatic!);                                    // ss
            byte[] encryptedPayload = _symmetric.EncryptAndHash(payload);

            _step = Step.ReadMessage2;
            return Concat(ephemeralPublic, encryptedStatic, encryptedPayload);
        }
        catch { _step = Step.Failed; throw; }
    }

    /// <summary>Reads handshake message 1 and returns its payload.</summary>
    public byte[] ReadMessage1(ReadOnlySpan<byte> message)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Expect(Step.ReadMessage1);
        try
        {
            if (message.Length < Message1Overhead)
                throw new CryptographicException("Noise message 1 is truncated.");

            _remoteEphemeral = message[..DhLen].ToArray();                        // e
            _symmetric.MixHash(_remoteEphemeral);

            MixDh(_staticKey, _remoteEphemeral);                                  // es (responder side)
            _remoteStatic = _symmetric.DecryptAndHash(message.Slice(DhLen, DhLen + TagSize));  // s
            if (_remoteStatic.Length != DhLen)
                throw new CryptographicException("Noise message 1 carried a malformed static key.");
            MixDh(_staticKey, _remoteStatic);                                     // ss

            byte[] payload = _symmetric.DecryptAndHash(message[(DhLen + DhLen + TagSize)..]);
            _step = Step.WriteMessage2;
            return payload;
        }
        catch { _step = Step.Failed; throw; }
    }

    /// <summary>Writes handshake message 2: <c>e, ee, se</c>, then splits.</summary>
    public byte[] WriteMessage2(ReadOnlySpan<byte> payload = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Expect(Step.WriteMessage2);
        try
        {
            _ephemeralKey = X25519Identity.Generate();
            byte[] ephemeralPublic = _ephemeralKey.PublicKey;
            _symmetric.MixHash(ephemeralPublic);                                  // e

            MixDh(_ephemeralKey, _remoteEphemeral!);                              // ee
            MixDh(_ephemeralKey, _remoteStatic!);                                 // se (responder: DH(e, rs))
            byte[] encryptedPayload = _symmetric.EncryptAndHash(payload);

            _step = Step.Complete;
            return Concat(ephemeralPublic, encryptedPayload);
        }
        catch { _step = Step.Failed; throw; }
    }

    /// <summary>Reads handshake message 2 and completes the handshake.</summary>
    public byte[] ReadMessage2(ReadOnlySpan<byte> message)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Expect(Step.ReadMessage2);
        try
        {
            if (message.Length < Message2Overhead)
                throw new CryptographicException("Noise message 2 is truncated.");

            _remoteEphemeral = message[..DhLen].ToArray();                        // e
            _symmetric.MixHash(_remoteEphemeral);

            MixDh(_ephemeralKey!, _remoteEphemeral);                              // ee
            MixDh(_staticKey, _remoteEphemeral);                                  // se (initiator: DH(s, re))
            byte[] payload = _symmetric.DecryptAndHash(message[DhLen..]);

            _step = Step.Complete;
            return payload;
        }
        catch { _step = Step.Failed; throw; }
    }

    /// <summary>
    /// Produces the transport session. Only valid once the handshake completed:
    /// there is no way to obtain transport keys from a partial handshake.
    /// </summary>
    public NoiseTransport Split()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_step != Step.Complete)
            throw new InvalidOperationException("The Noise handshake has not completed; no transport keys exist.");

        var (first, second) = _symmetric.Split();
        // Noise: the first cipher is initiator->responder, the second the reverse.
        return _isInitiator
            ? new NoiseTransport(first, second, HandshakeHash, _remoteStatic!)
            : new NoiseTransport(second, first, HandshakeHash, _remoteStatic!);
    }

    private void MixDh(X25519Identity local, ReadOnlySpan<byte> peerPublicKey)
    {
        byte[] shared = local.Agree(peerPublicKey);
        try { _symmetric.MixKey(shared); }
        finally { CryptographicOperations.ZeroMemory(shared); }
    }

    private void Expect(Step expected)
    {
        if (_step != expected)
            throw new InvalidOperationException($"Noise handshake step out of order: expected {expected}, state is {_step}.");
    }

    private static byte[] Concat(params byte[][] parts)
    {
        int total = 0;
        foreach (byte[] part in parts) total += part.Length;
        byte[] output = new byte[total];
        int offset = 0;
        foreach (byte[] part in parts)
        {
            part.CopyTo(output, offset);
            offset += part.Length;
        }
        return output;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _symmetric.Dispose();
        _ephemeralKey?.Dispose();
    }
}

/// <summary>
/// RFC 6479-style sliding replay window over a 64-message span.
/// </summary>
/// <remarks>
/// A transport that can reorder or duplicate needs more than a monotonic
/// counter: this accepts each sequence number at most once, tolerates
/// reordering inside the window, and refuses anything older than the window —
/// fail closed, because a message too old to prove fresh must not be accepted.
/// </remarks>
public sealed class ReplayWindow
{
    public const int WindowSize = 64;

    private ulong _highest;
    private ulong _bitmap;
    private bool _initialized;

    /// <summary>Highest sequence number accepted so far.</summary>
    public ulong HighestAccepted => _highest;

    /// <summary>Accepts a sequence number exactly once, updating the window.</summary>
    public bool TryAccept(ulong sequence)
    {
        if (!_initialized)
        {
            _initialized = true;
            _highest = sequence;
            _bitmap = 1;
            return true;
        }

        if (sequence > _highest)
        {
            ulong shift = sequence - _highest;
            _bitmap = shift >= WindowSize ? 1UL : (_bitmap << (int)shift) | 1UL;
            _highest = sequence;
            return true;
        }

        ulong distance = _highest - sequence;
        if (distance >= WindowSize) return false;      // older than the window: refused
        ulong mask = 1UL << (int)distance;
        if ((_bitmap & mask) != 0) return false;       // already seen: replay
        _bitmap |= mask;
        return true;
    }
}

/// <summary>
/// Post-handshake transport. Each frame carries its sequence number explicitly
/// (<c>seq(8, big-endian) || ciphertext || tag(16)</c>) and is bound to it as
/// associated data, so a replayed or reordered frame is resolved by the replay
/// window instead of desynchronising an implicit counter.
/// </summary>
public sealed class NoiseTransport : IDisposable
{
    public const int SequenceSize = 8;
    public const int TagSize = NoiseCipherState.TagSize;

    private readonly NoiseCipherState _send;
    private readonly NoiseCipherState _receive;
    private readonly ReplayWindow _replayWindow = new();
    private readonly byte[] _handshakeHash;
    private readonly byte[] _remoteStaticPublicKey;
    private bool _disposed;

    /// <summary>Handshake hash; a channel binding both sides can compare.</summary>
    public byte[] HandshakeHash => _handshakeHash.ToArray();

    /// <summary>The peer static key authenticated by the handshake.</summary>
    public byte[] RemoteStaticPublicKey => _remoteStaticPublicKey.ToArray();

    public ReplayWindow ReplayWindow => _replayWindow;

    internal NoiseTransport(NoiseCipherState send, NoiseCipherState receive,
        byte[] handshakeHash, byte[] remoteStaticPublicKey)
    {
        _send = send;
        _receive = receive;
        _handshakeHash = handshakeHash;
        _remoteStaticPublicKey = remoteStaticPublicKey;
    }

    /// <summary>Encrypts a payload into a sequenced transport frame.</summary>
    public byte[] Encrypt(ReadOnlySpan<byte> payload, ReadOnlySpan<byte> associatedData = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ulong sequence = _send.TakeNextNonce();

        byte[] frame = new byte[SequenceSize + payload.Length + TagSize];
        BinaryPrimitives.WriteUInt64BigEndian(frame.AsSpan(0, SequenceSize), sequence);

        // The sequence number is authenticated: moving a frame to another slot
        // breaks the tag instead of silently succeeding.
        byte[] ad = BindSequence(sequence, associatedData);
        // TakeNextNonce already advanced the counter: encrypt at the slot it handed out.
        byte[] sealedPayload = _send.EncryptAtNonce(sequence, ad, payload);
        sealedPayload.CopyTo(frame.AsSpan(SequenceSize));
        return frame;
    }

    /// <summary>
    /// Decrypts a transport frame, rejecting replays and out-of-window frames
    /// before any plaintext is returned.
    /// </summary>
    public bool TryDecrypt(ReadOnlySpan<byte> frame, out byte[] payload, out string? rejection,
        ReadOnlySpan<byte> associatedData = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        payload = Array.Empty<byte>();

        if (frame.Length < SequenceSize + TagSize)
        {
            rejection = "frame_truncated";
            return false;
        }

        ulong sequence = BinaryPrimitives.ReadUInt64BigEndian(frame[..SequenceSize]);
        byte[] ad = BindSequence(sequence, associatedData);
        byte[] plaintext;
        try
        {
            plaintext = _receive.DecryptAtNonce(sequence, ad, frame[SequenceSize..]);
        }
        catch (CryptographicException)
        {
            // Authentication first: a forged frame must never move the window.
            rejection = "authentication_failed";
            return false;
        }

        if (!_replayWindow.TryAccept(sequence))
        {
            rejection = "replayed_or_out_of_window";
            return false;
        }

        payload = plaintext;
        rejection = null;
        return true;
    }

    private static byte[] BindSequence(ulong sequence, ReadOnlySpan<byte> associatedData)
    {
        byte[] ad = new byte[SequenceSize + associatedData.Length];
        BinaryPrimitives.WriteUInt64BigEndian(ad.AsSpan(0, SequenceSize), sequence);
        associatedData.CopyTo(ad.AsSpan(SequenceSize));
        return ad;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _send.Dispose();
        _receive.Dispose();
    }

}
