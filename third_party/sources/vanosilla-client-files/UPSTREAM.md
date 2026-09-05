# Vanosilla/client-files — Upstream Reference

Repository: https://github.com/Vanosilla/client-files
Status: RESEARCH / REFERENCE ONLY
License: NOT CLEARLY IDENTIFIED IN REPOSITORY — DO NOT COPY SOURCE/DATA INTO PRODUCT UNTIL VERIFIED

Useful data categories reported by upstream:
- act_desc
- BCard
- Card
- Item
- monster
- npctalk
- quest
- qstprize
- qstnpc
- shoptype
- Skill
- tutorial
- maps (width, height, cell/grid flags)
- language/name/description data

NosAi integration targets:
- QuestGraph / ProgressionEngine
- ItemCatalog / EquipmentOptimizer
- MonsterCatalog / CombatModel
- SkillCatalog / TacticalCombatController
- NpcCatalog / Dialogue/Quest reasoning
- Spatial/Map Model

Rule:
Use this repository to understand schemas and cross-check private-test-server data. Do not treat its data as authoritative for the current server build; pin and validate every imported dataset against the exact exam environment.
