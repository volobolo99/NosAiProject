// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Sicurezza — Check di certificazione Noise IK: handshake, trasporto, replay
// ============================================================================

using System;
using System.Security.Cryptography;

namespace NosAi.Runtime.Security;

public static partial class EphemeralSessionTestRunner
{
    /// <summary>Drives a full IK handshake and returns both transports.</summary>
    private static (NoiseTransport Initiator, NoiseTransport Responder) Handshake(
        X25519Identity initiatorStatic, X25519Identity responderStatic)
    {
        using var initiator = NoiseHandshakeState.CreateInitiator(initiatorStatic, responderStatic.PublicKey);
        using var responder = NoiseHandshakeState.CreateResponder(responderStatic);
        responder.ReadMessage1(initiator.WriteMessage1());
        initiator.ReadMessage2(responder.WriteMessage2());
        return (initiator.Split(), responder.Split());
    }

    private static bool TestNoiseIkHandshakeCompletes()
    {
        using var initiatorStatic = X25519Identity.Generate();
        using var responderStatic = X25519Identity.Generate();
        using var initiator = NoiseHandshakeState.CreateInitiator(initiatorStatic, responderStatic.PublicKey);
        using var responder = NoiseHandshakeState.CreateResponder(responderStatic);

        byte[] message1 = initiator.WriteMessage1();
        if (message1.Length != NoiseHandshakeState.Message1Overhead) return false;
        responder.ReadMessage1(message1);

        byte[] message2 = responder.WriteMessage2();
        if (message2.Length != NoiseHandshakeState.Message2Overhead) return false;
        initiator.ReadMessage2(message2);

        // The handshake hash is the channel binding: it matches on both sides only
        // if every mix happened in the same order over the same material.
        return initiator.IsComplete && responder.IsComplete
            && initiator.HandshakeHash.AsSpan().SequenceEqual(responder.HandshakeHash);
    }

    private static bool TestNoiseIkAuthenticatesIdentities()
    {
        using var initiatorStatic = X25519Identity.Generate();
        using var responderStatic = X25519Identity.Generate();
        using var initiator = NoiseHandshakeState.CreateInitiator(initiatorStatic, responderStatic.PublicKey);
        using var responder = NoiseHandshakeState.CreateResponder(responderStatic);

        responder.ReadMessage1(initiator.WriteMessage1());
        initiator.ReadMessage2(responder.WriteMessage2());

        // IK transmits the initiator's static encrypted: the responder learns it
        // from the handshake, and each side ends up holding the other's real key.
        return responder.RemoteStaticPublicKey!.AsSpan().SequenceEqual(initiatorStatic.PublicKey)
            && initiator.RemoteStaticPublicKey!.AsSpan().SequenceEqual(responderStatic.PublicKey);
    }

    private static bool TestNoiseIkHandshakePayloads()
    {
        using var initiatorStatic = X25519Identity.Generate();
        using var responderStatic = X25519Identity.Generate();
        using var initiator = NoiseHandshakeState.CreateInitiator(initiatorStatic, responderStatic.PublicKey);
        using var responder = NoiseHandshakeState.CreateResponder(responderStatic);

        byte[] request = "guard-enrolment-request"u8.ToArray();
        byte[] response = "guard-enrolment-accepted"u8.ToArray();

        byte[] readRequest = responder.ReadMessage1(initiator.WriteMessage1(request));
        byte[] readResponse = initiator.ReadMessage2(responder.WriteMessage2(response));
        return readRequest.AsSpan().SequenceEqual(request) && readResponse.AsSpan().SequenceEqual(response);
    }

    private static bool TestNoiseTransportRoundTrip()
    {
        using var initiatorStatic = X25519Identity.Generate();
        using var responderStatic = X25519Identity.Generate();
        var (initiator, responder) = Handshake(initiatorStatic, responderStatic);
        using (initiator)
        using (responder)
        {
            byte[] up = "pc-to-phone"u8.ToArray();
            byte[] down = "phone-to-pc"u8.ToArray();

            if (!responder.TryDecrypt(initiator.Encrypt(up), out byte[] gotUp, out _)) return false;
            if (!initiator.TryDecrypt(responder.Encrypt(down), out byte[] gotDown, out _)) return false;

            // Each direction has its own key: one side's send cipher is the other's
            // receive cipher, and the split must not hand out the same one twice.
            return gotUp.AsSpan().SequenceEqual(up)
                && gotDown.AsSpan().SequenceEqual(down)
                && initiator.HandshakeHash.AsSpan().SequenceEqual(responder.HandshakeHash);
        }
    }

    private static bool TestNoiseHandshakeRejectsTampering()
    {
        using var initiatorStatic = X25519Identity.Generate();
        using var responderStatic = X25519Identity.Generate();
        using var initiator = NoiseHandshakeState.CreateInitiator(initiatorStatic, responderStatic.PublicKey);
        using var responder = NoiseHandshakeState.CreateResponder(responderStatic);

        byte[] message1 = initiator.WriteMessage1();
        message1[^1] ^= 0xFF;   // flip a bit in the encrypted payload
        try
        {
            responder.ReadMessage1(message1);
            return false;
        }
        catch (CryptographicException) { return true; }
    }

