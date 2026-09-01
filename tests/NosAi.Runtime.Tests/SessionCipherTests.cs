using System.Security.Cryptography;
using NosAi.Runtime.Gate1;
using Xunit;
using Xunit.Abstractions;

namespace NosAi.Runtime.Tests;

/// <summary>
/// Session payload encryption (ADR-0009), pinned against the Python implementation.
/// </summary>
/// <remarks>
/// The vectors here have twins in <c>tests/test_session_cipher.py</c>. The golden
/// frame is the strongest of them: these bytes were produced by the Python side
/// and are opened here, so a divergence in the key schedule, the nonce layout or
/// the associated data fails a test instead of appearing later as a phone that
/// authenticates and then cannot read anything.
/// </remarks>
public sealed class SessionCipherTests
{
    private readonly ITestOutputHelper _output;

    public SessionCipherTests(ITestOutputHelper output) => _output = output;

    // Fixed P-256 material. Test vectors, not credentials: they protect nothing
    // and exist so a mismatch between the two languages is visible.
    private const string ClientPublic =
        "04515C3D6EB9E396B904D3FECA7F54FDCD0CC1E997BF375DCA515AD0A6C3B4035F4536BE3A50F318FBF9A5475902A221502BEF0D57E08C53B2CC0A56F17D9F9354";
    private const string ServerPublic =
        "04C6559D416DFB56AF714F146D917C24ABF818B2FB121604129649848230A2D258B2A6D82DC6C6734CF092FFAA9FC012F10F7008D3952A08D5797E85FEABA5D977";
    private const string ClientScalar = "0102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F20";
    private const string SessionMaterial =
        "E60A5234E58D2840E2A2B5F14A68FCB4EE8BFD10B40E4B331916B80314308ADA" +
        "8C86BE6149B84DB5EAB8C5C38C8C5C36DC3261EB26CAE85E63872F392F18384B";

    // A TelemetrySnapshot frame: NOSA, version 3, type 0x11, payload 0x2A, sequence 7.
    private const string GoldenHeader = "4E4F53410311002A00000007";
    private const string GoldenPayload =
        "000000000000000000000000803FC5ED76FBFF5E0AEB85DCAE312A4ACD2BFDFE1D57628D1C92FBC16466";
    private static readonly byte[] GoldenPlaintext = "gate1-snapshot"u8.ToArray();

    private static byte[] ClientNonce() => Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    private static byte[] ServerNonce() => Enumerable.Range(0, 32).Select(i => (byte)(255 - i)).ToArray();

    private static byte[] Material() => Convert.FromHexString(SessionMaterial);

    [Fact]
    public void TheKeyScheduleMatchesThePythonImplementation()
    {
        // The agreement is done with the BCL over the pinned fixed keys, then fed
        // to the schedule under test. What this proves is that C# and Python agree
        // on SHA-256(Z), on the HKDF salt and info, and on the 64-byte split.
        byte[] serverPublic = Convert.FromHexString(ServerPublic);
        var clientParameters = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = Convert.FromHexString(ClientScalar),
            Q = new ECPoint
            {
                X = Convert.FromHexString(ClientPublic)[1..33],
                Y = Convert.FromHexString(ClientPublic)[33..65]
            }
        };
        clientParameters.Validate();

