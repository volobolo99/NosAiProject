using System.Security.Cryptography;
using NosAi.Runtime.Gate1;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The handshake transcript that closes the version 1 authentication holes.
/// </summary>
/// <remarks>
/// The digests below are pinned in both languages: <c>tests/test_session_transcript.py</c>
/// asserts the same vectors against the Python implementation. A divergence would
/// otherwise surface only as a phone that can no longer authenticate, with both
/// sides believing they were right.
/// </remarks>
public sealed class SessionTranscriptTests
{
    // Chosen so the vectors are reproducible by hand: 0..31 and 255..224 for the
    // nonces, and a 0x04-prefixed ramp for each ephemeral key. Those stand in for
    // real P-256 points on purpose — the transcript hashes the encoded bytes and
    // never interprets them, so the vector stays independent of key generation.
    private static byte[] ClientNonce() => Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    private static byte[] ServerNonce() => Enumerable.Range(0, 32).Select(i => (byte)(255 - i)).ToArray();

    private static byte[] ClientEphemeral() =>
        new byte[] { 0x04 }.Concat(Enumerable.Range(0, 64).Select(i => (byte)i)).ToArray();

    private static byte[] ServerEphemeral() =>
        new byte[] { 0x04 }.Concat(Enumerable.Range(0, 64).Select(i => (byte)(255 - i))).ToArray();

    private const string ClientDigest = "C21C431996795F1008869B2F2F404788065FEBB2B4D540EBA6E10586EB81DCCB";
    private const string ServerDigest = "4FA15241CCA7785A61BA9ADA88CD5C6C6C3330BDA4B9C7160D6F50E8F6E59047";
    private const string BindingDigest = "EEA2EFAC25055CB73768C2C38E4150E682441F83A2D9EDF8056FEC37078DD397";

    private static byte[] Compute(HandshakeRole role) =>
        SessionTranscript.Compute(role, ClientNonce(), ServerNonce(), ClientEphemeral(), ServerEphemeral());

    [Fact]
    public void PinnedVectorsMatchThePythonImplementation()
    {
        Assert.Equal(ClientDigest, Convert.ToHexString(Compute(HandshakeRole.Client)));
        Assert.Equal(ServerDigest, Convert.ToHexString(Compute(HandshakeRole.Server)));
        Assert.Equal(BindingDigest, Convert.ToHexString(
            SessionTranscript.ComputeBinding(ClientNonce(), ServerNonce(), ClientEphemeral(), ServerEphemeral())));
    }

    [Fact]
    public void TheTwoRolesNeverProduceTheSameDigest()
    {
        // Without the role byte, a signature harvested from the phone could be
        // replayed back as the runtime's proof, and the phone would accept its own
        // signature as evidence it was talking to a genuine runtime.
        Assert.NotEqual(Compute(HandshakeRole.Client), Compute(HandshakeRole.Server));
    }

    [Fact]
    public void TheKeyBindingIsNeverSomethingEitherSideSigns()
    {
        // The binding uses role 0x00, which is not a valid signing role, so a
        // key-derivation input cannot collide with a digest carrying a signature.
        byte[] binding = SessionTranscript.ComputeBinding(ClientNonce(), ServerNonce(), ClientEphemeral(), ServerEphemeral());

        Assert.NotEqual(binding, Compute(HandshakeRole.Client));
        Assert.NotEqual(binding, Compute(HandshakeRole.Server));
    }

    [Fact]
    public void ASignatureDoesNotCarryAcrossSessions()
    {
        byte[] first = Compute(HandshakeRole.Client);

        Assert.NotEqual(first, SessionTranscript.Compute(
            HandshakeRole.Client, ClientNonce(), SessionTranscript.CreateNonce(), ClientEphemeral(), ServerEphemeral()));
        Assert.NotEqual(first, SessionTranscript.Compute(
            HandshakeRole.Client, SessionTranscript.CreateNonce(), ServerNonce(), ClientEphemeral(), ServerEphemeral()));
    }

    [Fact]
    public void SwappingAnEphemeralKeyChangesTheDigest()
    {
        // This is what authenticates the key agreement (ADR-0009): an attacker who
        // substitutes an ephemeral key invalidates the signature carrying it.
        byte[] first = Compute(HandshakeRole.Client);
        byte[] other = new byte[SessionTranscript.EphemeralKeyLength];
        other[0] = 0x04;

        Assert.NotEqual(first, SessionTranscript.Compute(
            HandshakeRole.Client, ClientNonce(), ServerNonce(), other, ServerEphemeral()));
        Assert.NotEqual(first, SessionTranscript.Compute(
            HandshakeRole.Client, ClientNonce(), ServerNonce(), ClientEphemeral(), other));
    }

    [Fact]
    public void AMalformedFieldIsRefused()
    {
        // A peer that will not commit to a full nonce or a full point cannot be
        // given a session-bound proof, and accepting one would mean signing over
        // material it fully controls.
        Assert.Throws<ArgumentException>(() =>
            SessionTranscript.Compute(HandshakeRole.Client, new byte[31], ServerNonce(), ClientEphemeral(), ServerEphemeral()));
        Assert.Throws<ArgumentException>(() =>
            SessionTranscript.Compute(HandshakeRole.Client, ClientNonce(), new byte[33], ClientEphemeral(), ServerEphemeral()));
        Assert.Throws<ArgumentException>(() =>
            SessionTranscript.Compute(HandshakeRole.Client, ClientNonce(), ServerNonce(), new byte[64], ServerEphemeral()));
        Assert.Throws<ArgumentException>(() =>
            SessionTranscript.Compute(HandshakeRole.Client, ClientNonce(), ServerNonce(), ClientEphemeral(), new byte[66]));
    }

