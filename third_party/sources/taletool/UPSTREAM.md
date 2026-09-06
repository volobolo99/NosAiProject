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
- Evaluated and briefly integrated as a vendored subprocess (built
  Windows binary, run externally), then explicitly replaced by the
  repository owner with a native equivalent: no AGPL binary is vendored
  or run by NosAiProject.
- What replaced it: `src/NosAi.Runtime/GameData/ClientDirectoryScanner.cs`
  classifies every file under a client data directory using this
  project's own `NosArchive` reader (already established against the
  real client for `ReferenceImporter`), feeding the same
  `GameReferenceDatabase` `client_inventory` table and `--client-updates`
  command the taletool-based version fed.
- Not yet reached by the native scanner either: quest.dat/qstprize.dat/
  npctalk.dat/tutorial.dat semantic parsing and map/geometry extraction.
  taletool's own `scan`/`archive`/`text` commands could reach these, but
  a NosAi-native decoder for their content (the same "typed columns only
  where confidently resolved" discipline already applied to Skill.dat)
  is separate follow-up work, independent of this decision.

Academic use:
Useful for building a version-pinned NosTale knowledge base without giving the live agent privileged server state.
