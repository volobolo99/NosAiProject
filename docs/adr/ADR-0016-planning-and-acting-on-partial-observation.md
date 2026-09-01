# ADR-0016 — What Gate 3 may plan on, and what it may act on

## Status

Accepted, 1 Sep 2026. Refines [ADR-0003](ADR-0003-runtime-safety-authority.md)
(the runtime is the safety authority) and
[ADR-0012](ADR-0012-gameplay-observation-source.md) (classification and staging).
It changes the meaning of `Gate3WorldState.IsPlannable` and the condition under
which a cycle may reach a real effector.

## Context

The network observation path now works. A live capture of the world channel
publishes the player's HP, max HP and MP as `LIVE`, checked against the client's
own HUD (`docs/PROTOCOLLO_NOSTALE.md`). Gate 3 still cannot plan a single cycle
on it, and would refuse to act even if it could. Two separate rules cause that,
and both were written before anything real could reach them.

### The first rule refuses on fields nothing consumes

`IsPlannable` requires all five fields — HP, max HP, MP, `HasTarget`,
`InCombat` — to carry a value. The wire establishes the first three and says
nothing about the last two: no packet in either capture carries the player's
targeting or combat state in a form anybody has established, and ADR-0012's own
rule is that an unestablished field is not read.

So the loop returns `NoWorldState` forever. Worse, one of the two fields it
refuses over is **read by nothing**: `ActionPlanner.PlanCandidates` takes
`isInCombat` as a parameter and never mentions it again in the body. Gate 3 was
refusing to plan on the absence of a fact that would have changed no decision.

The consequence is not caution, it is inertia. With HP at 200/5000 and the
targeting state unknown, everything needed to decide "drink a potion" has been
observed and checked, and the runtime does nothing at all. ADR-0012 staged HP and
MP first precisely because they are "the most safety-relevant"; the intent was
that they alone would enable the decisions that matter most.

### The second rule calls a one-second-old reading a simulation

`IsFullyObserved` requires all five fields to be `LIVE`, and the cycle refuses
anything else with `RefusedSimulatedInput` and the message *"Stato simulato con
effector reale collegato"*.

That was accurate when `LIVE` and `SIMULATED` were the only two things that ever
occurred. It is not accurate now. `stat` is sent when the number changes, not on
a schedule — 62 packets in 90 s of combat — so between two of them the provider
republishes the last reading as `CACHED` with the time it was really observed,
which ADR-0012 explicitly sanctions. That state is not simulated. Refusing it is
defensible; *calling it simulated* is a false statement in an operator-facing
message, and it hides the one thing the operator needs to know: how old the
reading is.

There is also a third case the rule cannot express. A reading through an
operator-reconstructed `ProtocolMap` is `DERIVED`. Under an all-`LIVE` rule it
can never act — which would make the source ADR-0012 actually adopted, screen
perception, permanently unable to drive anything.

## Options considered

### Default the unknown flags to false

`HasTarget = false` yields a plannable state immediately. It also sends the
character walking to a waypoint, because that is exactly what the planner does
when it believes there is no target. An invented `false` is not a neutral value;
it is a decision, taken by nobody, on evidence that does not exist. **Rejected**
for the same reason ADR-0012 rejected a manufactured max HP.

### Keep refusing until every field is established

Honest, and it is what happens today. But it makes the runtime's usefulness
depend on fields nobody has established, one of which nothing reads, and it means
a fully observed critical HP produces no action. It also cannot be fixed by more
observation: the wire does not carry these, and ADR-0012's confirming source
(screen or memory) is a separate piece of work. **Rejected** as inertia dressed
as caution.

### Plan per fact, act on freshness

Adopted. Two changes, each narrow.

## Decision

### 1. A rule is skipped when its own facts are unknown; the cycle is not

**Planning requires the vitals — HP, max HP, MP — and nothing else.** Every other
fact gates only the rules that read it:

