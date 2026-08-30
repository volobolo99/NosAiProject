# ADR-0006 — Canonical PC ↔ Phone Channel

**Status:** Accepted  
**Date:** 2026-08-30  
**Supersedes:** the transport described in `docs/WIFI_BRINGUP.md`

## Context

Three modules in the repository claimed the PC ↔ phone boundary, with mutually
incompatible wire formats:

| Module | Framing | Authentication | Port | Covered by Gate 1 tests |
|---|---|---|---|---|
| `src/NosAi.Runtime/Gate1/Gate1Runtime.cs` (`GuardAiNetworkChannel`) | NOSA binary `MAGIC/VERSION/TYPE/PAYLOAD_LEN/SEQ` | RSA-2048 challenge/response, single-use | 17471 | yes |
| `nosai/bringup/` (`guard_server.py`, `protocol.py`) | JSON lines over TCP | none | 8765 | no |
| `nosai/guard/protocol.py` | `GuardMessage` dataclasses, transport-neutral | none | n/a | no |

`docs/WIFI_BRINGUP.md` instructed the phone-side implementer to build against the
JSON-lines path. A phone client written that way could not talk to the Gate 1
runtime at all: the runtime speaks only NOSA binary framing with RSA
authentication. The contradiction was latent because no phone client exists yet,
so nothing had ever attempted the connection.

The Gate 1 checklist already records authentication, heartbeat fail-closed and
controlled reconnection as covered — and all of that evidence comes from the C#
channel, not from the JSON-lines path.

## Decision

`GuardAiNetworkChannel` is the **only** canonical PC ↔ phone channel.

- The Guard AI phone client must implement NOSA binary framing, the RSA-2048
  challenge/response handshake, the sequence guard and the 2 s fail-closed
  heartbeat, and connect on TCP/17471.
- `nosai/bringup/` and `nosai/guard/protocol.py` are **non-canonical**. They stay
  in the repository as earlier foundations with their existing tests, but they are
  outside Gate 1 and no client may be written against them.
- `docs/WIFI_BRINGUP.md` no longer describes a path to implement. It is retained
  as a historical record and carries a superseded notice.
- A failed Guard channel bind fails closed with a structured reason
  (`guard_port_in_use:<port>`). Unlike the operator dashboard, the runtime must
  not continue without this channel: it is the authenticated link, not
  observability.

## Consequences

- The two open `Guard AI smartphone` checklist rows are blocked on an application
  that does not exist, not on hardware availability. No Android/iOS project is
  present in the repository.
- Whoever writes the phone client has exactly one contract to target, and that
  contract is the one already covered by the Gate 1 suite.
- The JSON-lines bring-up server keeps working for local experiments but must not
  be presented as progress toward Gate 1, and it no longer defaults to 8765,
  which collided with the Python operator UI.
- If the JSON-lines transport is ever revived, it needs authentication before it
  can carry any Gate 1 claim, and this ADR must be superseded explicitly.
