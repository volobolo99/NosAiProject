# ADR-0009 — Authenticated encryption of the Gate 1 session payload

## Status

Accepted.

**Builds on:** [ADR-0006](ADR-0006-canonical-phone-channel.md),
[ADR-0007](ADR-0007-wifi-transport.md), [ADR-0008](ADR-0008-mutual-handshake.md)
— closes the third consequence of ADR-0007.

## Context

ADR-0008 made the handshake mutual, and that was verified on a real handset over
USB and Wi-Fi. It closed impersonation. It closed nothing about confidentiality:
**authentication is not encryption**, and after `AuthResult` every byte still
travels in clear.

What that leaks is not abstract. The `TelemetrySnapshot` carries the classified
Gate 1 snapshot — the client process name and PID, the window handle, host
identifiers, session state. Anyone able to observe the network reads all of it,
and can also see the size and cadence of every frame. On the trusted-network
assumption ADR-0007 started from, that was tolerable. Once mutual authentication
made the Wi-Fi transport usable on a network the operator does not fully control,
it is the largest remaining hole in the channel.

Two properties are missing, and only one of them is obvious:

1. **Confidentiality.** An observer reads the payload.
2. **Integrity of the payload in transit.** The RSA signatures prove *who* is at
   each end at handshake time. They say nothing about the frames that follow, so
   an active attacker on the path can flip bits in a snapshot and neither end
   notices.

The long-term RSA keys sit in files, not in a hardware key store — this is a
declared, still-open limitation. That makes **forward secrecy** worth paying for
rather than optional: without it, a key file recovered later decrypts every
session ever recorded.

## Decision

**Bump the wire version to 3 and encrypt the session payload after the version 2
handshake, under keys that the version 2 signatures already authenticate.**

No new trust root and no second handshake. The ephemeral key exchange is folded
into the transcript that both sides already sign, so the RSA identities
authenticate the ephemeral keys as a side effect of proving themselves.

### Key exchange, bound to the signed transcript

Each side generates an ephemeral **NIST P-256** key pair per session and sends
its public key as an uncompressed X9.62 point (65 bytes, `0x04 || X || Y`):

- `SessionHello` payload becomes `clientNonce(32) || clientEphemeral(65)` — 97 bytes.
- `AuthChallenge` payload becomes `serverNonce(32) || serverEphemeral(65)` — 97 bytes.

The transcript label becomes `NOSAI-GUARD-HANDSHAKE-V3` and covers both
ephemeral keys:

```
NOSAI-GUARD-HANDSHAKE-V3 || 0x00 || role || 0x00
  || clientNonce(32) || serverNonce(32)
  || clientEphemeral(65) || serverEphemeral(65)
```

`role` stays `0x01` for the runtime and `0x02` for the phone, and signatures stay
PKCS#1 v1.5 over the pre-hashed digest (`SignHash`). Because each signature now
covers both ephemeral keys, an attacker who substitutes one of them invalidates
the signature it is carried under. **The key exchange is authenticated by the
handshake that already exists**, which is the whole reason this is a small change
rather than a new protocol.

P-256 rather than X25519: the shared `NosAi.Protocol` assembly is deliberately
dependency-free because it compiles for both a Windows runtime and an Android
application, and the .NET BCL has no Curve25519. X25519 would mean a
BouncyCastle dependency inside the phone application, or two divergent
implementations of the same agreement. `ECDiffieHellman` over P-256 is in the
BCL, is in Python's `cryptography`, and needs neither. The repository's other
crypto core (`EphemeralSession`, X25519, runtime-internal) is untouched and stays
what it is.

### Key schedule

```
Z          = ECDH(P-256, own ephemeral private, peer ephemeral public)
ikm        = SHA-256(Z)
binding    = SHA-256(label || 0x00 || 0x00 || 0x00 || clientNonce || serverNonce
                     || clientEphemeral || serverEphemeral)
keys(64)   = HKDF-SHA256(ikm, salt = binding, info = "NOSAI-GUARD-SESSION-V3")
c2s        = keys[0..32]     client → server
s2c        = keys[32..64]    server → client
```

