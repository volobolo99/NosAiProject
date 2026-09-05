# AP-00 / A1 — Claude — Hardware Contracts

## MODE
Implementation agent. Work only on the files named below.

## GOAL
Complete the hardware/runtime capability contracts used by the profiler and inference-budget policy.

## READ
`docs/ROADMAP_ESECUTIVA.md`; `docs/HARDWARE_PROFILE_ASUS_NITRO_V16.md`; `nosai/runtime/hardware.py`; existing hardware tests.

## OWNED FILES
Only hardware capability contract/model files and their direct unit tests explicitly required by the inspected implementation. Do not edit Control Panel, Gate3, CI or other agents' files.

## REQUIREMENTS
Preserve runtime SKU detection. Model CPU/GPU/NPU/RAM/VRAM/thermal/storage capabilities, inference tiers and bounded budgets. Unknown hardware must remain UNKNOWN. No hardcoded machine identity.

## DELIVERY
Write complete files. Run targeted tests and static validation. No TODO/FIXME/stubs. Handoff exact files and evidence to A6.
