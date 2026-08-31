# ADR-0011 — Who may hold the single Guard session

## Status

Accepted and **implemented**: both the admission deadline and bounded concurrent
admission are in the runtime. What remains open is stated under *Consequences*
and is not a gap in the implementation but a limit of the approach.

**Builds on:** [ADR-0007](ADR-0007-wifi-transport.md) — closes the second
consequence recorded there.

## Context

`GuardAiNetworkChannel` serves **one** phone. The accept loop takes the first
connection and closes every other one while it is held:

```csharp
if (_client is not null) { client.Close(); continue; }
```

Over USB that was unreachable: the cable bounds who can connect. On a LAN
(ADR-0007) it is not, and the consequence was recorded there — any host that
merely opens a socket can hold the slot and keep the paired phone out. No
credential is needed, because holding a TCP connection needs none.

What an unauthenticated peer can actually do today is narrower than it first
looks, and the difference matters:

- It **cannot** read anything. Under [ADR-0009](ADR-0009-session-payload-encryption.md)
  every post-handshake frame is encrypted, and a peer that cannot complete the
  handshake never obtains a key. It sees `Capabilities` and a challenge.
- It **cannot** hold the slot indefinitely. The heartbeat watchdog terminates a
  session that goes 2000 ms without a heartbeat, and `_lastHeartbeatUtc` is set
  at accept and never advanced during the handshake.
- It **can** deny service. It holds the slot for the full heartbeat budget, then
  reconnects and holds it again. The paired phone has no priority whatsoever; it
  simply races.

The eviction that limits the damage today is an accident rather than a policy.
`HeartbeatTimeout` exists to detect a *dead authenticated session*. It happens to
also bound an unauthenticated squatter, so anyone who later raises it for a flaky
network — an entirely reasonable change — silently widens this hole, with nothing
to say so.

Measured, so the numbers below are not guesses: a complete version 3 handshake
against the real runtime process takes a **median of 75 ms and a worst case of
151 ms over loopback** (12 samples, Python reference client, 2026-08-31). A phone
over Wi-Fi is slower — RSA on ARM, four round trips, a possible cold start — but
not by the order of magnitude that separates it from the budget below.

## Decision

**Only an authenticated peer owns the session. An unauthenticated connection is
a candidate, on a short clock of its own.**

### Accepted and implemented: an admission deadline

An unauthenticated connection has its own deadline, `AuthenticationDeadline`,
independent of `HeartbeatTimeout`:

- **1500 ms**, roughly ten times the worst measured handshake, so a slow phone on
  a slow network is never cut off mid-handshake;
- enforced by the existing watchdog, which now applies whichever deadline matches
  the session's state rather than one budget for both;
- terminating with `authentication_deadline_exceeded`, which names what happened
  instead of reporting a heartbeat that was never due.

The point is not mainly the 500 ms it saves. It is that **the bound on an
unauthenticated peer stops being a side effect of an unrelated setting.** Tuning
the heartbeat for a flaky network can no longer widen the window a squatter gets,
and a test pins that the two are independent.

An authenticated session is never displaced by a new connection. That is already
true and is now pinned by a test, because it is the property that stops the
reverse attack — a squatter kicking the real phone off.

### Accepted and implemented: bounded concurrent admission

The slot for *connecting* and the slot for the *session* are not the same slot.
The runtime admits up to **four** connections at once, runs their handshakes in
parallel, and gives the session to the first that authenticates, closing the
rest. A silent peer no longer excludes the phone at all: the phone gets its own
connection and its own handshake instead of a place in a queue.

Four, because unbounded admission is its own denial of service. When the set is
full the **least progressed** candidate is evicted first — one that has not sent
a hello is costing a slot for nothing, and "connect and stay silent" is the
cheapest way to squat. Among equals the oldest goes.

The prerequisite was real and is now done: `SessionAuth` held the handshake
nonces, both ephemeral keys and the derived session material **as fields on one
shared object**, so two handshakes at once overwrote each other. That state now
lives on a per-connection `HandshakeSession`; `SessionAuth` keeps only the long-
term keys and serialises access to them, since neither `RSA` instance is
documented as thread-safe. A candidate that loses the race has its handshake
abandoned and its derived material zeroed, so key material never outlives the
peer it was derived for.

Measured after the change: with a silent squatter already connected, a real
client authenticates in **82 ms** — it is admitted alongside, not queued behind.
Before, it was refused until the squatter's deadline expired.

### Rejected

- **Rate-limiting by source address.** An attacker on the LAN picks their own
  address, so it filters the honest and not the hostile.
- **An allowlist of source addresses.** It reintroduces the thing the project
  removed on purpose: an address the operator has to manage. Discovery exists so
  that nobody types an IP.
- **Requiring authentication before accepting the TCP connection.** There is no
  such thing; the handshake *is* what runs on the connection.

## Consequences

- **Denial of service is reduced, not closed, and the residue is narrower than
  it was.** Silent squatting no longer works at any volume: those candidates are
  evicted first and the phone is admitted alongside them. What remains is a peer
  that *speaks* — sends a well-formed hello, then stalls — often enough to keep
  all four slots occupied by candidates that look mid-handshake. That costs the
  attacker a real handshake round per slot instead of an idle socket, and it is
  still possible. Closing it needs something this decision does not have: a way
  to tell a candidate that will authenticate from one that will not, before it
  does. Saying it were solved would be worse than the flaw.
- **A flood is visible.** Every eviction and every dropped candidate reports a
  named reason, so the condition shows up as `admission_slots_full` and
  `authentication_deadline_exceeded` rather than as a phone that mysteriously
  will not connect.
- A phone whose handshake genuinely needs more than 1500 ms is dropped and must
  retry. Ten times the worst measured case makes that unlikely; if a real device
  ever gets near it, the fact belongs in the checklist and the budget should be
  raised deliberately rather than by widening the heartbeat.
- Two deadlines now exist where there was one. They mean different things — one
  bounds a handshake, the other detects a dead session — and conflating them is
  what this decision undoes.
- The phone needs no change. This is entirely a runtime admission policy; the
  wire contract, the handshake and the version are untouched.