    private static bool TestNoiseHandshakeRejectsWrongResponder()
    {
        using var initiatorStatic = X25519Identity.Generate();
        using var responderStatic = X25519Identity.Generate();
        using var impostorStatic = X25519Identity.Generate();

        // The initiator was provisioned with the impostor's key, so the real
        // responder derives different keys and authentication must fail closed.
        using var initiator = NoiseHandshakeState.CreateInitiator(initiatorStatic, impostorStatic.PublicKey);
        using var responder = NoiseHandshakeState.CreateResponder(responderStatic);
        try
        {
            responder.ReadMessage1(initiator.WriteMessage1());
            return false;
        }
        catch (CryptographicException) { return true; }
    }

    private static bool TestNoiseHandshakeIsOrdered()
    {
        using var initiatorStatic = X25519Identity.Generate();
        using var responderStatic = X25519Identity.Generate();
        using var initiator = NoiseHandshakeState.CreateInitiator(initiatorStatic, responderStatic.PublicKey);
        using var responder = NoiseHandshakeState.CreateResponder(responderStatic);

        bool responderRefusesToWriteFirst = false;
        try { responder.WriteMessage2(); }
        catch (InvalidOperationException) { responderRefusesToWriteFirst = true; }

        bool initiatorRefusesToRepeat = false;
        initiator.WriteMessage1();
        try { initiator.WriteMessage1(); }
        catch (InvalidOperationException) { initiatorRefusesToRepeat = true; }

        return responderRefusesToWriteFirst && initiatorRefusesToRepeat;
    }

    private static bool TestNoiseSplitRequiresCompletion()
    {
        using var initiatorStatic = X25519Identity.Generate();
        using var responderStatic = X25519Identity.Generate();
        using var initiator = NoiseHandshakeState.CreateInitiator(initiatorStatic, responderStatic.PublicKey);
        try
        {
            initiator.Split();
            return false;
        }
        catch (InvalidOperationException) { return true; }
    }

    private static bool TestReplayWindowSemantics()
    {
        var window = new ReplayWindow();
        if (!window.TryAccept(0)) return false;
        if (window.TryAccept(0)) return false;               // exact replay
        if (!window.TryAccept(5)) return false;              // jump forward
        if (!window.TryAccept(3)) return false;              // in-window reorder
        if (window.TryAccept(3)) return false;               // reordered replay
        if (!window.TryAccept(4)) return false;

        // Far ahead, then anything older than the window is refused even though it
        // was never seen: freshness that cannot be proven fails closed.
        if (!window.TryAccept(1000)) return false;
        if (window.TryAccept(100)) return false;
        return window.TryAccept(1000 - ReplayWindow.WindowSize + 1)
            && !window.TryAccept(1000 - ReplayWindow.WindowSize);
    }

    private static bool TestNoiseTransportRefusesReplay()
    {
        using var initiatorStatic = X25519Identity.Generate();
        using var responderStatic = X25519Identity.Generate();
        var (initiator, responder) = Handshake(initiatorStatic, responderStatic);
        using (initiator)
        using (responder)
        {
            byte[] frame = initiator.Encrypt("command:pause"u8);
            if (!responder.TryDecrypt(frame, out _, out _)) return false;

            // The very same bytes, replayed: authentication still passes, so only
            // the replay window can stop it.
            if (responder.TryDecrypt(frame, out _, out string? replayReason)) return false;
            if (replayReason != "replayed_or_out_of_window") return false;

            byte[] forged = initiator.Encrypt("command:stop"u8);
            forged[^1] ^= 0xFF;
            bool forgedRejected = !responder.TryDecrypt(forged, out _, out string? forgedReason)
                                  && forgedReason == "authentication_failed";
            bool truncatedRejected = !responder.TryDecrypt(new byte[4], out _, out string? shortReason)
                                     && shortReason == "frame_truncated";
            return forgedRejected && truncatedRejected;
        }
    }

    private static bool TestNoiseTransportBindsSequence()
    {
        using var initiatorStatic = X25519Identity.Generate();
        using var responderStatic = X25519Identity.Generate();
        var (initiator, responder) = Handshake(initiatorStatic, responderStatic);
        using (initiator)
        using (responder)
        {
            initiator.Encrypt("first"u8);                      // consumes sequence 0
            byte[] second = initiator.Encrypt("second"u8);     // sequence 1

            // Rewrite the frame's sequence number to 0: the number is authenticated
            // as associated data, so the tag must fail rather than let the frame
            // masquerade as the one that was never delivered.
            second[7] = 0;
            return !responder.TryDecrypt(second, out _, out string? reason)
                && reason == "authentication_failed";
        }
    }
}
