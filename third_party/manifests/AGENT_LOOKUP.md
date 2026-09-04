# Agent Lookup Map — Cursor / Claude

Usare questo file come indice prima di eseguire ricerche esterne.

| Necessità | Prima posizione locale | Sorgente di riferimento |
|---|---|---|
| Packet/protocol concepts | `third_party/sources/opennos/` | OpenNos |
| Modern C#/.NET architecture | `third_party/sources/noscore/` | NosCore |
| Plugin/event/entity patterns | `third_party/sources/chickenapi/` | ChickenAPI |
| Event-driven/distributed patterns | `third_party/sources/saltyemu/` | SaltyEmu |
| Resource/packet/TimeSpace tooling | `third_party/sources/nosgm/` | NosGm |
| RAG/vector/hybrid retrieval | `third_party/sources/llm-rag-architecture/` | LLM-RAG-Architecture |
| Provenance/license | `third_party/provenance/` + `third_party/licenses/` | all |

## Routing rules

- `RealClientConnector`, process attach, memory: consult `third_party/sources/opennos/` and `third_party/sources/noscore/` only for concepts; product implementation stays in `src/NosAi.Runtime`.
- Packet parsing/catalog: consult OpenNos/NosGm reference material; do not import privileged server state.
- Resource parsing: consult NosGm reference material; keep tooling isolated from runtime.
- Domain/entity/event architecture: consult ChickenAPI/NosCore/SaltyEmu.
- RAG/memory: consult LLM-RAG-Architecture; enforce `Network|Memory|Screen|Local|Operator|Unknown` provenance.
- Any proposed action/execution path: first inspect `docs/adr/ADR-0021-unprivileged-observability-boundary.md` and `docs/UNPRIVILEGED_DEMO_SPEC.md`.

## Important

If a required file is not present in `third_party/sources/`, the agent may search the upstream repository. When it does, it must add the useful source reference to the appropriate manifest instead of repeatedly rediscovering it.
