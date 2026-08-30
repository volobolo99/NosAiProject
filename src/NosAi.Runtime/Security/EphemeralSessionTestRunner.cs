// ============================================================================
// Progetto: NosAi — Runtime di Automazione Controllata
// Versione: 1.0 Beta
// Sicurezza — Suite di certificazione della sessione effimera X25519/AEAD
// ============================================================================

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NosAi.Runtime.Security;

public static partial class EphemeralSessionTestRunner
{
    /// <summary>
    /// Runs every crypto check and reports each one by name (same contract as the
    /// gate runners: no short-circuit, a throwing check is a named failure).
    /// </summary>
    public static bool RunAll()
    {
        Console.WriteLine("=== Ephemeral session (X25519/AEAD) checks ===");

        bool allPassed = true;
        allPassed &= Run("X25519 matches the RFC 7748 test vector", TestX25519Rfc7748Vector);
        allPassed &= Run("Both ends derive the same session key", TestBothEndsAgree);
        allPassed &= Run("HKDF derivation matches the fixed cross-language vector", TestHkdfKnownVector);
        allPassed &= Run("Round-trip decrypts what was encrypted", TestRoundTrip);
        allPassed &= Run("Associated data is authenticated", TestAssociatedDataAuthenticated);
        allPassed &= Run("Tampered ciphertext fails authentication", TestTamperDetected);
        allPassed &= Run("Nonce is unique and monotonic per direction", TestNonceMonotonic);
        allPassed &= Run("Each session derives a distinct ephemeral key", TestDistinctSessionKeys);
        allPassed &= Run("Wire layout is nonce(12) || ciphertext || tag(16)", TestWireLayout);
        allPassed &= Run("A small-order peer key is rejected", TestSmallOrderRejected);
        allPassed &= Run("Noise IK handshake completes and both sides agree", TestNoiseIkHandshakeCompletes);
        allPassed &= Run("Noise IK authenticates both static identities", TestNoiseIkAuthenticatesIdentities);
        allPassed &= Run("Noise IK carries payloads in both handshake messages", TestNoiseIkHandshakePayloads);
        allPassed &= Run("Noise transport round-trips in both directions", TestNoiseTransportRoundTrip);
        allPassed &= Run("Noise handshake rejects a tampered message", TestNoiseHandshakeRejectsTampering);
        allPassed &= Run("Noise handshake rejects the wrong responder key", TestNoiseHandshakeRejectsWrongResponder);
        allPassed &= Run("Noise handshake refuses out-of-order steps", TestNoiseHandshakeIsOrdered);
        allPassed &= Run("Transport keys are unreachable before completion", TestNoiseSplitRequiresCompletion);
        allPassed &= Run("Replay window accepts once, reorders, refuses the old", TestReplayWindowSemantics);
        allPassed &= Run("Transport refuses replayed and forged frames", TestNoiseTransportRefusesReplay);
        allPassed &= Run("Transport binds the sequence number into the tag", TestNoiseTransportBindsSequence);

        Console.WriteLine(allPassed
            ? "=== Crypto checks passed. Local only: no interoperability test against another Noise stack. ==="
            : "=== Crypto checks FAILED. See the lines marked FAIL above. ===");
        return allPassed;
    }

    private static bool Run(string name, Func<bool> check)
    {
        try { return Report(name, check(), null); }
        catch (Exception ex) { return Report(name, false, $"{ex.GetType().Name}: {ex.Message}"); }
    }

