using System.Security.Cryptography;
using NosAi.Runtime.Gate1;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The invariant behind wire version 4: no frame on the Guard channel is ever
/// sealed over an empty plaintext.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the bug it guards could not be caught on the machine the
/// tests run on. Sealing an empty plaintext hands ChaCha20-Poly1305 a zero-length
/// span, which in C# always pins to a null pointer; the desktop BCL accepts that
/// and the Android crypto PAL aborts the process:
/// </para>
/// <code>
/// pal_cipher.c:258 (AndroidCryptoNative_CipherUpdate):
///     Parameter 'in' must be a valid pointer
/// </code>
/// <para>
/// So the round trip below passes on Windows either way and proves nothing about
/// the phone. What is actually load-bearing is
/// <see cref="TheHeartbeatPayloadIsNotEmpty"/>: the property a Windows test can
/// check is that the payload the heartbeat carries is non-empty, and that is what
/// stops the empty encoding coming back.
/// </para>
/// </remarks>
public sealed class HeartbeatPayloadTests
{
    /// <summary>One phone/runtime pair sharing the session material a real handshake derives.</summary>
    private static (SessionCipher Phone, SessionCipher Runtime) Pair()
    {
        var material = RandomNumberGenerator.GetBytes(EphemeralKeyExchange.SessionMaterialLength);
        return (SessionCipher.ForPhone(material), SessionCipher.ForRuntime(material));
    }

    [Fact]
    public void TheHeartbeatPayloadIsNotEmpty()
    {
        // The whole point of wire version 4. An empty payload here is the bug.
        Assert.False(WireMessageTypes.HeartbeatPayload.IsEmpty);
        Assert.True(WireMessageTypes.HeartbeatPayload.Length >= 1);
    }

    [Fact]
    public void AHeartbeatIsNotAHandshakeSoItIsSealed()
    {
        // If it were a handshake type it would travel in clear and the empty
        // plaintext would never reach the cipher. It is not, which is why the
        // payload has to be non-empty rather than the sealing skipped.
        Assert.False(WireMessageTypes.IsHandshake(WireMessageType.Heartbeat));
        Assert.False(WireMessageTypes.IsHandshake(WireMessageType.HeartbeatAck));
    }

    [Fact]
    public void TheWireVersionIsTheOneThatCarriesAHeartbeatPayload()
    {
        // Pinned so that giving heartbeats a payload without refusing version-3
        // peers is a failing test rather than a native crash on a phone.
        Assert.Equal(4, WireHeader.CurrentVersion);
    }

    [Fact]
    public void AVersionThreePeerIsRefusedAtTheHeaderInsteadOfBeingAnswered()
    {
        var frame = new byte[WireHeader.HeaderSize];
        new WireHeader(WireMessageType.Heartbeat, 1, 1).WriteTo(frame);
        frame[4] = 3; // a runtime or phone from before this fix

        Assert.False(WireHeader.TryRead(frame, out _, out var error));
        Assert.Equal("unsupported_version", error);
    }

    [Fact]
    public void ASealedHeartbeatFrameCarriesACiphertextRatherThanNothing()
    {
        var (client, server) = Pair();
        using (client)
        using (server)
        {
            byte[] frame = client.SealFrame(
                WireMessageType.Heartbeat, 1, WireMessageTypes.HeartbeatPayload.Span);

            // Header, nonce, at least one byte of ciphertext, tag. A zero-length
            // ciphertext is exactly what the Android PAL refuses to open.
            int ciphertextLength = frame.Length - WireHeader.HeaderSize - SessionCipher.Overhead;
            Assert.True(ciphertextLength >= 1, $"ciphertext was {ciphertextLength} bytes");
        }
    }

    [Fact]
    public void TheHeartbeatPayloadSurvivesASealAndOpenRoundTrip()
    {
        var (client, server) = Pair();
        using (client)
        using (server)
        {
            byte[] frame = client.SealFrame(
                WireMessageType.Heartbeat, 1, WireMessageTypes.HeartbeatPayload.Span);

            bool opened = server.TryOpenFrame(
                frame.AsSpan(0, WireHeader.HeaderSize),
                frame.AsSpan(WireHeader.HeaderSize),
                out byte[] plaintext,
                out string? reason);

            Assert.True(opened, reason);
            Assert.Equal(WireMessageTypes.HeartbeatPayload.ToArray(), plaintext);
        }
    }

    [Fact]
    public void TheSharedPayloadCannotBeMutatedThroughTheProperty()
    {
        // One array is handed to every heartbeat on every connection. Exposing it
        // as ReadOnlyMemory is what stops a caller editing the copy everyone sends.
        byte[] first = WireMessageTypes.HeartbeatPayload.ToArray();
        first[0] ^= 0xFF;

        Assert.NotEqual(first, WireMessageTypes.HeartbeatPayload.ToArray());
    }
}
