# NosAiProject — Systematic Third-Party Research 2026-09-05

Status: ACTIVE RESEARCH CATALOG
Scope: perception, OCR, tracking, world models, imitation/offline RL, planning, Windows capture, inference, memory and optimization.

## Selection principles

1. Prefer mature upstream repositories with clear licenses.
2. Vendor only small, high-value source subsets; consume large frameworks as packages/tools.
3. Keep third-party code outside the production build until explicitly adapted and tested.
4. Preserve Guard -> Trust -> Safety -> Executor -> Verifier.
5. No anti-cheat bypass/circumvention code enters the autonomous runtime.
6. Server-side ground truth belongs only to AcademicEvaluator and never feeds Agent Plane.

## High-priority sources

| Area | Upstream | License | Mode | Priority | NosAi target |
|---|---|---|---|---|---|
| OCR | PaddlePaddle/PaddleOCR | Apache-2.0 | PACKAGE/TOOL | VERY HIGH | Perception/OCR |
| Detection | open-mmlab/mmdetection | Apache-2.0 | TRAINING TOOL | HIGH | Perception/Detector |
| Tracking | FoundationVision/ByteTrack | MIT | REFERENCE/ADAPT | VERY HIGH | Perception/Tracking |
| Segmentation | facebookresearch/sam2 | Apache-2.0 | OFFLINE TOOL | MEDIUM-HIGH | Dataset/annotation |
| World model | danijar/dreamerv3 | MIT | RESEARCH TOOL | HIGH | WorldModel sandbox |
| Imitation | HumanCompatibleAI/imitation | MIT | TRAINING TOOL | VERY HIGH | Skill policy learning |
| Offline RL | takuseno/d3rlpy | MIT | TRAINING TOOL | HIGH | Replay/offline learning |
| Sequence RL | kzl/decision-transformer | MIT | RESEARCH | MEDIUM | Trajectory modeling |
| RL baseline | DLR-RM/stable-baselines3 | MIT | RESEARCH TOOL | HIGH | Sandbox benchmarks |
| RL baseline | vwxyzjn/cleanrl | MIT | REFERENCE | HIGH | Academic reproducibility |
| GOAP | luxkun/ReGoap | Apache-2.0 | LOCAL SOURCE | VERY HIGH | Planning/GOAP |
| HTN | ptrefall/fluid-hierarchical-task-network | MIT | LOCAL SOURCE | VERY HIGH | Planning/HTN |
| Capture | robmikh/Win32CaptureSample | MIT | LOCAL SOURCE | VERY HIGH | Perception/Capture |
| Inference | microsoft/onnxruntime | MIT | PACKAGE | VERY HIGH | AI/Perception Runtime |
| Local LLM | ggml-org/llama.cpp | MIT | EXTERNAL RUNTIME | HIGH | Provider Router Tier 3 |
| RAG/.NET | microsoft/semantic-kernel | MIT | REFERENCE/PACKAGE | MEDIUM-HIGH | Memory/Knowledge |
| Vector SQLite | asg017/sqlite-vec | MIT/Apache-2.0 | EXTENSION/PACKAGE | HIGH | Memory/Knowledge |
| Optimization | google/or-tools | Apache-2.0 | PACKAGE | VERY HIGH | Build/Inventory/Progression |

## Materialized source subsets

### ReGoap
Local:
- third_party/sources/luxkun/ReGoap/reference/IReGoapAction.cs
- third_party/sources/luxkun/ReGoap/reference/IReGoapAgent.cs
- third_party/sources/luxkun/ReGoap/reference/IReGoapGoal.cs
- third_party/sources/luxkun/ReGoap/reference/IReGoapMemory.cs
- third_party/sources/luxkun/ReGoap/reference/IReGoapSensor.cs
- third_party/sources/luxkun/ReGoap/reference/ReGoapCondition.cs
- third_party/sources/luxkun/ReGoap/reference/ReGoapPlanner.cs
- third_party/sources/luxkun/ReGoap/reference/ReGoapNode.cs
- third_party/sources/luxkun/ReGoap/reference/ReGoapPlannerSettings.cs
- third_party/sources/luxkun/ReGoap/reference/LICENSE

Pinned upstream commit:
69eeea4a5489506b2e0d3f2db4a02c288d8d38fa

Use:
cleanly adapt planner contracts/algorithm into NosAi.Core Planning. Keep deterministic behavior and do not couple third-party planner directly to execution.

### FluidHTN
Local:
- third_party/sources/ptrefall/FluidHTN/reference/Planner.cs
- third_party/sources/ptrefall/FluidHTN/reference/Domain.cs
- third_party/sources/ptrefall/FluidHTN/reference/DomainBuilder.cs
- third_party/sources/ptrefall/FluidHTN/reference/BaseDomainBuilder.cs
- third_party/sources/ptrefall/FluidHTN/reference/LICENSE

Pinned upstream commit:
e67af264cfdf240053f392d4e0e6c620c454eb97

Use:
long-horizon task decomposition for quest chains, progression, Time-Space, recovery sequences and skill composition.

### Win32CaptureSample
Local:
- third_party/sources/robmikh/Win32CaptureSample/reference/SimpleCapture.cpp
- third_party/sources/robmikh/Win32CaptureSample/reference/SimpleCapture.h
- third_party/sources/robmikh/Win32CaptureSample/reference/CaptureSnapshot.cpp
- third_party/sources/robmikh/Win32CaptureSample/reference/CaptureSnapshot.h
- third_party/sources/robmikh/Win32CaptureSample/reference/LICENSE

Pinned upstream commit:
49fefe79fd9b11025f0b5eb91783a98888516070

Use:
Windows.Graphics.Capture / Direct3D11 frame-pool reference, especially CreateFreeThreaded, frame arrival, resize handling and snapshots.

## Architecture routing

### Gate 2 — Cognitive Skill Spine
HTN + GOAP + reactive rules:
- FluidHTN -> strategic decomposition
- ReGoap -> deterministic action sequencing
- NosAi reactive rules -> interrupts/recovery
- all output still goes through Guard/Trust/Safety

### Perception production pipeline
Windows Graphics Capture -> ROI -> detector -> tracker -> OCR-on-demand -> fusion/provenance.
Recommended upstream:
Win32CaptureSample + MMDetection-trained/ONNX models + ByteTrack + PaddleOCR.

### Learning pipeline
Human demonstrations -> Behavior Cloning -> targeted corrective datasets -> offline RL -> optional world model research.
Recommended upstream:
imitation + d3rlpy + Stable-Baselines3/CleanRL baselines + DreamerV3 research.

### Knowledge / memory
SQLite remains primary persistence.
sqlite-vec may add local vector retrieval.
Semantic Kernel is reference/package material for orchestration but not a source of gameplay truth.

### Character/build/inventory optimization
OR-Tools is the preferred general-purpose constraint optimizer candidate.
Use for equipment/resource selection only when the optimization formulation is deterministic and explainable.

## Academic target

Certified autonomous run target:
Combat Level 55
+ sufficient Job Level
+ main progression
+ SP1 + SP2 + SP3
+ pet
+ partner
+ coherent equipment
+ autonomous inventory management.

## Next integration order

1. Win32Capture production adapter
2. OCR + detector + tracker benchmark
3. FluidHTN + ReGoap hybrid planner prototype
4. Skill Library contracts
5. OR-Tools Build/Inventory optimizer prototype
6. demonstration recorder + imitation-learning dataset format
7. offline RL lab
8. world-model sandbox experiments
9. sqlite-vec evaluation
10. end-to-end Gate 2 certification
