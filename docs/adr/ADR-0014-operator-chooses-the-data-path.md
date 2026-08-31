# ADR-0014 — The operator chooses the data path

## Status

Accepted.

**Supersedes:** [ADR-0013](ADR-0013-injection-not-adopted.md) entirely.
**Amends:** [ADR-0012](ADR-0012-gameplay-observation-source.md) — its prohibitions
are lifted; its reasoning about correctness and classification is kept and
restated below as a requirement.

## Context

ADR-0012 rejected memory reads and game-protocol access as a first provider, and
ADR-0013 went further: it recorded that the operator's acceptance of account risk
"was heard and is not enough". That second decision refused a choice that was not
the deciding party's to refuse.

The facts that matter here are not in dispute:

- The machine is the operator's. The game client runs on it, under their account.
- The account exposed to any consequence is the operator's own.
- This project is personal tooling. It is not sold, not distributed, and not
  operated on anyone else's behalf.
- The operator has asked twice, explicitly, with the risk stated.

ADR-0013 bundled three different things under one refusal — **observing** the
client's own traffic, **reading** its memory, and **evading** detections — and
answered all three the same way. They are not the same, and the strongest
argument ADR-0012 made was never about bans at all: it was that a wrong memory
offset returns a plausible number, and a source that cannot tell correct from
incorrect cannot be honestly classified.

That argument is still right. It is an engineering requirement, not a reason to
forbid the technique.

## Decision

**The operator decides which data paths this project uses. The prohibitions in
ADR-0012 and ADR-0013 are lifted.**

Available as engineering options, to be chosen on their merits:

- capturing and decoding the traffic between the client and its server;
- reading the client's process memory;
- driving the client through input or through its own protocol.

None of these is now blocked by a decision record. Each is still subject to
everything below, which is what ADR-0012 was actually protecting.

### What does not change

**Safety remains the authority.** [ADR-0003](ADR-0003-runtime-safety-authority.md)
stands. A new data source does not acquire the right to act; it feeds the same
`Observe → WorldState → Decision → Safety → Execute → Verify` path, and the gate
still refuses what it must. Widening the input does not widen the authority.

**Classification stays honest.** [ADR-0002](ADR-0002-real-demo-data-separation.md)
stands, and it binds these sources harder than it binds the ones already in
place:

- A value decoded from the wire is `LIVE` **only** when it was actually read from
  the stream and parsed by a decoder that verified its framing. A decode that
  falls out of sync yields `UNKNOWN`, never the last good value and never a
  plausible-looking number.
- A value read from process memory is `LIVE` **only** while a validity check
  passes — a known signature, a bounded range, continuity against the previous
  read. Without such a check the offset may have moved and the read is `UNKNOWN`.
  This is the requirement ADR-0012 was right about, kept.
- Screen-derived values remain `DERIVED`, as ADR-0012 said. That did not depend
  on the prohibition and is unaffected.

**`UNKNOWN` is still not zero.** A source that fails is a source that says so.

### What is still not implemented

**Detection evasion.** Making the runtime hard for an anti-cheat to notice is a
different activity from reading data or driving a client: it is work aimed at
defeating someone else's security control, and it is not something these agents
build. Nothing here depends on it — the data paths above work or fail on their
own merits, and the operator's stated purpose is to see the data in detail, not
to be unseen.

This is one named technique, not a reinstated category. Everything else asked for
is available.

## Consequences

- **ADR-0013 is superseded and marked as such**, rather than deleted. A decision
  record that vanishes takes the reasoning with it, and the next person to ask
  this question deserves to find both the refusal and its reversal. The
  prohibition is gone; the history is not.
- **ADR-0012 keeps its analysis and loses its veto.** Its comparison of the three
  sources is still the best summary of their failure modes, and screen perception
  remains a legitimate option — now one among several rather than the only one.
- **`.cursor/rules/25-connection-and-ban-risk.mdc` is rewritten** to match. A
  standing rule that contradicted an accepted ADR would be the exact documentary
  disagreement these records exist to prevent.
- **Account risk is the operator's, and is real.** Traffic interception and
  memory reads are what anti-cheat and server-side statistics are built to
  notice, and this decision does not reduce that. It records that the person
  carrying the risk is the person who decided.
- **Brittleness is now an engineering problem to solve, not an argument.** Offsets
  move with patches and protocols change; every such provider carries its own
  validity check, and the runtime reports `UNKNOWN` the moment one fails rather
  than acting on a stale interpretation.
