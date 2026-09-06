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
- Licensing decision made explicitly by the repository owner: vendor the
  built Windows binary, run it as a subprocess.
- Vendored binary: `third_party/taletool/taletool.exe` (+ LICENSE, NOTICE.md,
  VERSION recording the exact upstream commit and build toolchain).
- Invoked from `src/NosAi.Runtime/GameData/TaletoolInvoker.cs` as a
  separate OS process; no AGPL code is copied, linked, or compiled into
  NosAi.Core or NosAi.Runtime.
- Output boundary as originally planned: taletool `scan --json` ->
  parsed inventory -> `GameReferenceDatabase` (`client_inventory` table)
  for update detection alongside the existing `ReferenceImporter` tables.
- Not yet wired: quest.dat/qstprize.dat/npctalk.dat/tutorial.dat parsing
  and map/geometry extraction. taletool's `scan`/`archive`/`text`
  commands can reach these; a NosAi-native decoder for their content
  (the same "typed columns only where confidently resolved" discipline
  already applied to Skill.dat) is separate follow-up work.

Academic use:
Useful for building a version-pinned NosTale knowledge base without giving the live agent privileged server state.
