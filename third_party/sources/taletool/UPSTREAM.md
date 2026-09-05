# imxeno/taletool — Upstream Reference

Repository: https://github.com/imxeno/taletool
License: GNU AGPL-3.0
Status: ISOLATED TOOL / REFERENCE
Purpose: NosTale client-data inspection, unpacking/packing and conversion.

Useful areas:
- client archive inspection/unpacking
- Item.dat / monster.dat / Skill.dat / quest.dat / qstprize.dat / npctalk.dat / tutorial.dat parsing
- map/geometry/height-grid/cell-flag extraction
- machine-readable JSON exports

NosAi integration:
- Keep as an external/offline research tool.
- Do NOT copy AGPL implementation into NosAi.Core or NosAi.Runtime without an explicit licensing decision.
- Preferred output boundary: taletool -> JSON/artifacts -> NosAi importers.
- Candidate destinations for NosAi-native importers:
  - src/NosAi.Core/Knowledge/
  - src/NosAi.Runtime/Knowledge/
  - tools/

Academic use:
Useful for building a version-pinned NosTale knowledge base without giving the live agent privileged server state.
