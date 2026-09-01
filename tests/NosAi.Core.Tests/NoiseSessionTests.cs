using System.Text;
using NosAi.Security;
using Xunit;

namespace NosAi.Core.Tests;

[Trait("Category", "Gate1")]
public sealed class NoiseSessionTests
{
    [Fact]
    public void FullXxHandshakeCompletesAndBothSidesReachTransport()
    {
        using INoiseSession initiator = new NoiseXxSession(initiator: true, NoiseXxSession.GenerateStaticPrivateKey());
        using INoiseSession responder = new NoiseXxSession(initiator: false, NoiseXxSession.GenerateStaticPrivateKey());

        Assert.Equal(NoiseHandshakeState.Idle, initiator.State);
        Assert.Equal(NoiseHandshakeState.Idle, responder.State);

        Span<byte> wire = new byte[4096];

        // Message 1: initiator -> responder (e).
        int len1 = initiator.WriteMessage(default, wire);
        Assert.Equal(NoiseHandshakeState.SentE, initiator.State);
        responder.ReadMessage(wire[..len1], stackalloc byte[4096]);
        Assert.Equal(NoiseHandshakeState.SentE, responder.State);

        // Message 2: responder -> initiator (e, ee, s, es).
        int len2 = responder.WriteMessage(default, wire);
        Assert.Equal(NoiseHandshakeState.SentEe, responder.State);
        initiator.ReadMessage(wire[..len2], stackalloc byte[4096]);
        Assert.Equal(NoiseHandshakeState.SentEe, initiator.State);

        // Message 3: initiator -> responder (s, se). Completes the handshake for both sides.
        int len3 = initiator.WriteMessage(default, wire);
        Assert.Equal(NoiseHandshakeState.Transport, initiator.State);
        responder.ReadMessage(wire[..len3], stackalloc byte[4096]);
        Assert.Equal(NoiseHandshakeState.Transport, responder.State);
    }

    [Fact]
    public void TransportMessagesRoundTripAfterHandshakeCompletes()
    {
        using INoiseSession initiator = new NoiseXxSession(initiator: true, NoiseXxSession.GenerateStaticPrivateKey());
        using INoiseSession responder = new NoiseXxSession(initiator: false, NoiseXxSession.GenerateStaticPrivateKey());

        CompleteHandshake(initiator, responder);

        byte[] plaintext = Encoding.UTF8.GetBytes("gate 1 transport message");
        Span<byte> ciphertext = new byte[plaintext.Length + 64];
        int written = initiator.WriteMessage(plaintext, ciphertext);

        Span<byte> decrypted = new byte[plaintext.Length + 64];
        int read = responder.ReadMessage(ciphertext[..written], decrypted);

        Assert.True(decrypted[..read].SequenceEqual(plaintext));
    }

    [Fact]
    public void TamperedTransportCiphertextFailsToDecryptAndFailsTheSession()
    {
        using INoiseSession initiator = new NoiseXxSession(initiator: true, NoiseXxSession.GenerateStaticPrivateKey());
        using INoiseSession responder = new NoiseXxSession(initiator: false, NoiseXxSession.GenerateStaticPrivateKey());

        CompleteHandshake(initiator, responder);

        byte[] plaintext = Encoding.UTF8.GetBytes("integrity check");
        byte[] ciphertext = new byte[plaintext.Length + 64];
        int written = initiator.WriteMessage(plaintext, ciphertext);
        ciphertext[0] ^= 0xFF;
        byte[] tampered = ciphertext[..written];

        Assert.ThrowsAny<Exception>(() => responder.ReadMessage(tampered, new byte[plaintext.Length + 64]));
        Assert.Equal(NoiseHandshakeState.Failed, responder.State);

        // Failed is terminal: no further use of this session instance is attempted.
        Assert.Throws<InvalidOperationException>(() => responder.WriteMessage(plaintext, ciphertext));
    }

    [Fact]
    public void RekeyBeforeTheHandshakeCompletesThrows()
    {
        using INoiseSession session = new NoiseXxSession(initiator: true, NoiseXxSession.GenerateStaticPrivateKey());

        Assert.Throws<InvalidOperationException>(session.Rekey);
    }

    [Fact]
    public void RekeyAfterHandshakeAllowsContinuedTransportUse()
    {
        using INoiseSession initiator = new NoiseXxSession(initiator: true, NoiseXxSession.GenerateStaticPrivateKey());
        using INoiseSession responder = new NoiseXxSession(initiator: false, NoiseXxSession.GenerateStaticPrivateKey());

        CompleteHandshake(initiator, responder);

        initiator.Rekey();
        responder.Rekey();

        byte[] plaintext = Encoding.UTF8.GetBytes("post-rekey message");
        Span<byte> ciphertext = new byte[plaintext.Length + 64];
        int written = initiator.WriteMessage(plaintext, ciphertext);

        Span<byte> decrypted = new byte[plaintext.Length + 64];
        int read = responder.ReadMessage(ciphertext[..written], decrypted);

        Assert.True(decrypted[..read].SequenceEqual(plaintext));
    }

    [Fact]
    public void GeneratedStaticPrivateKeysAreThirtyTwoBytesAndNotAllZero()
    {
        byte[] key = NoiseXxSession.GenerateStaticPrivateKey();

        Assert.Equal(32, key.Length);
        Assert.Contains(key, b => b != 0);
    }

    private static void CompleteHandshake(INoiseSession initiator, INoiseSession responder)
    {
        Span<byte> wire = new byte[4096];

        int len1 = initiator.WriteMessage(default, wire);
        responder.ReadMessage(wire[..len1], new byte[4096]);

        int len2 = responder.WriteMessage(default, wire);
        initiator.ReadMessage(wire[..len2], new byte[4096]);

        int len3 = initiator.WriteMessage(default, wire);
        responder.ReadMessage(wire[..len3], new byte[4096]);

        Assert.Equal(NoiseHandshakeState.Transport, initiator.State);
        Assert.Equal(NoiseHandshakeState.Transport, responder.State);
    }

    [Fact]
    public void BothSidesDeriveTheSameFrameSessionKeyAfterHandshake()
    {
        using INoiseSession initiator = new NoiseXxSession(initiator: true, NoiseXxSession.GenerateStaticPrivateKey());
        using INoiseSession responder = new NoiseXxSession(initiator: false, NoiseXxSession.GenerateStaticPrivateKey());

        CompleteHandshake(initiator, responder);

        byte[] initiatorKey = initiator.DeriveFrameSessionKey();
        byte[] responderKey = responder.DeriveFrameSessionKey();

        Assert.Equal(32, initiatorKey.Length);
        Assert.Equal(initiatorKey, responderKey);
    }

    [Fact]
    public void DeriveFrameSessionKeyBeforeHandshakeThrows()
    {
        using INoiseSession session = new NoiseXxSession(initiator: true, NoiseXxSession.GenerateStaticPrivateKey());

        Assert.Throws<InvalidOperationException>(() => session.DeriveFrameSessionKey());
    }
}
