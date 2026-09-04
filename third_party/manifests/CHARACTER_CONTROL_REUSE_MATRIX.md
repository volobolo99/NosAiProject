# Character Control / Game Functions / Statistics / Prediction — Reuse Matrix

## Product rule
NosAi product code remains independent from third-party sources. Third-party material is kept in `third_party/` for audit/reference or permissive reuse only after license review.

## Selected sources

| Capability | Source | License | NosAi use | Status |
|---|---|---|---|---|
| NosTale packet/domain reference | NosCoreIO/NosCore | MIT | protocol/domain reference; adapter input | REFERENCE / ADOPT AFTER REVIEW |
| Legacy packet catalog | OpenNos/OpenNos | GPL-2.0 | protocol comparison only | REFERENCE |
| Event/plugin patterns | ChickenAPI | verify upstream license before reuse | architecture reference | REFERENCE |
| Event-driven server patterns | SaltyEmu | GPL-3.0 | architecture reference only | REFERENCE |
| Resource/packet catalog | KILL009/NosGm | verify upstream license | offline reference only | REFERENCE |
| Windows input | bettercallsean/ClickyController | MIT | optional input adapter reference | CANDIDATE |
| Windows input | michaelnoonan/inputsimulator | MIT | optional input adapter reference | CANDIDATE |
| GOAP | caesuric/mountain-goap | MIT | planning reference; prefer NosAi-native deterministic planner | CANDIDATE |
| Navigation | ikpil/DotRecast | Zlib | navigation/pathfinding adapter | CANDIDATE |
| Statistics | Math.NET Numerics | MIT/X11 | statistical calculations | CANDIDATE |
| Resilience | Polly | BSD-family | recovery infrastructure | CANDIDATE |
| Agent memory | joslat/agent-memory-dotnet | MIT | advanced memory reference | CANDIDATE |
| Memory lifecycle | microsoft/Memora | MIT | memory lifecycle reference | CANDIDATE |
| Vision | JPDoesDev/GamingVision | MIT | screen perception reference | CANDIDATE |
| World-model/RL | DreamerV3 implementations | varies | offline simulation/research only | ISOLATED |

## Integration boundary

The live gameplay path may use only ordinary-client-observable information and ordinary client-side actions. No server database, GM/mod/admin API, server console, hidden state, privileged credential, or developer-only gameplay control may become a source of truth or action authority.

Pipeline:

`Observe -> WorldState -> Statistics/Prediction -> Goal/Planner -> Candidate Action -> Guard -> Safety Gate -> Character Controller -> Client -> Observe/Verify`

Unknown or stale gameplay state is never promoted to authoritative truth.

## Functional domains

- Movement: move, stop, position, direction.
- Combat: target, basic attack, skill, combat result.
- Character: HP, MP, SP, buffs, debuffs, equipment.
- Interaction: NPC/entity interaction, pickup, map transition.
- Inventory: observed item use and state changes.
- Statistics: damage, attack speed, critical, resistance, accuracy, resources, cooldowns, equipment-derived observations.
- Prediction: trend estimates, expected damage/survival/resource trajectory, action success probability; predictions remain advisory and cannot bypass Guard/Safety.

## Reuse policy

1. Preserve all existing GPL/LGPL sources; never auto-delete third-party files.
2. Preserve upstream notices and exact commit/blob provenance for copied source.
3. Prefer permissive-source implementations or clean-room NosAi-native code for product modules.
4. Every adopted source requires tests, dependency review, provenance, and ADR-0021 compatibility review.
5. RL/world-model code stays offline until a separately approved deterministic integration boundary exists.
