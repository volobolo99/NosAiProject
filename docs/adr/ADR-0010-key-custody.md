# ADR-0010 — Custody of the long-term identity keys

## Status

Accepted and **implemented on both sides**. The PC half is verified locally,
including against this machine's real identity. The phone half compiles and is
**not yet exercised on a device**, so it is `Integrated`, never `Verified`. One
deviation from the decision as first written is recorded under *Implementation*.

**Builds on:** [ADR-0008](ADR-0008-mutual-handshake.md),
[ADR-0009](ADR-0009-session-payload-encryption.md) — closes the third limit
recorded in `docs/GATE1_CHECKLIST.md`.

## Context

Two long-term RSA-2048 identities decide who may talk on the Gate 1 channel:

- the **runtime identity**, `data/runtime_identity.pem`, which the phone pins at
  pairing and verifies before it signs anything;
- the **device identity**, held in the Guard AI application's private storage,
  which the runtime holds the public half of in `data/guard_public_key.pem`.

Both private halves are plain files. On Windows, `data/` is readable by any
process running as the same user, and by anything with the disk. On Android, app
private storage is protected by the OS sandbox — which is real protection, and
stops at a rooted device, a backup, or an unlocked bootloader.

What their loss costs, precisely:

- **The runtime key** lets an attacker impersonate the runtime to a paired
  phone: answer discovery, present a valid `ServerAuthProof`, and feed the
  operator fabricated state.
- **The device key** lets an attacker impersonate the phone to the runtime, and
  so read the classified Gate 1 snapshot. Today the snapshot is all it gets,
  because execution is disabled in Gate 1. Past Gate 1 the same key is what
  authorises commands, so its value grows with the project.

[ADR-0009](ADR-0009-session-payload-encryption.md) already removed the worst
consequence: session keys are ephemeral, so a stolen identity file no longer
decrypts traffic recorded before the theft. What remains is impersonation going
forward, and that is what this decision addresses.

Both keys are also **recoverable by re-pairing**, which matters: this is a
confidentiality problem, not a durability one. Losing a key costs one re-pair.

## Decision

**Keep the private halves out of readable files, on both sides, without changing
the wire contract.**

Nothing about the handshake changes. Both sides still sign the same
`SessionTranscript`; only where the private key lives, and what performs the
signature, changes. There is no second handshake and no new message type.

### PC runtime — DPAPI

The runtime identity is stored wrapped with Windows DPAPI
(`ProtectedData.Protect`, `DataProtectionScope.CurrentUser`) as
`data/runtime_identity.dpapi`. The plaintext PEM is never written again.
`data/runtime_public.pem` is unchanged: it is public, and pairing depends on it.

`CurrentUser` rather than `LocalMachine` scope, deliberately: `LocalMachine`
would let any account on the PC unwrap the key, which is most of what this is
meant to prevent. The consequence is that the runtime must run as the user that
created the identity — which is how it is run today, and which must be stated
rather than discovered.

### Phone — Android Keystore

The device identity is **generated inside** the Android Keystore
(`AndroidKeyStore` provider, RSA-2048, signature-only, no user authentication
requirement so an unattended session still works). The private key never enters
application memory and cannot be exported; signing is performed by the keystore.

Generated inside, not imported into it. An imported key is software-backed on
many devices, which would give the appearance of hardware custody without the
substance — and appearing safer than you are is worse than the plain file, which
at least is honestly plain.

## Migration

This is the part that decides whether the decision can be applied, and the two
sides are not symmetric.

**The PC migrates cleanly.** On start, if `runtime_identity.pem` exists and the
wrapped file does not, the runtime wraps it and deletes the plaintext. The key
material is unchanged, so the public half is unchanged, so **every already-paired
phone keeps working** and no re-pair is needed.

**The phone cannot migrate.** A keystore-generated key is a *new* key, so the
device identity changes, so the runtime's trusted key no longer matches. Every
paired phone must re-pair. The path exists and is one command —
`python -m nosai.phone.deploy` already collects the new public key and pushes the
runtime pin — but it is a re-pair, not a migration, and it must be announced
rather than discovered as "the phone stopped connecting".

## Consequences

- **A re-pair is required on the phone**, once, when this ships. The runtime side
  is silent and automatic.
- **The runtime becomes bound to one Windows user account.** Running it as a
  different user, or as a service under another identity, produces a runtime that
  cannot unwrap its own key. It must fail closed with that reason named, not
  silently generate a new identity — a new identity looks like an impostor to
  every paired phone, which is the same symptom as a compromise and would send
  the operator looking in the wrong place.