- HP and max HP known → the survival rules (heal, defensive reposition) plan.
- `HasTarget` known and true → the attack rules plan; the skill rule additionally
  requires MP.
- `HasTarget` known and false → the exploration rule plans.
- **`HasTarget` unknown → neither.** No attack, and no waypoint move. The absence
  of a fact never selects a branch.
- `InCombat` is not a precondition for anything, because no rule reads it. It
  stays in the state — other consumers may come to need it — and it stops
  blocking the loop.

This is the pattern the repository already uses on the decision-engine side:
`NetworkWorldFeed.ToDecisionContext` records an unobserved fact as `UNKNOWN`
rather than omitting it, precisely so "a rule that needs an unobserved fact must
be skipped, and it can only be skipped if the fact is present as unknown". Gate 3
now does the same thing.

When no rule's facts are satisfied, the outcome is `NoCandidate` — nothing to do —
which is different from `NoWorldState` and reads differently to an operator.

**What this does not change:** planning still refuses outright when the vitals
themselves are unknown. There is no reasoning to be done about a character whose
HP nobody has read.

### 2. Acting requires an observation that is real and recent

Three tiers replace the single all-`LIVE` test:

| State | May plan | May act |
|---|---|---|
| `UNKNOWN` in any required field | no | no |
| `SIMULATED` in any field | yes | **never** |
| `LIVE` / `DERIVED` / `CACHED`, within the freshness bound | yes | yes |
| `LIVE` / `DERIVED` / `CACHED`, older than the bound | yes | no |

- **Simulated input may never reach a real effector.** Unchanged, and it keeps
  its own outcome and message: it is a different failure from staleness and must
  not be merged with it.
- **Age is measured per field**, from the `ObservedAtUtc` each classified value
  already carries, against a bound the orchestrator holds. A state is as old as
  its oldest field: a current MP does not make a stale HP current.
- The default bound is **2 seconds**, deliberately stricter than the provider's
  own retention. That leaves a band — observed between 2 and 5 seconds ago — where
  the runtime will reason about the state and refuse to act on it, which is the
  distinction the old rule could not express at all.
- A refusal for staleness says so, and says how old the reading is. An operator
  who sees "simulated" when the truth is "1.4 s old" cannot diagnose anything.

`IsFullyObserved` keeps its meaning — every field `LIVE` — as the strictest tier
available to a caller who wants it. It is no longer what gates the effector.

### 3. `DERIVED` may act

It follows from the table above and deserves stating plainly, because it is the
part that loosens. A reading through a reconstructed protocol map, or off the
screen, may drive an action if it is fresh.

The protection is not the label, it is the checks the reading already had to pass
to exist: range, internal arithmetic, and a decoder that yields nothing rather
than a plausible number when its framing fails. ADR-0012 required those precisely
so that a `DERIVED` value could be trusted enough to be useful — a classification
nothing may ever act on would have made the source that ADR adopted pointless.

## Consequences

- **Gate 3 becomes usable on the network path.** With HP, max HP and MP observed
  and `HasTarget` unknown, the survival rules plan and the attack and exploration
  rules do not. That is the first real decision this project can take on observed
  game state.
- **`HasTarget` is now the field worth establishing**, not `InCombat`. It is what
  separates "can only react to my own health" from "can fight". The wire does not
  carry it; ADR-0012's confirming source is where it will come from.
- **One existing test changes meaning.** A partially read observation used to
  assert `IsPlannable == false`; it now asserts that the state is plannable *and*
  that no candidate depending on the unknown fact is ever produced. The property
  being protected — an unknown fact must not select a branch — is unchanged and
  is now tested directly rather than through a blanket refusal.
- **A stale reading is refused with an accurate reason.** `RefusedStaleInput`
  joins `RefusedSimulatedInput`; merging them would have gone on describing a
  real observation as a fiction.
- **The freshness bound is a policy, not a constant.** It belongs to the
  orchestrator, so an operator running a slower or faster channel sets it once,
  and a test can make staleness deterministic instead of racing a clock.
