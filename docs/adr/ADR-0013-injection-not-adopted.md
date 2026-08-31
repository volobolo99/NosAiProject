# ADR-0013 — Client injection, traffic intercept and detection evasion are not adopted

## Status

**Superseded by [ADR-0014](ADR-0014-operator-chooses-the-data-path.md). Nothing
below is in force.**

Kept rather than deleted: this record is why the question was asked twice, and
removing it would leave the reversal looking as though it had never had an
argument against it.

The decision it recorded — that the operator's acceptance of account risk "was
heard and is not enough" — refused a choice belonging to the person carrying the
risk. ADR-0014 lifts the prohibition and keeps the one part of the reasoning that
was never about bans: a source that cannot tell a correct reading from an
incorrect one cannot be honestly classified.

**Originally:** Accepted, building on
[ADR-0003](ADR-0003-runtime-safety-authority.md) and
[ADR-0012](ADR-0012-gameplay-observation-source.md).

## Context

The operator asked for client injection, interception of the game protocol, and
evasion of detections, and **accepted the risk to their own game account**.

ADR-0012 already rejected memory reads and game-protocol parsing as a first
provider: a wrong offset looks like a real number, and touching another process
or its traffic is what anti-cheat and server-side stats are built to notice.
Public NosTale tooling of that class injects into `NostaleClientX` to log or
send packets; publishers and private servers treat that as hostile. Gate 1
already reports live input and packet injection as disabled.

An ADR is the place to record a change of mind. This one records that the
account-risk acceptance **was heard and is not enough**.

## Options considered

### Adopt injection / intercept / evasion because the operator accepts the ban

This would make the runtime depend on undocumented client internals, put
untrusted bytes on the path that Safety must not treat as `LIVE`, and ask
agents to implement process injection and detection evasion. The last of those
is not an architecture choice we will encode.

**Not adopted.**

### Keep screen observation; treat the request as closed

Gameplay observations, when a provider exists, still come from classified
screen perception (`DERIVED`, never `LIVE`). No DLL into the game, no hook of
its send/recv, no forging of its packets, no “stealth” layer.

**Adopted.**

## Decision

**Operator acceptance of account risk does not authorise client injection,
interception of the game protocol, or evasion of detections.**

Those paths are out of scope for implementation. They are not queued behind a
future “yes”. A later ADR that wanted memory or protocol access would still
have to argue against ADR-0012 on **correctness and Safety**, not only on ban
risk — and it still could not ask for detection evasion.

`.cursor/rules/25-connection-and-ban-risk.mdc` stays aligned with this
decision.

## Consequences

- Agents do not implement, complete, or “just sketch” injectors, packet
  loggers/senders against the game, or anti-detection work.
- The next gameplay step remains a screen-derived provider under ADR-0012
  (HP/MP first), or `UNKNOWN` until that exists.
- Gate 1 execution and packet injection remain disabled unless a **different**
  accepted ADR changes Safety, which this one does not.
