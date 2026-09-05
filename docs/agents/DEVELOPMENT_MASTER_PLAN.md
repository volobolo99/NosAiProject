# NosAiProject — Multi-Agent Development Master Plan v1.0

## Objective
Run Claude Code and Cursor as coordinated multi-agent teams. Each phase uses five parallel implementation agents and one integration agent. The user launches the command file explicitly; agents never guess their scope.

## Phase order
AP-00 Hardware/Runtime → AP-01 World Model → AP-02 Multimodal Perception → AP-03 Map Reconstruction → AP-04 Exploration/Navigation → AP-05 Combat → AP-06 Quest Intelligence → AP-07 Character/Inventory/Equipment → AP-08 Strategic Autonomy → AP-09 Memory/Learning/Simulation → AP-10 Autonomous Certification.

## Parallel topology
A1 contracts/domain; A2 perception/data; A3 planning/algorithms; A4 runtime/integration; A5 tests/benchmarks/docs; A6 integration/release gate.

## Synchronization
A1–A5 use disjoint write sets. A6 starts only after all five handoffs. No phase overlaps another. Never let two agents edit one file. If a cross-agent API is needed, A6 owns the integration change.

## Complete-file policy
Agents must write complete source files, complete tests and complete documentation. No snippets, TODOs, placeholders, pseudocode, stubs or knowingly broken intermediate state may be committed.

## Execution recommendation
Use Claude for architecture/contracts/reasoning-heavy tasks and Cursor for repository-scale implementation/perception/runtime tasks, but either can execute any assigned role. Prefer six agents for AP-01–AP-10; use four only when resources are constrained.

## Gate
`Present → Integrated → Done → Verified`. Real-client/hardware evidence is required for `Verified` where applicable.

## Safety boundary
No privileged server data, GM/admin/debug state, server modifications to expose hidden data, credentials or detection-evasion. Mouse/keyboard remain optional. Safety/Guard is authoritative.
