# ADR-0008 — Mutual authentication on the Gate 1 channel

## Status

Accepted.

## Context

ADR-0006 made NOSA framing and RSA-2048 the only canonical PC↔phone channel.
Version 1 of that channel authenticated the phone to the runtime and not the
reverse: the phone signed a raw 32-byte challenge chosen by whoever answered.

Over USB the cable bounds who can answer. On Wi-Fi (ADR-0007) it does not. A
host on the LAN that wins discovery can collect a phone signature over bytes it
chose, and without a role-bound transcript that signature can be replayed as if
the runtime had produced it.

## Decision

Bump the wire version to 2. Refuse a version 1 peer rather than downgrade.

Both sides sign a SHA-256 transcript, not the raw nonce:

```
NOSAI-GUARD-HANDSHAKE-V2 || 0x00 || role || 0x00 || clientNonce(32) || serverNonce(32)
```

`role` is `0x01` for the runtime and `0x02` for the phone. Signatures use
PKCS#1 v1.5 over that pre-hashed digest (`SignHash`), so the phone is not a
signing oracle for attacker-chosen bytes.

Sequence:

1. Phone → `SessionHello` (client nonce)
2. Runtime → `Capabilities`, `AuthChallenge` (server nonce), `ServerAuthProof` (`0x08`)
3. Phone verifies the proof against a runtime public key pinned at USB pairing
4. Phone → `AuthResponse` (client transcript signature)
5. Runtime → `AuthResult` then classified telemetry

The runtime persists `data/runtime_identity.pem` and writes
`data/runtime_public.pem` for pairing. The phone stores only the public half.
A missing pin is fail-closed.

## Consequences

- Version 1 clients cannot connect. Both ends ship together.
- Pairing must push the runtime public key onto the phone as well as collect
  the device key. `python -m nosai.phone.deploy` does both.
- Regenerating the runtime identity looks like an impostor to already-paired
  phones and requires a re-pair. That is visible and recoverable.
- The private identity lives in a file, not a hardware store. That limitation
  is unchanged from the phone's `DeviceIdentity` and remains recorded here.

## Validazione su dispositivo reale

Verificata il 2026-08-30 sul dispositivo Android `9125322104AC` contro il runtime
reale, in entrambi i trasporti.

Su Wi-Fi il tunnel `adb reverse` era rimosso e il loopback dal telefono risultava
rifiutato, quindi la LAN era l'unico percorso possibile. L'app mostrava
`192.168.0.4:17471`, l'indirizzo LAN del PC.

```
USB    authenticated True   sessionId 7f73c07ed84c42f880be53d46fbce329
Wi-Fi  authenticated True   sessionId dbb75562afea436194b348625c27388d
       heartbeat 20:45:02 -> 20:45:06 -> 20:45:09
```

Un difetto emerso solo qui: il pin del runtime veniva consegnato con `adb run-as`,
che funziona unicamente su build debuggable. L'APK è release, quindi la chiave non
arrivava mai e il Wi-Fi restava fail-closed senza modo di aprirlo. Il test che
copriva quel percorso asseriva l'uso di `run-as` e sarebbe passato per sempre
contro un command recorder. Ora la chiave passa dalla cartella esterna dell'app.