    [Fact]
    public void ANonceIsThirtyTwoBytesAndNotConstant()
    {
        byte[] first = SessionTranscript.CreateNonce();
        byte[] second = SessionTranscript.CreateNonce();

        Assert.Equal(SessionTranscript.NonceLength, first.Length);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void TheRuntimeProvesItselfAndThePhoneCanTellAnImpostorApart()
    {
        // The property version 1 lacked entirely: something on the network could
        // answer a discovery probe and act as a runtime, and the phone had no way
        // to notice.
        using var genuine = RuntimeIdentity.CreateEphemeral();
        using var impostor = RuntimeIdentity.CreateEphemeral();

        byte[] clientNonce = SessionTranscript.CreateNonce();
        byte[] serverNonce = SessionTranscript.CreateNonce();

        byte[] proof = genuine.SignAsServer(clientNonce, serverNonce, ClientEphemeral(), ServerEphemeral());

        using var genuineKey = RSA.Create();
        genuineKey.ImportFromPem(genuine.PublicKeyPem);
        using var impostorKey = RSA.Create();
        impostorKey.ImportFromPem(impostor.PublicKeyPem);

        Assert.True(SessionTranscript.Verify(
            genuineKey, HandshakeRole.Server, clientNonce, serverNonce, ClientEphemeral(), ServerEphemeral(), proof));
        Assert.False(SessionTranscript.Verify(
            impostorKey, HandshakeRole.Server, clientNonce, serverNonce, ClientEphemeral(), ServerEphemeral(), proof));
    }

    [Fact]
    public void APhoneSignatureIsNotAcceptedAsARuntimeProof()
    {
        // The replay the role byte exists to stop.
        using var phone = RSA.Create(2048);
        byte[] clientNonce = SessionTranscript.CreateNonce();
        byte[] serverNonce = SessionTranscript.CreateNonce();

        byte[] phoneSignature = SessionTranscript.Sign(
            phone, HandshakeRole.Client, clientNonce, serverNonce, ClientEphemeral(), ServerEphemeral());

        Assert.True(SessionTranscript.Verify(
            phone, HandshakeRole.Client, clientNonce, serverNonce, ClientEphemeral(), ServerEphemeral(), phoneSignature));
        Assert.False(SessionTranscript.Verify(
            phone, HandshakeRole.Server, clientNonce, serverNonce, ClientEphemeral(), ServerEphemeral(), phoneSignature));
    }

    [Fact]
    public void ASignatureDoesNotVerifyUnderASwappedEphemeralKey()
    {
        using var phone = RSA.Create(2048);
        byte[] clientNonce = SessionTranscript.CreateNonce();
        byte[] serverNonce = SessionTranscript.CreateNonce();

        byte[] signature = SessionTranscript.Sign(
            phone, HandshakeRole.Client, clientNonce, serverNonce, ClientEphemeral(), ServerEphemeral());

        Assert.False(SessionTranscript.Verify(
            phone, HandshakeRole.Client, clientNonce, serverNonce, ServerEphemeral(), ClientEphemeral(), signature));
    }

    [Fact]
    public void AnUnstartedSessionVerifiesNothing()
    {
        // No hello, no nonces, so no signature can verify: a peer cannot skip
        // straight to an auth response, and gets no key material either.
        using var device = RSA.Create(2048);
        using var auth = new SessionAuth(device.ExportSubjectPublicKeyInfoPem());

        Assert.False(auth.VerifyAndConsume(new byte[256], out byte[] material));
        Assert.Empty(material);
        Assert.False(auth.TryCreateServerProof(out _));
    }

    [Fact]
    public void AMalformedClientHelloEndsTheHandshake()
    {
        using var device = RSA.Create(2048);
        using var auth = new SessionAuth(device.ExportSubjectPublicKeyInfoPem());
        using var exchange = EphemeralKeyExchange.Create();

        // Too short, and the right length but not a point on the curve: both are
        // refused before any key material exists.
        Assert.False(auth.TryBeginHandshake(new byte[8], out _));
        Assert.False(auth.TryBeginHandshake(new byte[SessionAuth.HandshakeHelloLength], out _));

        byte[] hello = new byte[SessionAuth.HandshakeHelloLength];
        SessionTranscript.CreateNonce().CopyTo(hello, 0);
        exchange.PublicKey.CopyTo(hello, SessionTranscript.NonceLength);

        Assert.True(auth.TryBeginHandshake(hello, out byte[] serverHello));
        Assert.Equal(SessionAuth.HandshakeHelloLength, serverHello.Length);
    }

    [Fact]
    public void AFailedSignatureReleasesNoKeyMaterial()
    {
        // A refused phone must not walk away with a usable cipher.
        using var device = RSA.Create(2048);
        using var other = RSA.Create(2048);
        using var auth = new SessionAuth(device.ExportSubjectPublicKeyInfoPem());
        using var exchange = EphemeralKeyExchange.Create();

        byte[] clientNonce = SessionTranscript.CreateNonce();
        byte[] hello = new byte[SessionAuth.HandshakeHelloLength];
        clientNonce.CopyTo(hello, 0);
        exchange.PublicKey.CopyTo(hello, SessionTranscript.NonceLength);
        Assert.True(auth.TryBeginHandshake(hello, out byte[] serverHello));

        byte[] serverNonce = serverHello[..SessionTranscript.NonceLength];
        byte[] serverEphemeral = serverHello[SessionTranscript.NonceLength..];
        byte[] wrongSignature = SessionTranscript.Sign(
            other, HandshakeRole.Client, clientNonce, serverNonce, exchange.PublicKey, serverEphemeral);

        Assert.False(auth.VerifyAndConsume(wrongSignature, out byte[] material));
        Assert.Empty(material);
    }
}
