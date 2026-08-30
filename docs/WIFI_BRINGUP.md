# NosAi Minimal Wi-Fi Bring-up

> **SUPERSEDED — do not implement a phone client against this document.**
>
> `docs/adr/ADR-0006-canonical-phone-channel.md` makes `GuardAiNetworkChannel`
> (NOSA binary framing, RSA-2048 challenge/response, TCP/17471) the only canonical
> PC ↔ phone channel. The JSON-lines transport described below has no
> authentication and the Gate 1 runtime does not speak it, so a phone client built
> from this document cannot connect to the runtime.
>
> This file is kept as a historical record of the first transport experiment. The
> completion gate at the bottom is **not** a Gate 1 gate and passing it proves
> nothing about Gate 1.

## Goal

Prove the smallest reliable runtime path before adding vision, memory, local LLM optimization, or game-client integration:

`Play AI -> Play Guard (PC) <-> Wi-Fi <-> Guard AI (phone)`

The first transport is TCP with newline-delimited UTF-8 JSON. The protocol is transport-neutral so the transport can be replaced later without changing message contracts.

## Session contract

1. PC accepts a connection and creates a random `session_id`.
2. PC sends `HELLO` and `CAPABILITIES`.
3. Phone answers with `HELLO` using the same `session_id`.
4. Both sides exchange `HEARTBEAT` messages during the session.
5. `STATUS` is used for connection state and acknowledgements.
6. Any malformed message, protocol mismatch, session mismatch, or socket failure terminates the session safely.
7. Reconnect starts a new session; stale session IDs are rejected.

## PC bring-up

From repository root:

```powershell
python -m nosai.bringup.guard_server --host 0.0.0.0 --port 8769
```

For the first LAN test, restrict Windows Firewall to the trusted private network and TCP port 8769. Do not expose this endpoint to the public Internet.

The port is 8769, not 8765: 8765 belongs to the Python operator UI.

## Phone side

**Do not build the phone client from this section.** Per ADR-0006 the Guard AI
phone client implements NOSA binary framing with the RSA-2048 handshake and
connects on TCP/17471 — see `src/NosAi.Runtime/Gate1/Gate1Runtime.cs`. The
JSON-lines contract described here is unauthenticated and the Gate 1 runtime does
not accept it.

## Completion gate

This gate is complete only after a physical PC and phone demonstrate:

- successful discovery/connection using the PC LAN address;
- HELLO/CAPABILITIES exchange;
- sustained heartbeat exchange;
- status acknowledgement;
- clean disconnect;
- successful reconnect with a new session ID;
- no dependency on NosTale or privileged game I/O.

Only after this gate passes should the project proceed to richer Play AI/Guard functionality.