- **DPAPI is not a hardware store.** It ties the key to the user account, not to
  a TPM. It stops a file copy and another account; it does not stop code running
  as that user. That is a real improvement over a plain file and should not be
  described as more.
- **The phone key becomes non-exportable** wherever the Keystore is available,
  so it can no longer be read out for diagnostics. `nosai.phone.enroll` reads only
  the public half from the log, so the pairing flow is unaffected. Where the
  Keystore is not available the key stays a file and the app says so; see the
  deviation under *Implementation*.
- **Backup and reinstall lose the device key**, by design. Cost: one re-pair.
- The Gate 1 checklist's third limit narrows from "keys in files" to "the PC key
  is user-bound, not hardware-bound"; the phone key moves out of the limit.

## Implementation

### PC — done and verified locally

`RuntimeIdentity` wraps the private key with DPAPI at
`data/runtime_identity.dpapi` and migrates an existing plaintext PEM on first
load. Verified on this machine's real identity, which is the check that mattered:

```
before  data/runtime_identity.pem   1678 bytes, readable PEM
after   data/runtime_identity.dpapi 1414 bytes, no PEM header anywhere in it
        data/runtime_identity.pem   gone
        data/runtime_public.pem     byte-identical to the backup
second start -> loads from the wrapped file, public key still identical
```

The public half is unchanged, so **no paired phone has to be paired again** for
the PC side. That is what makes this a migration.

One behaviour changed beyond storage, and it is the more important half:
**`LoadOrCreate` no longer replaces an identity it cannot read.** It used to fall
through to a fresh key on the reasoning that refusing to start is worse than a
re-pair. That was wrong in the case that matters — a runtime that silently adopts
a new identity presents to every paired phone exactly as an impostor does, and
the operator sees trust fail with no cause given. It now throws
`RuntimeIdentityException` with a named reason and the remedy in the message.
Only a genuinely absent identity produces a new one.

The reason for a failed unwrap is `identity_unwrap_failed`, not
"wrong Windows account": DPAPI reports the same error for a blob protected by
another account and for a damaged file, so the reason says what is known and the
message names both causes. Guessing would misdirect the operator half the time.

### Phone — done, not yet exercised on a device

The device identity is generated inside `AndroidKeyStore`
(`KeystoreDeviceSigner`), signature-only, RSA-2048, SHA-256 with PKCS#1 v1.5.

The obstacle was that a Keystore key **cannot be handed a digest computed outside
it** — it signs with `SHA256withRSA`, hashing the message itself — while the
handshake signed a pre-computed transcript digest. Rather than change the wire
contract, the client now signs `SessionTranscript.Message(...)`, the buffer the
digest is taken over. `SignData(message)` and `SignHash(SHA256(message))` are the
same bytes under PKCS#1 v1.5, so the runtime verifies exactly what it verified
before. The equivalence is pinned by a test, because a silent divergence would
look like a phone that suddenly cannot authenticate.

`GuardAiClient` now takes an `IDeviceSigner` instead of an `RSA`. The old
constructor still exists and wraps the key, so the reference client and the tests
did not change.

### Deviation: the fallback is reported, not removed

This ADR first said Keystore or nothing. What shipped is Keystore **or a file
that says it is a file**: `IDeviceSigner.Custody` reports
`PlatformKeyStore` or `AppPrivateFile`, the reason the Keystore was unavailable
is kept, and both go into the pairing log.

The reasoning that changed: refusing to run on a device without a usable Keystore
would brick pairing on hardware this turn could not test, to close a limit that
is currently declared and understood. A fallback that announces itself is not the
thing this ADR warned about — that was a fallback that *looks* protected. Custody
becomes an observed property with a value, which is how every other uncertain
fact in this project is handled.

### Still open

- **A device round.** Generation, pairing and a session over USB and Wi-Fi have
  not been run against hardware. Until then the phone half is `Integrated`.
- **The re-pair is real.** A Keystore key is a new key, so the first launch after
  this change produces a new device identity and the runtime must enroll it.
  `python -m nosai.phone.deploy --reinstall` does that in one command, and it is
  announced here rather than discovered as "the phone stopped connecting".
- **DPAPI is not a hardware store.** It ties the key to the Windows account, not
  to a TPM. It stops a copied file and another account; it does not stop code
  running as that account.
- **The runtime is now bound to one Windows account.** Running it as a different
  user fails closed with the reason named, which is the intended behaviour and
  is worth knowing before it happens.
