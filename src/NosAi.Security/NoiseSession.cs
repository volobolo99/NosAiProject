using System.Security.Cryptography;
using Noise;

namespace NosAi.Security;

/// <summary>
/// Coarse handshake progress for <see cref="INoiseSession"/>
/// (docs/ROADMAP_ESECUTIVA.md S:2.2). <see cref="Failed"/> is terminal: no
/// path re-attempts on the same session instance
/// (docs/ROADMAP_ESECUTIVA.md S:2.3, "Stato Failed è terminale").
/// </summary>
public enum NoiseHandshakeState : byte
{
    Idle = 0,
    SentE = 1,
    SentEe = 2,
    Transport = 3,
    Failed = 4
}

/// <summary>
/// One side of a Noise_XX_25519_ChaChaPoly_SHA256 session. The same
/// read/write pair of methods carries both the three handshake messages and,
/// once complete, transport messages: callers do not need to know which phase
/// they are in.
/// </summary>
public interface INoiseSession : IDisposable
{
    NoiseHandshakeState State { get; }

    /// <summary>Encrypts (transport) or advances the handshake and writes the result, returning bytes written.</summary>
    int WriteMessage(ReadOnlySpan<byte> payload, Span<byte> destination);

    /// <summary>Decrypts (transport) or advances the handshake from a received message, returning bytes written to <paramref name="destination"/>.</summary>
    int ReadMessage(ReadOnlySpan<byte> message, Span<byte> destination);

    /// <summary>
    /// Ratchets both transport directions forward. Requires <see cref="NoiseHandshakeState.Transport"/>.
    /// Both peers must call this at the same message-count/time boundary
    /// (docs/ROADMAP_ESECUTIVA.md S:2.3: every 2^20 messages or 15 minutes,
    /// whichever comes first) or they lose the ability to decrypt each other;
    /// enforcing that boundary is the caller's responsibility -- this method
    /// only performs the ratchet.
    /// </summary>
    void Rekey();

    /// <summary>
    /// HKDF-SHA256 of the Noise handshake hash, used as <c>K_session</c> for
    /// <see cref="FrameTagCalculator"/>. Both peers derive the same 32 bytes
    /// after <see cref="NoiseHandshakeState.Transport"/>; the method throws
    /// before that.
    /// </summary>
    byte[] DeriveFrameSessionKey();
}

/// <summary>
/// <see cref="INoiseSession"/> implemented on top of the Noise.NET package
/// (ADR-0015 S:6). Wraps a <see cref="HandshakeState"/> until the handshake's
/// final message hands back a <see cref="Transport"/>, then delegates to that
/// instead.
/// </summary>
public sealed class NoiseXxSession : INoiseSession
{
    /// <summary>Mandatory rekey budget: 2^20 messages (docs/ROADMAP_ESECUTIVA.md S:2.3).</summary>
    public const long RekeyMessageBudget = 1 << 20;

    /// <summary>Mandatory rekey budget: 15 minutes (docs/ROADMAP_ESECUTIVA.md S:2.3).</summary>
    public static readonly TimeSpan RekeyInterval = TimeSpan.FromMinutes(15);

    private static readonly Protocol NoiseProtocol = new(HandshakePattern.XX, CipherFunction.ChaChaPoly, HashFunction.Sha256);

    /// <summary>
    /// Prologue mixed into the handshake hash so a session for this protocol
    /// cannot be confused with a Noise_XX session of anything else.
    /// </summary>
    public static ReadOnlySpan<byte> Prologue => "NosAi.Gate1.v1"u8;

    private static ReadOnlySpan<byte> FrameTagInfo => "nosai-frame-tag-v1"u8;

    private HandshakeState? _handshake;
    private Transport? _transport;
    private byte[]? _handshakeHash;
    private NoiseHandshakeState _state = NoiseHandshakeState.Idle;
    private long _messagesSinceRekey;
    private DateTimeOffset _lastRekeyUtc;
    private bool _disposed;