    private static bool Report(string name, bool passed, string? error)
    {
        var detail = error is null ? string.Empty : $" [{error}]";
        Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] {name}{detail}");
        return passed;
    }

    private static bool TestX25519Rfc7748Vector()
    {
        // RFC 7748 §6.1: Alice's private + Bob's public → known shared secret.
        var alice = X25519Identity.FromPrivateKey(
            Convert.FromHexString("77076d0a7318a57d3c16c17251b26645df4c2f87ebc0992ab177fba51db92c2a"));
        byte[] bobPublic = Convert.FromHexString("de9edb7d7b7dc1b4d35b61c2ece435373f8343c85b78674dadfc7e146f882b4f");
        byte[] shared = alice.Agree(bobPublic);
        return Convert.ToHexString(shared).Equals(
            "4A5D9D5BA4CE2DE1728E3BF480350F25E07E21C947D19E3376F09B3C1E161742", StringComparison.Ordinal);
    }

    private static bool TestBothEndsAgree()
    {
        using var server = X25519Identity.Generate();
        using var client = X25519Identity.Generate();
        byte[] serverShared = server.Agree(client.PublicKey);
        byte[] clientShared = client.Agree(server.PublicKey);
        return CryptographicOperations.FixedTimeEquals(serverShared, clientShared)
            && CryptographicOperations.FixedTimeEquals(
                EphemeralSession.DeriveSessionKey(serverShared), EphemeralSession.DeriveSessionKey(clientShared));
    }

    private static bool TestHkdfKnownVector()
    {
        // Fixed cross-language vector: HKDF-SHA256(ikm=0x00..0x1F, salt=zeros,
        // info="NOS_AI_PROTOCOL_V1") → 32 bytes. This exact value is reproduced by
        // the Python cryptography HKDF on the other end, pinning wire parity.
        byte[] ikm = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        byte[] derived = EphemeralSession.DeriveSessionKey(ikm);
        return Convert.ToHexString(derived).Equals(
            "4E1E4E0E6BFF5A3214844E49F47B5736D0F7D64DA5FD607C4D40A938FB6A8D31", StringComparison.Ordinal);
    }

    private static bool TestRoundTrip()
    {
        var (session, peer) = EstablishPair();
        using (session)
        using (peer)
        {
            byte[] message = Encoding.UTF8.GetBytes("NosAi telemetry frame 42");
            byte[] packet = session.Encrypt(message);
            byte[] recovered = peer.Decrypt(packet);
            return recovered.SequenceEqual(message);
        }
    }

    private static bool TestAssociatedDataAuthenticated()
    {
        var (session, peer) = EstablishPair();
        using (session)
        using (peer)
        {
            byte[] message = Encoding.UTF8.GetBytes("bound-to-header");
            byte[] ad = Encoding.UTF8.GetBytes("frame-index:7");
            byte[] packet = session.Encrypt(message, ad);
            if (!peer.Decrypt(packet, ad).SequenceEqual(message)) return false;
            try
            {
                peer.Decrypt(packet, Encoding.UTF8.GetBytes("frame-index:8"));
                return false; // wrong AD must not authenticate
            }
            catch (CryptographicException) { return true; }
        }
    }

    private static bool TestTamperDetected()
    {
        var (session, peer) = EstablishPair();
        using (session)
        using (peer)
        {
            byte[] packet = session.Encrypt(Encoding.UTF8.GetBytes("do-not-tamper"));
            packet[^1] ^= 0xFF; // flip a tag bit
            try { peer.Decrypt(packet); return false; }
            catch (CryptographicException) { return true; }
        }
    }

    private static bool TestNonceMonotonic()
    {
        var (session, _) = EstablishPair();
        using (session)
        {
            byte[] first = session.Encrypt(Array.Empty<byte>());
            byte[] second = session.Encrypt(Array.Empty<byte>());
            var firstNonce = first.AsSpan(0, EphemeralSession.NonceSize).ToArray();
            var secondNonce = second.AsSpan(0, EphemeralSession.NonceSize).ToArray();
            ulong n0 = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(firstNonce.AsSpan(4));
            ulong n1 = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(secondNonce.AsSpan(4));
            return n0 == 0 && n1 == 1 && !firstNonce.SequenceEqual(secondNonce);
        }
    }

    private static bool TestDistinctSessionKeys()
    {
        using var server = X25519Identity.Generate();
        using var clientA = X25519Identity.Generate();
        using var clientB = X25519Identity.Generate();
        byte[] keyA = EphemeralSession.DeriveSessionKey(server.Agree(clientA.PublicKey));
        byte[] keyB = EphemeralSession.DeriveSessionKey(server.Agree(clientB.PublicKey));
        return !keyA.SequenceEqual(keyB);
    }

    private static bool TestWireLayout()
    {
        var (session, _) = EstablishPair();
        using (session)
        {
            byte[] payload = new byte[24];
            RandomNumberGenerator.Fill(payload);
            byte[] packet = session.Encrypt(payload);
            return packet.Length == EphemeralSession.NonceSize + payload.Length + EphemeralSession.TagSize;
        }
    }

    private static bool TestSmallOrderRejected()
    {
        using var identity = X25519Identity.Generate();
        byte[] allZeroPeer = new byte[X25519Identity.KeySize]; // small-order point → zero shared secret
        try { identity.Agree(allZeroPeer); return false; }
        catch (CryptographicException) { return true; }
    }

    private static (EphemeralSession Local, EphemeralSession Peer) EstablishPair()
    {
        using var server = X25519Identity.Generate();
        using var client = X25519Identity.Generate();
        // Independent session objects sharing the same derived key: each keeps its
        // own send counter, as the two directions would at runtime.
        return (EphemeralSession.FromX25519(server, client.PublicKey),
                EphemeralSession.FromX25519(client, server.PublicKey));
    }
}
