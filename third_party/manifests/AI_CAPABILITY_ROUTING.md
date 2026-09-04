# AI Capability Routing — Cursor / Claude

## Mandatory reading order

Before designing or importing an AI capability:

1. `docs/adr/ADR-0021-unprivileged-observability-boundary.md`
2. `docs/UNPRIVILEGED_DEMO_SPEC.md`
3. `third_party/README.md`
4. `third_party/manifests/AGENT_LOOKUP.md`
5. `third_party/provenance/REUSE_INDEX.md`
6. `third_party/manifests/AI_DEEP_RESEARCH_VAULT.md`
7. `docs/research/AI_CAPABILITY_RESEARCH_2026-09-04.md`
8. `docs/research/AI_CAPABILITY_DEEP_RESEARCH_2026-09-04.md`

## Capability -> candidate references

| Capability | Candidate | Use |
|---|---|---|
| GOAP | `luxkun/ReGoap` | Planner research / adapter prototype |
| GOAP performance | `caesuric/mountain-goap` | Benchmark and API comparison |
| HTN / behavior trees | existing research + `MistreevousSharp` | Deterministic routine orchestration |
| Persistent agent memory | `joslat/agent-memory-dotnet` | Architecture reference / prototype |
| Cognitive memory lifecycle | `microsoft/Memora` | Episodic/semantic/procedural design |
| Navigation | `ikpil/DotRecast` | Navmesh/pathfinding/crowd concepts |
| LLM orchestration | `microsoft/semantic-kernel` | Non-authoritative AI orchestration |
| Screen perception | `JPDoesDev/GamingVision` | ONNX + Windows capture + OCR reference |
| CV/OCR/recovery | `KingshotAuto/Kingshot-bot` | Pattern reference only |
| Screen-only game loop | `ckazi/pilot` | Perception/action/recovery reference only |
| Observability/drift | `demml/scouter` | Monitoring/tracing/drift concepts |
| Agent workflow orchestration | `shyftlabs/continuum` | Workflow/recovery/evaluation concepts |
| Evaluation | `openai/evals` and agent-evaluation ecosystem | Repeatable offline evaluation |
| RL / self-play | `datamllab/awesome-game-ai` and referenced projects | Offline simulation/training only |

## Architecture rules

### AI is advisory

LLMs, GOAP, HTN, behavior trees, RL policies, embeddings, vector search and learned models may propose or rank candidates. None may directly invoke gameplay execution authority.

### Provenance is mandatory

Every gameplay fact entering WorldState must preserve provenance:

`Network | Memory | Screen | Local | Operator | Unknown`

An inferred or retrieved memory cannot silently become `Network` or `Memory` truth.

### Learning is offline first

Training, self-play, RL, policy optimization and experimentation happen against replay/simulation datasets first. Promotion to runtime requires deterministic validation, regression tests and safety checks.

### Recovery is first-class

Any new agent capability must define timeout, cancellation, invalid-observation handling, disconnect handling and safe-stop behavior before it is considered production-ready.

### Third-party code policy

- Search the local vault before external search.
- Preserve license headers and copyright notices.
- Record exact upstream repository, path, commit/blob SHA and license.
- Ambiguous licensing => `REVIEW_REQUIRED`.
- Never delete files under `third_party/` automatically.
- Third-party code is reference-only unless explicitly wired and tested.

## Recommended implementation sequence

1. Build `NosAi.Memory` interfaces and provenance-aware records.
2. Add replay-backed episodic memory.
3. Add hybrid retrieval behind an abstraction.
4. Add GOAP adapter behind `IPlanner`.
5. Add navigation/pathfinding adapter.
6. Add HTN/behavior-tree adapter for deterministic routines.
7. Add ONNX/OCR perception adapters behind perception interfaces.
8. Add recovery state machine.
9. Add tracing, drift monitoring and evaluation harness.
10. Add offline policy evaluation.
11. Add RL/self-play in simulation.
12. Promote only validated artifacts through the deterministic path.
