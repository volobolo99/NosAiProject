using System.Security.Cryptography;
using NosAi.Runtime.Gate1;
using Xunit;

namespace NosAi.Runtime.Tests;

/// <summary>
/// The handshake transcript that closes the version 1 authentication holes.
/// </summary>
/// <remarks>
/// The two digests below are pinned in both languages: <c>tests/test_session_transcript.py</c>
/// asserts the same vectors against the Python implementation. A divergence would
/// otherwise surface only as a phone that can no longer authenticate, with both
/// sides believing they were right.
/// </remarks>
public sealed class SessionTranscriptTests
{
    // Chosen so the vectors are reproducible by hand: 0..31 and 255..224.
    private static byte[] ClientNonce() => Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    private static byte[] ServerNonce() => Enumerable.Range(0, 32).Select(i => (byte)(255 - i)).ToArray();

    private const string ClientDigest = "024BF7D9878949C7521C7BC00D8A1AD36181EC36545A28D2725C547B9E8247BB";
    private const string ServerDigest = "FB4928D668FEFF2F89ED1894B14EEDA456268AF143D3580F93E2E27C65763795";

    [Fact]
    public void PinnedVectorsMatchThePythonImplementation()
    {
        Assert.Equal(ClientDigest, Convert.ToHexString(
            SessionTranscript.Compute(HandshakeRole.Client, ClientNonce(), ServerNonce())));
        Assert.Equal(ServerDigest, Convert.ToHexString(
            SessionTranscript.Compute(HandshakeRole.Server, ClientNonce(), ServerNonce())));
    }

    [Fact]
    public void TheTwoRolesNeverProduceTheSameDigest()
    {
        // Without the role byte, a signature harvested from the phone could be
        // replayed back as the runtime's proof, and the phone would accept its own
        // signature as evidence it was talking to a genuine runtime.
        Assert.NotEqual(
            SessionTranscript.Compute(HandshakeRole.Client, ClientNonce(), ServerNonce()),
            SessionTranscript.Compute(HandshakeRole.Server, ClientNonce(), ServerNonce()));
    }

    [Fact]
    public void ASignatureDoesNotCarryAcrossSessions()
    {
        byte[] first = SessionTranscript.Compute(HandshakeRole.Client, ClientNonce(), ServerNonce());

        Assert.NotEqual(first, SessionTranscript.Compute(HandshakeRole.Client, ClientNonce(), SessionTranscript.CreateNonce()));
        Assert.NotEqual(first, SessionTranscript.Compute(HandshakeRole.Client, SessionTranscript.CreateNonce(), ServerNonce()));
    }

    [Fact]
    public void AShortNonceIsRefused()
    {
        // A peer that will not commit to a full nonce cannot be given a
        // session-bound proof, and accepting one would mean signing over material
        // it fully controls.
        Assert.Throws<ArgumentException>(() =>
            SessionTranscript.Compute(HandshakeRole.Client, new byte[31], ServerNonce()));
        Assert.Throws<ArgumentException>(() =>
            SessionTranscript.Compute(HandshakeRole.Client, ClientNonce(), new byte[33]));
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

        byte[] proof = genuine.SignAsServer(clientNonce, serverNonce);

        using var genuineKey = RSA.Create();
        genuineKey.ImportFromPem(genuine.PublicKeyPem);
        using var impostorKey = RSA.Create();
        impostorKey.ImportFromPem(impostor.PublicKeyPem);

        Assert.True(SessionTranscript.Verify(genuineKey, HandshakeRole.Server, clientNonce, serverNonce, proof));
        Assert.False(SessionTranscript.Verify(impostorKey, HandshakeRole.Server, clientNonce, serverNonce, proof));
    }

    [Fact]
    public void APhoneSignatureIsNotAcceptedAsARuntimeProof()
    {
        // The replay the role byte exists to stop.
        using var phone = RSA.Create(2048);
        byte[] clientNonce = SessionTranscript.CreateNonce();
        byte[] serverNonce = SessionTranscript.CreateNonce();

        byte[] phoneSignature = SessionTranscript.Sign(phone, HandshakeRole.Client, clientNonce, serverNonce);

        Assert.True(SessionTranscript.Verify(phone, HandshakeRole.Client, clientNonce, serverNonce, phoneSignature));
        Assert.False(SessionTranscript.Verify(phone, HandshakeRole.Server, clientNonce, serverNonce, phoneSignature));
    }

    [Fact]
    public void AnUnstartedSessionVerifiesNothing()
    {
        // No hello, no nonces, so no signature can verify: a peer cannot skip
        // straight to an auth response.
        using var device = RSA.Create(2048);
        using var auth = new SessionAuth(device.ExportSubjectPublicKeyInfoPem());

        Assert.False(auth.VerifyAndConsume(new byte[256]));
        Assert.False(auth.TryCreateServerProof(out _));
    }

    [Fact]
    public void AMalformedClientNonceEndsTheHandshake()
    {
        using var device = RSA.Create(2048);
        using var auth = new SessionAuth(device.ExportSubjectPublicKeyInfoPem());

        Assert.False(auth.TryBeginHandshake(new byte[8], out _));
        Assert.True(auth.TryBeginHandshake(SessionTranscript.CreateNonce(), out byte[] serverNonce));
        Assert.Equal(SessionTranscript.NonceLength, serverNonce.Length);
    }
}
