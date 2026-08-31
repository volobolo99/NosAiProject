# ADR-0012 — Where gameplay observations may come from

## Status

Accepted, and **amended by
[ADR-0014](ADR-0014-operator-chooses-the-data-path.md)**: its prohibitions on
memory reads and protocol access are lifted, and the operator chooses the data
path.

What survives is everything this record says about **classification and
correctness** — screen-derived values stay `DERIVED`, a reading that fails its
checks stays `UNKNOWN`, and a source that cannot tell a correct value from a
wrong one is never `LIVE`. Read the comparison below as an account of failure
modes, not as a list of what is permitted.

**Implementation status (2026-08-31).** The seam is implemented and wired:
`IGameplayProvider` / `GameplayObservation` in
`src/NosAi.Runtime/LiveIntegration/GameplayProvider.cs`, published by the Gate 1
snapshot under the existing `gameplayBaseline` key and read by
`Gate1SnapshotWorldStateSource` into `Gate3WorldState`. With no provider attached
— still the default — the snapshot reports exactly what it reported before, so
nothing changed for anyone who has not opted in.

`NetworkGameplayProvider` is the first implementation, over the scoped network
observation channel. **It cannot yet report vitals**, and the reason is not a
missing feature: no NosTale protocol map exists. `ProtocolMap.PlayerVitals` is the
optional entry an operator has to derive by correlating captured traffic against
values read off the client's own screen. Until it exists, HP is `UNKNOWN` with
`player_vitals_not_mapped` and Gate 3 keeps refusing to plan.

That refusal is the decision below working, not a gap in it. A ratio is not an
HP, and manufacturing a maximum to turn one into the other would be exactly the
plausible-wrong-number this record rejected memory offsets over.

**Builds on:** [ADR-0002](ADR-0002-real-demo-data-separation.md) (source
classification), [ADR-0003](ADR-0003-runtime-safety-authority.md) (the runtime is
the safety authority).

## Context

Everything about the client that Windows can answer for is `LIVE` and verified on
the real game: process name, PID, window title and handle, responding, visible.
Everything about the *game* — HP, MP, the current map, entities — is `UNKNOWN`,
because nothing reads it.

That single gap is now the binding constraint on the whole project. Gate 2's
world model has no real input. Gate 3's loop plans over `NoWorldState`. Gates 4
to 6 can only demonstrate themselves against a simulated world, which is why they
stay `Integrated` no matter how green their suites are. The first circuit is
closed and verified; the first *decision* is not, and cannot be until something
observes the game.

There are three ways to get those values, and they are not equivalent.

## Options considered

### Reading the client's memory

`ReadProcessMemory` against the game process, at known offsets, yields exact
values with no interpretation step. It is also the option with the worst failure
mode for this project and the highest cost to the operator.

- **A wrong offset does not fail. It returns a plausible number.** Offsets move
  with every game patch, and nothing in the read distinguishes "HP is 412" from
  "this address now holds something else that happens to be 412". A source that
  cannot tell correct from incorrect cannot honestly be classified, and a
  confidently wrong HP is exactly what the safety gate must never act on.
- It reaches into another process's address space. On the operator's own machine
  with their own client that is their decision to make, but it is the kind of
  access anti-cheat is built to detect, and the cost of being wrong is the
  operator's account, not a failed test.
- It would make the runtime's correctness depend on an undocumented layout owned
  by someone else, re-derived by hand after every patch.

**Available under [ADR-0014](ADR-0014-operator-chooses-the-data-path.md).** One
requirement from this analysis survives and is not negotiable: a memory provider
carries a validity check able to tell a stale offset from a real value, and
publishes `UNKNOWN` when it cannot. The failure mode described above is why.

### Reading the game's network protocol

Parsing the client's traffic is the most authoritative source for entities and
map state. It requires understanding — and in practice decrypting — a protocol
the project does not own, which is a larger undertaking than either alternative
and carries the same detection exposure as memory reads.

**Available under [ADR-0014](ADR-0014-operator-chooses-the-data-path.md)**, and
the most direct answer to "see the exchanged data in detail". The caution that
survives: a decoder that falls out of sync must yield `UNKNOWN` rather than a
plausible misreading — the same requirement placed on memory reads.

