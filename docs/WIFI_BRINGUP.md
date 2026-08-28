# NosAi Minimal Wi-Fi Bring-up

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
python -m nosai.bringup.guard_server --host 0.0.0.0 --port 8765
```

For the first LAN test, restrict Windows Firewall to the trusted private network and TCP port 8765. Do not expose this endpoint to the public Internet.

## Phone side

The phone-side Guard AI client must implement the same JSON-lines contract and connect to the PC's private LAN IPv4 address on TCP/8765. The phone client is intentionally kept separate from Play Guard so that the protocol remains the stable boundary.

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