    /// <param name="initiator">Alice/Bob role: exactly one side of a session must pass <see langword="true"/>.</param>
    /// <param name="localStaticPrivateKey">32-byte X25519 private key. See <see cref="GenerateStaticPrivateKey"/>.</param>
    public NoiseXxSession(bool initiator, byte[] localStaticPrivateKey)
    {
        ArgumentNullException.ThrowIfNull(localStaticPrivateKey);

        _handshake = NoiseProtocol.Create(initiator, Prologue, s: localStaticPrivateKey);
        _lastRekeyUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>Generates a fresh 32-byte X25519 static private key for a new session identity.</summary>
    public static byte[] GenerateStaticPrivateKey()
    {
        using KeyPair keyPair = KeyPair.Generate();
        // Defensive copy: KeyPair.Dispose() zeroes its own backing array in place.
        return keyPair.PrivateKey.ToArray();
    }

    public NoiseHandshakeState State => _state;

    /// <summary>Whether the mandatory rekey budget (message count or elapsed time) has been reached.</summary>
    public bool IsRekeyDue =>
        _state == NoiseHandshakeState.Transport &&
        (_messagesSinceRekey >= RekeyMessageBudget || DateTimeOffset.UtcNow - _lastRekeyUtc >= RekeyInterval);

    public int WriteMessage(ReadOnlySpan<byte> payload, Span<byte> destination)
    {
        ThrowIfFailed();

        try
        {
            if (_state == NoiseHandshakeState.Transport)
            {
                int written = _transport!.WriteMessage(payload, destination);
                _messagesSinceRekey++;
                return written;
            }

            (int bytesWritten, byte[]? handshakeHash, Transport? transport) = _handshake!.WriteMessage(payload, destination);
            AdvanceHandshake(transport, handshakeHash);
            return bytesWritten;
        }
        catch
        {
            _state = NoiseHandshakeState.Failed;
            throw;
        }
    }

    public int ReadMessage(ReadOnlySpan<byte> message, Span<byte> destination)
    {
        ThrowIfFailed();

        try
        {
            if (_state == NoiseHandshakeState.Transport)
            {
                int read = _transport!.ReadMessage(message, destination);
                _messagesSinceRekey++;
                return read;
            }

            (int bytesRead, byte[]? handshakeHash, Transport? transport) = _handshake!.ReadMessage(message, destination);
            AdvanceHandshake(transport, handshakeHash);
            return bytesRead;
        }
        catch
        {
            _state = NoiseHandshakeState.Failed;
            throw;
        }
    }

    public void Rekey()
    {
        if (_state != NoiseHandshakeState.Transport)
            throw new InvalidOperationException("Rekey requires a completed handshake.");

        _transport!.RekeyInitiatorToResponder();
        _transport!.RekeyResponderToInitiator();
        _messagesSinceRekey = 0;
        _lastRekeyUtc = DateTimeOffset.UtcNow;
    }

    public byte[] DeriveFrameSessionKey()
    {
        if (_state != NoiseHandshakeState.Transport || _handshakeHash is null)
            throw new InvalidOperationException("Frame session key requires a completed handshake.");

        byte[] key = new byte[32];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, _handshakeHash, key, salt: ReadOnlySpan<byte>.Empty, info: FrameTagInfo);
        return key;
    }

    private void AdvanceHandshake(Transport? transport, byte[]? handshakeHash)
    {
        if (transport is not null)
        {
            _handshake!.Dispose();
            _handshake = null;
            _transport = transport;
            _handshakeHash = handshakeHash ?? throw new InvalidOperationException("Noise.NET returned a transport without a handshake hash.");
            _state = NoiseHandshakeState.Transport;
            _lastRekeyUtc = DateTimeOffset.UtcNow;
            return;
        }

        _state = _state switch
        {
            NoiseHandshakeState.Idle => NoiseHandshakeState.SentE,
            NoiseHandshakeState.SentE => NoiseHandshakeState.SentEe,
            _ => _state
        };
    }

    private void ThrowIfFailed()
    {
        if (_state == NoiseHandshakeState.Failed)
            throw new InvalidOperationException("This Noise session has failed and is terminal; create a new session instead.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _handshake?.Dispose();
        _transport?.Dispose();
        _disposed = true;
    }
}
