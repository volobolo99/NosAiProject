# Community Research Links

Community posts are recorded as research leads only. Forum/Reddit text is not copied wholesale unless its license/permission explicitly permits redistribution.

## Reddit — NosTale damage-counter discussion
Source: https://www.reddit.com/r/nostale/comments/1ho64jh/
Use: confirms community discussion around packet-derived telemetry/damage statistics.
NosAi relevance: telemetry concepts, combat analytics, independent evaluator.
Restriction: do not use this as authority for protocol details and do not copy third-party bypass techniques.

## elitepvpers
Search scope used: NosTale development, packet/map/bot source discussions.
Status: no sufficiently licensed/source-controlled artifact was identified during this intake that should be vendored automatically.
Rule: Claude/Cursor may use forum discussions as leads, but any code must be traced back to an original repository/license before copying.


## Extended systematic scan — 2026-09-05

### elitepvpers — How to manage Nostale map
URL: https://www.elitepvpers.com/forum/nostale/5027015-how-manage-nostale-map.html
Research value:
- describes NStuData/NOS archive structure at a conceptual level;
- little-endian file count and offsets;
- packed/unpacked blocks and zlib compression;
- map/model metadata and coordinates;
- useful as a schema lead for offline map tooling.
Status: RESEARCH LEAD.
The linked Git repository is hidden behind forum login and could not be resolved publicly during this scan. Do not copy forum code into product; prefer taletool or a traceable upstream repository.

### elitepvpers — Nostale widgets and more
URL: https://www.elitepvpers.com/forum/nostale/5026490-nostale-widgets-more.html
Research value:
- historical client-widget API concepts;
- custom UI/widget, keybinding, skill-cooldown and packet-observer ideas.
Status: QUARANTINED LEAD.
Reason: source link hidden and project described as obsolete. Do not reuse protection-bypass or injection-specific material.

### elitepvpers — Nos# Emulator presentation
URL: https://www.elitepvpers.com/forum/nostale/4404275-nos-emulator-presentation.html
Research value:
- historical architecture checklist: grid blocking, pathfinding, inventory, recipes, NPC dialog, skill system, buffs/debuffs, monsters, pets/partners, XP/drop.
Status: ARCHITECTURAL LEAD.
Use only as a completeness checklist; prefer current licensed upstreams for implementation.

### Reddit — damage formula discussion
URL: https://www.reddit.com/r/nostale/comments/1jwri6r/
Research value:
- community indicates exact modern damage formula is not reliably public/maintained.
NosAi consequence:
- do not hardcode unverified community formulas as truth;
- learn/fit combat outcome models from private-test observations and retain confidence/provenance.
Status: DOMAIN RESEARCH LEAD.

### Reddit — progression pace discussion
URL: https://www.reddit.com/r/nostale/comments/1rvm8nu/should_they_create_a_slower_server/
Research value:
- anecdotal evidence that solo progression/resource pressure can be substantial.
NosAi consequence:
- Human Pace Governor and ProgressionEngine must calibrate against actual human runs on the identical private test server, not community anecdotes.
Status: CONTEXT ONLY.
