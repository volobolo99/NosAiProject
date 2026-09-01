# ADR-0019 — Operating system input is the actuation channel for character control

## Status

Proposed, 1 Sep 2026. Scopes a choice that
[ADR-0014](ADR-0014-operator-chooses-the-data-path.md) deliberately left open.
Changes no classification rule and revokes no permission ADR-0014 granted.

**Builds on:** ADR-0014 (the operator chooses the data path),
[ADR-0003](ADR-0003-runtime-safety-authority.md) (the runtime is authoritative
for execution).

## Context

ADR-0014 lifted the prohibitions of ADR-0012 and ADR-0013 and listed three data
paths as available engineering options: capturing the traffic, reading the
client's process memory, and **driving the client through input or through its
own protocol**. It chose none of them; it recorded that the choice belongs to the
operator and that each option is still bound by Safety and by honest
classification.

Character control is the first piece of work that has to actually pick one. A
character that moves is a character that acts, and the act needs a channel.

Two documents already answer this question differently, which is why the record
is needed now rather than after the first `InputDriver` line is written.

- The runtime already actuates through `SendInput` (`Win32InputBackend`), behind
  `GatedInputBackend`, which is the boundary ADR-0003 requires. That is a choice
  made in code and never written down.
- The project note `SPEC_GAMEPLAY_DATASET.md` § 7 still says *"nessuna scrittura
  sul filo, nessuna injection nel processo del client … restano fuori dal
  progetto come da ROADMAP.md (`EXTERNAL_IMPLEMENTATION_REQUIRED`)"*. That
  sentence predates ADR-0014 and contradicts it: ADR-0014 removed exactly those
  prohibitions. Left standing it is the documentary disagreement that
  `docs/SOURCE_OF_TRUTH.md` exists to prevent.

So the question is not *may we*. ADR-0014 answered that. The question is *which
channel does character control use, and why that one*.

## Options considered

### Drive the client through its own protocol

Send the movement, targeting and skill packets the client would send. It is the
most precise channel available: no projection to calibrate, no pixel to miss, no
window to keep in the foreground, and the act is expressible exactly.

Two objections, neither about bans.

The first is that the outbound direction is encrypted and, as
[ADR-0017](ADR-0017-training-the-screen-reader-from-the-wire.md) records while
explaining why the player's own position is not on the wire, that direction has
not been read. Building actuation on a channel we cannot yet read means the
verifier and the actuator would be commissioned together, with nothing
independent to check either against.

The second is the one that decides it. An act sent on the wire bypasses the
client entirely, so the client's own refusals — *that square is not walkable*,
*that skill is on cooldown*, *that target is out of range* — stop being a
constraint the runtime works within and start being a constraint the runtime has
to reimplement correctly, from a reverse-engineered model, with the server as the
only thing that says no. Every modelling error becomes an act.

**Not adopted for actuation.** Reading that direction remains valuable and
remains permitted by ADR-0014.

### Inject into the client process and call its own functions

Precise for the same reasons and worse for one more: it makes the runtime depend
on undocumented internals of a program that updates itself, and a wrong hook is
not a failed action but an unpredictable one. ADR-0014 permits it. Nothing in
character control needs it.

**Not adopted.**

### Emit operating-system input to the session window

Synthetic mouse and keyboard events, injected into the system input stream. The
client cannot distinguish them from a device at the API level, and — this is the
point — it processes them through its own code. Every refusal the client already
implements stays in force for free. The runtime asks; the client decides whether
the ask was legal.

The costs are real and are accepted: the projection from map square to pixel must
be measured (it already is — `ScreenProjectionCalibration` fits
`screen = A·Δmap + anchor` from samples the client itself produces), the window
must be in the foreground and unobstructed at the moment of the act, and the
operator's own hand is on the same mouse.

**Adopted.**

## Decision

**Character control actuates through operating-system input to the session
window, and through no other channel.** The wire and process memory remain
observation sources, which is what ADR-0014 made them and what they are already
used for.

The choice is scoped to actuation. It does not reinstate any prohibition:

- Reading the outbound traffic stays permitted and stays wanted.
- Reading process memory stays permitted and is in use — the auto-calibrator
  reads the client's resolved walk target that way.
- **Detection evasion stays out**, as ADR-0014 left it, for ADR-0014's reason: it
  is work aimed at defeating someone else's security control, and nothing here
  depends on it.

A later ADR may adopt the protocol channel. It would have to argue the two
objections above, not the ban risk, and it would have to say what independently
verifies an act the client never evaluated.

## Consequences

- The safety work for character control is **window work**, not protocol work:
  the act is only as confined as the guarantee that the intended window received
  it. `SendInput` does not address a window — it goes to whatever holds focus —
  so confinement is a property the pipeline builds, not one the API supplies.
  This is what makes the commit-point revalidation
  (`docs/CONTROLLO_PERSONAGGIO_ATTUAZIONE.md` § 3.3) load-bearing rather than
  defensive decoration.
- **Posting messages to the window is not an alternative implementation of this
  channel and is not permitted.** Posted messages do not enter the input queue,
  do not run the keyboard hook and do not update key state, so a client reading
  key state sees nothing while a client reading messages sees an act. One channel
  or the other is a correctness question, not a preference.
- The operator's hand and the runtime's hand share one mouse. Human input takes
  precedence, which is a requirement this decision creates and which did not
  exist before it.
- `SPEC_GAMEPLAY_DATASET.md` § 7 is corrected to match ADR-0014 and this record.
- `docs/SOURCE_OF_TRUTH.md` lists this ADR.