The role byte in `binding` is `0x00`, which is not a valid signing role, so a
key-derivation input can never collide with a digest either side would sign.

Directional keys are not decoration: with one shared key, a frame captured in one
direction could be replayed back down the other, and it would decrypt.

### Frame format

The 12-byte header stays in clear and unchanged — the stream cannot be framed
otherwise — and becomes the AEAD **associated data**, so the message type, the
declared length and the sequence number are all authenticated even though they
are readable.

```
header(12, clear, authenticated)  ||  nonce(12)  ||  ciphertext  ||  tag(16)
```

- AEAD is **AES-256-GCM**: available in the BCL and in Python's `cryptography`,
  supported on every Android level this application targets, and hardware
  accelerated on the target handset.
- The nonce is `0x00000000 || uint64 big-endian counter`, counted per direction
  from zero. It **never wraps**: at exhaustion the sender refuses to encrypt
  rather than repeat a nonce, which under GCM would forfeit both confidentiality
  and integrity.
- The receiver requires the nonce to equal the counter it expects. Transmitting
  it keeps a captured frame independently decryptable given the key; checking it
  leaves the peer no freedom to choose it.
- `PayloadLength` counts the whole encrypted blob, so the maximum plaintext is
  `65535 - 28 = 65507` bytes.

### What is encrypted

Handshake messages — `SessionHello`, `Capabilities`, `AuthChallenge`,
`AuthResponse`, `AuthResult`, `ServerAuthProof` — travel in clear, because they
are what establishes the keys. They are already authenticated by RSA.

Every other message travels encrypted, and **only** encrypted: `Heartbeat`,
`HeartbeatAck`, `WorldStateDelta`, `TelemetrySnapshot`, `CommandRequest`,
`CommandAck`, `Disconnect`. A non-handshake frame that arrives before the keys
exist, or that fails to decrypt, terminates the session. There is no path on
which a payload is read in clear after the handshake.

### No downgrade

`WireHeader.TryRead` accepts exactly `CurrentVersion` and refuses everything
else, so version 1 and version 2 peers are rejected at the header with
`unsupported_version`. There is no negotiation and no plaintext fallback: a
channel that agrees to skip encryption when the peer asks is a channel with no
encryption. Both ends ship together, so there is nothing to stay compatible with.

### Out of scope

**LAN discovery is untouched.** UDP/17472 keeps the `NOSD` magic and its current
datagram, unversioned by this decision. Discovery still decides nothing and
carries nothing secret: it answers "a runtime is reachable here", and every
authorisation happens afterwards in the handshake.

## Consequences

- **Recorded traffic stays unreadable if a key file is later stolen.** The
  ephemeral keys are per session and never persisted. This is the property that
  makes the file-based key storage survivable rather than fatal, and it is the
  reason ephemeral agreement was chosen over encrypting to the pinned RSA key.
- **Every peer must be rebuilt.** Runtime, `NosAi.GuardClient`, the MAUI
  application and the Python reference client change together. A stale APK cannot
  connect — visibly, at the header, not as a silent misparse.
- **The transcript changes, so every pinned signature vector changes.** The
  version 2 vectors are replaced by version 3 vectors on both sides, and a
  negative test asserts a version 2 digest does not verify under version 3. The
  vectors are what stop C# and Python drifting apart.
- **Frames grow by 28 bytes** and each carries an AES-GCM operation. On snapshots
  of a few hundred bytes at heartbeat cadence this is not a cost worth measuring.
- **Traffic analysis still works.** Frame sizes and timing are visible. Padding
  and cover traffic are not part of this decision; the threat this closes is
  reading and tampering, not observing that a session exists.
- **A dropped or reordered frame is fatal to the session**, because the nonce
  counters and the sequence guard must stay in lockstep. On TCP that only happens
  when the connection is already broken, and the session is torn down anyway.
- **Key storage is unchanged and still open.** Long-term RSA keys remain in
  files. This decision reduces what their loss costs; it does not protect them.