### Reading what is on screen

Desktop Duplication (DXGI) captures the frame the operator is already looking at;
regions of interest and OCR turn parts of it into values. The repository already
has the foundations, and the operator's own eyes are the reference for whether it
is right.

- It reads no other process and injects nothing. It observes the same output a
  human does.
- It is brittle in a *visible* way: a changed resolution, an occluded window or a
  different UI scale makes the read fail or produce nonsense, and both are
  detectable rather than silent.
- Its error is measurable. OCR yields a confidence; a value can be range-checked
  and compared against the previous one. That is what makes it classifiable,
  which is the property the alternatives lack.

**Adopted** as the source gameplay observations may come from.

## Decision

**Gameplay observations come from classified screen perception, and they are
never `LIVE`.**

### Screen-derived values are `DERIVED`

`LIVE` means the value was read from whatever is authoritative for it. Windows is
authoritative for a process id, so `processId` is `LIVE`. Nothing is
authoritative for "HP is 412" except the game itself, and pixels are an
interpretation of its output with a non-zero error rate. Labelling that `LIVE`
would be the same failure as labelling a simulation live, only harder to notice.

So: a value recognised on screen is `DERIVED`, and carries the confidence it was
recognised with. A value that fails recognition, or fails its plausibility check,
is `UNKNOWN` — never zero, never the last known number silently reused.

`CACHED` is available and means one specific thing: a `DERIVED` value that is no
longer fresh, carrying the time it was observed. A stale HP presented as current
is a lie with a timestamp available to prevent it.

### A reading must be checked before it is published

OCR returns a string. A provider publishes an observation. Between the two:

1. **Range.** HP is bounded by a maximum that is itself observed and stable
   across frames. A value outside `0..max` is not a low-confidence reading, it is
   a failed one.
2. **Continuity.** Between consecutive frames the plausible change is bounded. A
   jump from 412 to 4,120,000 is a mis-read, not a game event; a jump from 412 to
   0 is not.
3. **Confidence.** Below the provider's threshold the answer is `UNKNOWN`.

Anything that fails these is `UNKNOWN`. The point is not to salvage readings, it
is to make garbage impossible to mistake for data.

### Safety acts on provenance, not just on value

The safety gate must see the classification, not only the number. An action whose
justification depends on a gameplay value it cannot trust must be refused, not
attempted on a guess — [ADR-0003](ADR-0003-runtime-safety-authority.md) already
puts that authority in the runtime, and this is what it is for.

One consequence deserves stating on its own: **verification must not compare a
derived observation against itself.** If the executor's expected outcome and the
verifier's observation come from the same fallible source, a mis-read confirms
the mis-read. Gate 3 had exactly that shape before, comparing the simulation to
itself, and it must not come back through perception.

### Staging

Not everything at once, easiest to verify first:

1. **HP and MP.** Bounded, numeric, range- and continuity-checkable, and the most
   safety-relevant. This is where a provider proves it can be honest.
2. **Current map.** A label, checkable against a known set: an unrecognised name
   is `UNKNOWN` rather than a guess.
3. **Entities.** Positions and identities of things on screen. Hardest, least
   verifiable, and **explicitly not attempted** until 1 and 2 are real on the
   operator's machine.

## Consequences

- **Gameplay stays `UNKNOWN` until a provider exists and passes the checks
  above.** No milestone may be marked complete by producing a number the runtime
  cannot vouch for.
- **`DERIVED` gameplay never promotes a gate to `VERIFIED` on its own.** It makes
  the closed loop possible; whether the loop is right is a separate question with
  its own evidence.
- **The perception path is observable before it is trusted.** A DXGI probe and its
  capture can be shown to the operator — that is diagnostics, and it does not put
  anything into the Gate 1 snapshot. Only a provider meeting this decision does.
- **The snapshot contract gains fields when a provider lands**, which is a change
  to `gate1.snapshot.v1` and is versioned then, not now.
- **Reading memory and reading the protocol are available** under ADR-0014. What
  this record still asks of them is a validity check, so a brittle source reports
  that it broke instead of returning something plausible.
