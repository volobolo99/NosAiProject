# NosAi Clean Architecture

## Purpose

`NosAiProject` is the clean development repository. The legacy `NosAi` repository remains a source/reference repository. Code is migrated selectively only after review; history is not copied wholesale.

## Runtime flow

```text
Observation/WorldState
        |
        v
Decision Provider
  |             |
  | online      | failure
  v             v
Local LLM    Rule-Based
      \        /
       v      v
      Safety Gate
          |
          v
      Runtime Action Boundary
          |
          v
        Telemetry
```

The Local LLM is decision-only. It cannot directly execute game input. Every proposed decision crosses the safety boundary before an execution adapter is introduced.

## Build order

1. Core contracts and deterministic tests.
2. Provider interface + rule fallback.
3. Local LLM adapter and real-server integration test.
4. Memory/telemetry contracts.
5. Observation-only Windows client adapter.
6. Vision/PSG/skill projection.
7. Action adapter behind safety gates.
8. Dashboard and AutoSet/benchmark integration.
9. Full live-client validation.

## Migration rule

Useful modules from `volobolo99/NosAi` are treated as source material. Before importing a module, verify its dependencies, contracts, tests, and compatibility with this architecture. Do not import legacy structure merely to preserve file count.