        using var client = ECDiffieHellman.Create(clientParameters);
        var peerParameters = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = serverPublic[1..33], Y = serverPublic[33..65] }
        };
        using var server = ECDiffieHellman.Create(peerParameters);

        byte[] agreementDigest = client.DeriveKeyFromHash(server.PublicKey, HashAlgorithmName.SHA256);
        byte[] binding = SessionTranscript.ComputeBinding(
            ClientNonce(), ServerNonce(), Convert.FromHexString(ClientPublic), serverPublic);

        Assert.Equal(SessionMaterial, Convert.ToHexString(EphemeralKeyExchange.Schedule(agreementDigest, binding)));
    }

    [Fact]
    public void TheGoldenFrameIsSealedByteForByte()
    {
        using var phone = SessionCipher.ForPhone(Material());

        byte[] frame = phone.SealFrame(WireMessageType.TelemetrySnapshot, 7, GoldenPlaintext);

        Evidence.Live(_output, "intestazione", Convert.ToHexString(frame[..WireHeader.HeaderSize]),
            "vettore fisso: un cambiamento qui e' un cambiamento di protocollo");
        Evidence.Live(_output, "payloadCifrato", Convert.ToHexString(frame[WireHeader.HeaderSize..]));
        Evidence.Live(_output, "byteTotaliFrame", frame.Length);

        Assert.Equal(GoldenHeader, Convert.ToHexString(frame[..WireHeader.HeaderSize]));
        Assert.Equal(GoldenPayload, Convert.ToHexString(frame[WireHeader.HeaderSize..]));
    }

    [Fact]
    public void TheGoldenFrameProducedByPythonOpensHere()
    {
        using var runtime = SessionCipher.ForRuntime(Material());

        Assert.True(runtime.TryOpenFrame(
            Convert.FromHexString(GoldenHeader), Convert.FromHexString(GoldenPayload), out byte[] plaintext, out string? reason));
        Assert.Null(reason);
        Assert.Equal(GoldenPlaintext, plaintext);
    }

    [Fact]
    public void BothSidesOfOneAgreementDeriveTheSameMaterial()
    {
        using var phone = EphemeralKeyExchange.Create();
        using var runtime = EphemeralKeyExchange.Create();

        byte[] binding = SessionTranscript.ComputeBinding(
            ClientNonce(), ServerNonce(), phone.PublicKey, runtime.PublicKey);

        Assert.Equal(
            phone.DeriveSessionMaterial(runtime.PublicKey, binding),
            runtime.DeriveSessionMaterial(phone.PublicKey, binding));
    }

    [Fact]
    public void AnEphemeralKeyIsAnUncompressedPointAndNeverRepeats()
    {
        using var first = EphemeralKeyExchange.Create();
        using var second = EphemeralKeyExchange.Create();

        Assert.Equal(SessionTranscript.EphemeralKeyLength, first.PublicKey.Length);
        Assert.Equal(0x04, first.PublicKey[0]);
        Assert.NotEqual(first.PublicKey, second.PublicKey);
    }

    [Fact]
    public void ADifferentBindingDerivesDifferentKeys()
    {
        // The binding is the HKDF salt, so a peer that saw a different handshake
        // ends up unable to decrypt rather than quietly agreeing on a key.
        using var phone = EphemeralKeyExchange.Create();
        using var runtime = EphemeralKeyExchange.Create();

        byte[] good = SessionTranscript.ComputeBinding(ClientNonce(), ServerNonce(), phone.PublicKey, runtime.PublicKey);
        byte[] other = SessionTranscript.ComputeBinding(ServerNonce(), ClientNonce(), phone.PublicKey, runtime.PublicKey);

        Assert.NotEqual(
            phone.DeriveSessionMaterial(runtime.PublicKey, good),
            phone.DeriveSessionMaterial(runtime.PublicKey, other));
    }

    [Fact]
    public void AnInvalidPeerPointIsRefused()
    {
        // An unchecked point would let a peer steer the agreement.
        using var phone = EphemeralKeyExchange.Create();
        byte[] offCurve = new byte[SessionTranscript.EphemeralKeyLength];
        offCurve[0] = 0x04;

        Assert.False(EphemeralKeyExchange.IsValidPublicKey(offCurve));
        Assert.False(EphemeralKeyExchange.IsValidPublicKey(new byte[SessionTranscript.EphemeralKeyLength]));
        Assert.False(EphemeralKeyExchange.IsValidPublicKey(new byte[32]));
        Assert.Throws<CryptographicException>(() => phone.DeriveSessionMaterial(offCurve, new byte[32]));
    }

    [Fact]
    public void TheDirectionsDoNotShareAKey()
    {
        // With one shared key a frame captured in one direction would decrypt when
        // replayed down the other.
        using var phone = SessionCipher.ForPhone(Material());
        using var otherPhone = SessionCipher.ForPhone(Material());

        byte[] frame = phone.SealFrame(WireMessageType.TelemetrySnapshot, 7, GoldenPlaintext);

        Assert.False(otherPhone.TryOpenFrame(
            frame[..WireHeader.HeaderSize], frame[WireHeader.HeaderSize..], out _, out string? reason));
        Assert.Equal("authentication_failed", reason);
    }

    [Fact]
    public void ATamperedHeaderFailsTheTag()
    {
        // The header is readable but authenticated: rewriting the type or the
        // sequence number must not go unnoticed.
        using var phone = SessionCipher.ForPhone(Material());
        using var runtime = SessionCipher.ForRuntime(Material());

        byte[] frame = phone.SealFrame(WireMessageType.TelemetrySnapshot, 7, GoldenPlaintext);
        byte[] header = frame[..WireHeader.HeaderSize];
        header[5] = (byte)WireMessageType.Heartbeat;

        Assert.False(runtime.TryOpenFrame(header, frame[WireHeader.HeaderSize..], out _, out string? reason));
        Assert.Equal("authentication_failed", reason);
    }

    [Fact]
    public void ATamperedCiphertextFailsTheTag()
    {
        using var phone = SessionCipher.ForPhone(Material());
        using var runtime = SessionCipher.ForRuntime(Material());

        byte[] frame = phone.SealFrame(WireMessageType.TelemetrySnapshot, 7, GoldenPlaintext);
        frame[WireHeader.HeaderSize + SessionCipher.NonceLength] ^= 0x01;

        Assert.False(runtime.TryOpenFrame(
            frame[..WireHeader.HeaderSize], frame[WireHeader.HeaderSize..], out _, out string? reason));
        Assert.Equal("authentication_failed", reason);
    }

    [Fact]
    public void TheNonceAdvancesAndTheSamePlaintextDoesNotRepeat()
    {
        using var phone = SessionCipher.ForPhone(Material());

        byte[] first = phone.SealFrame(WireMessageType.TelemetrySnapshot, 7, GoldenPlaintext);
        byte[] second = phone.SealFrame(WireMessageType.TelemetrySnapshot, 8, GoldenPlaintext);

        var firstNonce = first.AsSpan(WireHeader.HeaderSize, SessionCipher.NonceLength).ToArray();
        var secondNonce = second.AsSpan(WireHeader.HeaderSize, SessionCipher.NonceLength).ToArray();

        Assert.NotEqual(firstNonce, secondNonce);
        Assert.Equal(new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1 }, secondNonce);
    }

    [Fact]
    public void AnOutOfOrderNonceIsRefused()
    {
        using var phone = SessionCipher.ForPhone(Material());
        using var runtime = SessionCipher.ForRuntime(Material());

        phone.SealFrame(WireMessageType.TelemetrySnapshot, 7, GoldenPlaintext);
        byte[] second = phone.SealFrame(WireMessageType.TelemetrySnapshot, 8, GoldenPlaintext);

        // The receiver still expects counter 0, so counter 1 is refused before the
        // tag is even considered.
        Assert.False(runtime.TryOpenFrame(
            second[..WireHeader.HeaderSize], second[WireHeader.HeaderSize..], out _, out string? reason));
        Assert.Equal("nonce_out_of_order", reason);
    }

    [Fact]
    public void AShortPayloadIsRefused()
    {
        using var runtime = SessionCipher.ForRuntime(Material());

        Assert.False(runtime.TryOpenFrame(
            Convert.FromHexString(GoldenHeader), new byte[SessionCipher.Overhead - 1], out _, out string? reason));
        Assert.Equal("encrypted_payload_too_short", reason);
    }

    [Fact]
    public void OnlyHandshakeMessagesMayTravelInClear()
    {
        Assert.True(WireMessageTypes.IsHandshake(WireMessageType.SessionHello));
        Assert.True(WireMessageTypes.IsHandshake(WireMessageType.Capabilities));
        Assert.True(WireMessageTypes.IsHandshake(WireMessageType.AuthChallenge));
        Assert.True(WireMessageTypes.IsHandshake(WireMessageType.AuthResponse));
        Assert.True(WireMessageTypes.IsHandshake(WireMessageType.AuthResult));
        Assert.True(WireMessageTypes.IsHandshake(WireMessageType.ServerAuthProof));

        Assert.False(WireMessageTypes.IsHandshake(WireMessageType.Heartbeat));
        Assert.False(WireMessageTypes.IsHandshake(WireMessageType.HeartbeatAck));
        Assert.False(WireMessageTypes.IsHandshake(WireMessageType.TelemetrySnapshot));
        Assert.False(WireMessageTypes.IsHandshake(WireMessageType.WorldStateDelta));
        Assert.False(WireMessageTypes.IsHandshake(WireMessageType.CommandRequest));
        Assert.False(WireMessageTypes.IsHandshake(WireMessageType.CommandAck));
        Assert.False(WireMessageTypes.IsHandshake(WireMessageType.Disconnect));
    }

    [Fact]
    public void APlaintextThatWouldOverflowTheHeaderIsRefused()
    {
        using var phone = SessionCipher.ForPhone(Material());

        Assert.Equal(WireHeader.MaxPayloadLength - SessionCipher.Overhead, SessionCipher.MaxPlaintextLength);
        Assert.Throws<InvalidDataException>(() =>
            phone.SealFrame(WireMessageType.TelemetrySnapshot, 1, new byte[SessionCipher.MaxPlaintextLength + 1]));
    }

    [Fact]
    public void FrameLengthAccountsForHeaderNonceAndTag()
    {
        Assert.Equal(
            WireHeader.HeaderSize + SessionCipher.Overhead + GoldenPlaintext.Length,
            SessionCipher.FrameLength(GoldenPlaintext.Length));
    }

    [Fact]
    public void SealFrameIntoProducesTheSameBytesAsTheAllocatingSealFrame()
    {
        // Two fresh ciphers over the same fixed material and the same sequence:
        // both start their nonce counter at zero, so the two APIs must agree byte
        // for byte instead of merely "close enough".
        using var viaAllocatingApi = SessionCipher.ForPhone(Material());
        byte[] allocated = viaAllocatingApi.SealFrame(WireMessageType.TelemetrySnapshot, 7, GoldenPlaintext);

        using var viaPooledApi = SessionCipher.ForPhone(Material());
        Span<byte> destination = new byte[SessionCipher.FrameLength(GoldenPlaintext.Length)];
        viaPooledApi.SealFrameInto(destination, WireMessageType.TelemetrySnapshot, 7, GoldenPlaintext);

        Assert.Equal(allocated, destination.ToArray());
    }

    [Fact]
    public void SealFrameIntoWritesOnlyItsOwnSliceOfALargerPooledStyleBuffer()
    {
        // ArrayPool<T>.Rent can hand back a buffer larger than requested. The
        // pooled send path relies on SealFrameInto never touching bytes past the
        // frame it was asked to write.
        using var phone = SessionCipher.ForPhone(Material());
        int frameLength = SessionCipher.FrameLength(GoldenPlaintext.Length);
        byte[] oversized = new byte[frameLength + 64];
        Array.Fill(oversized, (byte)0xCC);

        phone.SealFrameInto(oversized, WireMessageType.TelemetrySnapshot, 7, GoldenPlaintext);

        Assert.Equal(GoldenHeader, Convert.ToHexString(oversized.AsSpan(0, WireHeader.HeaderSize)));
        foreach (byte trailing in oversized.AsSpan(frameLength))
            Assert.Equal(0xCC, trailing);
    }

    [Fact]
    public void SealFrameIntoRefusesADestinationSmallerThanTheFrame()
    {
        using var phone = SessionCipher.ForPhone(Material());
        byte[] tooSmall = new byte[SessionCipher.FrameLength(GoldenPlaintext.Length) - 1];

        Assert.Throws<ArgumentException>(() =>
            phone.SealFrameInto(tooSmall, WireMessageType.TelemetrySnapshot, 7, GoldenPlaintext));
    }

    [Fact]
    public void SealFrameIntoZeroesTheNonceItselfInsteadOfTrustingAPreCleanedBuffer()
    {
        // ArrayPool<byte>.Shared is process-wide and shared with code that has no
        // idea this channel exists -- System.Text.Json's own scratch buffers rent
        // from it and are not cleared on return. A destination buffer with
        // leftover nonzero bytes at the four positions this method never writes
        // is exactly what a neighbour like that leaves behind, and it must not
        // produce a frame the receiver refuses as "out of order".
        using var phone = SessionCipher.ForPhone(Material());
        using var runtime = SessionCipher.ForRuntime(Material());

        byte[] dirty = new byte[SessionCipher.FrameLength(GoldenPlaintext.Length)];
        Array.Fill(dirty, (byte)0xFF);

        phone.SealFrameInto(dirty, WireMessageType.TelemetrySnapshot, 7, GoldenPlaintext);

        Assert.True(runtime.TryOpenFrame(
            dirty.AsSpan(0, WireHeader.HeaderSize),
            dirty.AsSpan(WireHeader.HeaderSize),
            out byte[] plaintext,
            out string? reason));
        Assert.Null(reason);
        Assert.Equal(GoldenPlaintext, plaintext);
    }

    [Fact]
    public void ManyFramesSealedIntoPooledBuffersOfVaryingSizesAllOpen()
    {
        using var phone = SessionCipher.ForPhone(Material());
        using var runtime = SessionCipher.ForRuntime(Material());

        var sizes = new[] { 0, 0, 650, 0, 651, 0, 649, 0, 652 };
        for (int i = 0; i < sizes.Length; i++)
        {
            byte[] plaintext = new byte[sizes[i]];
            new Random(i).NextBytes(plaintext);

            using var frame = PooledWireBuffer.Rent(SessionCipher.FrameLength(plaintext.Length));
            phone.SealFrameInto(frame.Span, WireMessageType.TelemetrySnapshot, (uint)(i + 1), plaintext);

            using var headerCopy = PooledWireBuffer.Rent(WireHeader.HeaderSize);
            frame.Span[..WireHeader.HeaderSize].CopyTo(headerCopy.Span);
            using var payloadCopy = PooledWireBuffer.Rent(frame.Length - WireHeader.HeaderSize);
            frame.Span[WireHeader.HeaderSize..].CopyTo(payloadCopy.Span);

            bool ok = runtime.TryOpenFrame(headerCopy.Span, payloadCopy.Span, out byte[] opened, out string? reason);
            Assert.True(ok, $"iteration {i} size {sizes[i]} failed: {reason}");
            Assert.Equal(plaintext, opened);
        }
    }

    [Fact]
    public void AFrameSealedIntoAPooledBufferOpensIdenticallyToOneFromSealFrame()
    {
        using var phone = SessionCipher.ForPhone(Material());
        using var runtime = SessionCipher.ForRuntime(Material());

        using var frame = PooledWireBuffer.Rent(SessionCipher.FrameLength(GoldenPlaintext.Length));
        phone.SealFrameInto(frame.Span, WireMessageType.TelemetrySnapshot, 7, GoldenPlaintext);

        Assert.True(runtime.TryOpenFrame(
            frame.Span[..WireHeader.HeaderSize], frame.Span[WireHeader.HeaderSize..], out byte[] plaintext, out string? reason));
        Assert.Null(reason);
        Assert.Equal(GoldenPlaintext, plaintext);
    }
}
