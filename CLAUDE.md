# NosAiProject — Claude Code Instructions

## Mission

You are an implementation agent for NosAiProject. Optimize for correctness, maintainability, security, testability and small reviewable changes. The repository is the source of truth; do not invent architecture that conflicts with its specifications.

## Read first

Before changing code, inspect:

1. `NOSAI_MASTER_ROADMAP.md`
2. `docs/NOSAI_ARCHITECTURE_BASELINE.md`
3. relevant `docs/adr/*.md`
4. relevant existing implementation and tests

If a task references a milestone, use that milestone's acceptance criteria and dependencies.

## Architecture invariants

- Canonical flow: `Observe → World Model → Decision/Policy → Safety → Execute → Verify → Re-observe`.
- Runtime is authoritative for safety, authorization and privileged execution.
- UI/mobile clients may request supported operations but never enforce security only on the client.
- Real and simulated data must remain explicitly distinguishable.
- Data source classification: `LIVE`, `DERIVED`, `CACHED`, `SIMULATED`, `UNKNOWN`.
- Unknown is not equivalent to zero, false or empty.
- Public APIs, protocols and durable contracts are versioned when compatibility can change.
- Fail closed where safety requires it.

## Implementation workflow

For every non-trivial task:

1. Inspect the relevant code, tests and contracts.
2. State the smallest coherent implementation plan.
3. Identify dependencies and possible regressions.
4. Implement only the requested scope.
5. Add or update tests for changed behavior.
6. Build the affected project/solution.
7. Run the relevant test suite.
8. Review the diff for accidental changes, secrets and unrelated refactors.
9. Update documentation/contracts when behavior changes.
10. Report files changed, tests/build results, risks and blockers.

## Do not

- Do not delete or weaken tests to make them pass.
- Do not silently change public APIs or network protocols.
- Do not introduce a new dependency without justification.
- Do not replace real providers with mocks in production paths.
- Do not label simulated data as live.
- Do not bypass authentication, authorization or safety gates.
- Do not perform broad refactors while implementing a focused milestone.
- Do not commit secrets, private keys, tokens, credentials or machine-specific sensitive data.
- Do not claim `VERIFIED` without the required evidence.

## Real-environment rule

A component that works only with mocks/fixtures is not real-environment verified. For client integration, networking and device flows, explicitly distinguish local tests from real target validation.

## Error handling

Prefer typed/structured results and explicit failure states. Preserve diagnostic context. Handle cancellation and timeouts deliberately. Avoid broad exception swallowing.

## Testing expectations

Critical logic requires unit tests. Cross-component behavior requires integration tests. Network/security boundaries require contract and negative tests. Real-client features require real-environment validation before being marked `VERIFIED`.

## Git discipline

Use small commits with imperative messages. One coherent purpose per commit. Never rewrite unrelated history. If a change conflicts with an ADR or architecture baseline, stop and report the conflict rather than silently overriding it.

## Completion report

At the end of each task report:

- milestone/task ID;
- files created/modified;
- implementation summary;
- build command and result;
- test command and result;
- verification level (`Present`, `Integrated`, `Done`, `Verified`);
- remaining risks/blockers.
