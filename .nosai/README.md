# NosAi Control Plane

This directory contains the governance layer for autonomous AI-assisted development.

- `ORCHESTRATOR.md` — coordination contract and safety boundaries.
- `ORCHESTRATOR_PROMPT.md` — runtime operating instructions.
- `PROJECT_STATE.md` — persistent project/task state.

This layer does not itself execute Claude, Cursor or shell commands. Runtime execution must be wired to the local development environment only after CLI/tooling commands are verified. Until then, these files are the shared protocol between the agents.
