# NOS AI — Agent Runtime Platform

**Architecture Expansion:** v1.0 Beta  
**Version:** 1.0 Beta (locked)  
**Creator:** Volodymyr Ryzhuk

## Purpose

This expansion adds a model-agnostic, local-first runtime layer over the existing deterministic NosAi pipeline without granting execution privileges to stochastic Decision Providers.

## Cross-cutting runtime systems

- **SessionManager** — lifecycle and resumable in-process session state.
- **MemoryBus** — bounded event/evidence bus decoupled from persistence.
- **ProviderRegistry / ProviderRouter** — model-agnostic provider discovery and deterministic local-first selection.
- **RoutingPolicy** — privacy, locality, complexity, latency, VRAM and temperature constraints.
- **ResourceManager** — runtime resource snapshots and local execution checks.
- **ExecutionPolicy** — explicit execution mode and trust-tier policy.
- **AgentRuntime** — decision facade that routes a provider result through Guard AI and Safety Gate.

## Security boundary

Decision Providers implement `DecisionProvider` and return a `Decision`. They do not receive an `ActionExecutor`, Safety Gate bypass, game adapter, or privileged tool handle. Provider output is data until downstream validation succeeds.

The runtime path is:

```text
Session
  ↓
Provider Router
  ↓
Decision Provider
  ↓
Guard AI
  ↓
Safety Gate
  ↓
Execution Adapter (future / downstream only)
  ↓
Verification + Telemetry
```

## Local-first policy

`PrivacyClass.LOCAL_ONLY` and `PrivacyClass.SENSITIVE` default to local execution. A cloud provider is rejected when `requires_local=True`; sensitive cloud routing is disabled by default.

The router is intentionally provider-agnostic. A future llama.cpp provider, cloud provider, or another runtime can be registered without changing the decision core.

## Resource awareness

The current `ResourceManager` is a deterministic abstraction around a resource snapshot. OS/GPU-specific probes and benchmark collection remain separate implementation work so tests remain runnable without game or hardware dependencies.

## Session and memory

Sessions support checkpoint, stop and resume transitions. `MemoryBus` records bounded decision/verification events. Durable SQLite persistence remains a later gate and is not falsely reported as implemented.

## Trust and execution

Trust-tier enforcement remains owned by Guard AI and the Safety Gate. `ExecutionPolicy` provides runtime policy primitives, but does not replace either safety boundary.

## Non-goals in this increment

- No live game execution.
- No cloud credentials or network calls.
- No production GPU probing.
- No direct LLM integration.
- No persistent SQLite implementation.
- No version increment.

These boundaries preserve the 1.0 Beta reliability-first architecture.
