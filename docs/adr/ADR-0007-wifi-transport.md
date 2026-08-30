# ADR-0007 — Wi-Fi Transport and LAN Discovery

**Status:** Accepted
**Date:** 2026-08-30
**Builds on:** [ADR-0006](ADR-0006-canonical-phone-channel.md)

## Context

The Guard AI application could only reach the runtime over USB, through an
`adb reverse` tunnel to loopback. That is fine for bring-up and useless in
practice: an operator watching a session does not keep the phone tethered.

Two things stood in the way, and both were invisible until the transport was
actually attempted:

1. `GuardAiNetworkChannel` bound `IPAddress.Loopback`. Over USB the tunnel
   terminates on loopback so it worked; over Wi-Fi the phone dials the PC's LAN
   address, where nothing was listening.
2. The phone had no way to learn that address, and asking the operator to type an
   IP is asking them to manage the thing the application exists to hide.

## Decision

**The Wi-Fi transport is supported, and the operator configures nothing.**

- The Guard channel binds all interfaces by default. `--guard-loopback-only` (or
  `NOSAI_GUARD_LOOPBACK_ONLY`) restores loopback-only, which disables Wi-Fi.
- The runtime answers LAN discovery probes on **UDP/17472**
  (`DiscoveryProtocol`), replying with the port its Guard channel is on. The app
  broadcasts, takes the first answer, and dials it. No address is ever typed.
- Discovery uses its own magic (`NOSD`, against the session's `NOSA`) and its own
  port. A discovery datagram cannot be read as a session frame.
- The transport choice is the only thing the operator picks, and it is remembered
  between launches. Keys, pairing and addresses are resolved by the tooling.
- Pairing happens once over USB, as part of `python -m nosai.phone.deploy`: the
  app publishes its public key, the tool collects it, and the runtime loads it
  from `data/guard_public_key.pem` without a flag. The device identity persists,
  so Wi-Fi afterwards needs no cable.

## Consequences

Discovery decides nothing. It answers "a runtime is reachable here"; every
authorisation still happens in the RSA handshake, and an unknown key is still
refused.

Binding beyond loopback is a real change to the attack surface, and three
consequences follow:

1. **The runtime is not authenticated to the phone.** This is the significant
   one. The channel proves the *phone* to the *PC* and not the reverse, so a
   hostile host on the same network can answer a discovery probe first, accept
   the connection, and act as a runtime: feeding the phone fabricated state, or
   collecting signatures over challenges of its choosing. Until the handshake is
   mutual, **the Wi-Fi transport belongs on a trusted network only.**
2. **One session at a time.** The channel serves a single phone, so any host that
   merely opens a connection can hold the slot and keep the real phone out. Over
   USB this was unreachable; on a LAN it is not.
3. **The payload is not encrypted.** Authentication is not confidentiality:
   anything able to observe the network sees the telemetry in clear.

None of these is introduced by Wi-Fi alone — 1 and 3 were already true — but USB
made them unreachable in practice. Naming them is the point: the transport is
usable now, and it is not yet safe on a network the operator does not control.

Closing consequence 1 means adding runtime-side authentication to the handshake,
which changes the wire contract and supersedes part of ADR-0006. That work is not
done here.

## Validation

Verified on 2026-08-30 against a physical Android device with the USB cable
detached and the `adb reverse` tunnel removed, so the LAN was the only possible
path:

```
adb devices                -> (empty)
phone -> 127.0.0.1:17471   -> refused
phone -> 192.168.0.4:17471 -> open

connected        True   LIVE
authenticated    True   LIVE
sessionId        648fbd94d9eb4085b3f80072085f386a
lastHeartbeatUtc 18:07:12 -> 18:07:15 -> 18:07:17   (advancing)
client.status    attached_os_session
```

The phone found the runtime by discovery, with no address entered.
