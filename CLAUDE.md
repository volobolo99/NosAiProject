# NosAiProject — Claude Code Instructions

## Mission

You are an implementation agent for NosAiProject. Build a genuinely autonomous player for the private educational/test environment. Optimize for correctness, maintainability, security, testability and small reviewable changes.

## Read first

Before changing code, inspect:

1. `docs/ROADMAP_ESECUTIVA.md`
2. `docs/NOSAI_AUTONOMOUS_PLAYER_SPEC.md`
3. `docs/NOSAI_ARCHITECTURE_BASELINE.md`
4. relevant `docs/adr/*.md`
5. relevant existing implementation and tests

`NOSAI_MASTER_ROADMAP.md` is superseded and must not be treated as canonical.

## Product boundary

NosAi may use PC CPU/GPU/NPU/RAM/storage, normal Windows APIs, client-visible network traffic, legitimately readable local client memory, screen/pixel capture, OCR/CV, local telemetry and software client-control mechanisms. Mouse and keyboard are permitted but optional. No external automation hardware is required or permitted beyond those devices.

Never use server DBs, GM/mod/admin tools, server consoles, privileged APIs, hidden/debug state, secret credentials or any channel unavailable to an ordinary client/player. Do not modify the server to expose hidden gameplay state.

## Architecture invariants

- Canonical flow: `Observe → Sensor Fusion → World Model → Simulation/Prediction → Ranking/Utility → Strategic Orchestrator → HTN/GOAP → Guard → Trust/Authorization → Safety → Execute → Verify → Re-observe`.
- The runtime is authoritative for authorization and safety.
- No LLM, ML model, heuristic or stochastic component has direct execution authority.
- Unknown is not zero, false or empty.
- Every important gameplay fact carries provenance, confidence and freshness.
- Real, derived, cached and simulated data remain explicitly distinguishable.
- Fail closed where safety requires it.
- Public contracts and protocols are versioned when compatibility can change.

## Implementation workflow

For every non-trivial task:

1. Inspect relevant code, tests, contracts and the canonical roadmap.
2. State the smallest coherent implementation plan.
3. Identify dependencies, performance bottlenecks and regressions.
4. Implement only the requested scope.
5. Add/update unit and integration tests.
6. Build affected projects.
7. Run relevant tests and benchmarks.
8. Review diff for accidental changes, secrets and boundary violations.
9. Update canonical documentation when behavior changes.
10. Report files, build/tests, verification level, risks and blockers.

## Autonomy requirements

The long-term target is a player that can autonomously perceive maps, estimate dimensions, explore, navigate, recognize entities, fight adaptively, understand multi-step quests, manage inventory/equipment/progression, learn from failures and recover from disturbances.

Do not hardcode a static macro where a model/planner is required. Prefer hierarchical planning: strategic goals → HTN/GOAP → reactive recovery. Use simulation/prediction as advisory evidence only.

## Do not

- Do not delete or weaken tests to make them pass.
- Do not silently change public APIs or protocols.
- Do not introduce dependencies without justification.
- Do not replace real providers with mocks on production/critical paths.
- Do not label simulated data as live.
- Do not bypass authentication, authorization or Safety.
- Do not claim `Verified` without evidence.
- Do not broad-refactor unrelated code.
- Do not commit secrets, credentials or machine-specific sensitive data.
- Do not delete files in `third_party/`; preserve GPL/LGPL/MIT/Apache/ZLib provenance and license notices.

## Real-environment rule

Mocks/fixtures can support isolated tests but never establish real-environment verification. Client integration, perception and actuation require real target validation before `Verified`.

## Testing expectations

Critical logic requires unit tests. Cross-component behavior requires integration tests. Network/security boundaries require contract and negative tests. Autonomous-player milestones require replay/evidence traces and real-client validation where applicable.

## Git discipline

Use small commits with imperative messages and one coherent purpose. Never rewrite unrelated history. If an implementation conflicts with an ADR or canonical specification, stop and report the conflict.

## Completion report

Report:

- milestone/task ID;
- files created/modified/deleted;
- implementation summary;
- build/test/benchmark commands and results;
- verification level (`Present`, `Integrated`, `Done`, `Verified`);
- remaining risks/blockers.
