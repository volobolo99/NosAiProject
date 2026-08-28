# NosAiProject

Clean development repository for NosAi, an AI runtime for NosTale.

## Repository strategy

- `NosAiProject` is the new clean source of truth for development.
- `volobolo99/NosAi` is retained as a legacy/reference repository.
- Legacy code is selectively reused only after architectural and test review.
- No GGUF weights, local binaries, credentials, or machine-specific artifacts belong in Git.

## Current baseline

The first clean runtime slice contains:

- explicit core DTO contracts;
- deterministic Rule-Based provider;
- isolated `llama.cpp` OpenAI-compatible provider;
- fail-closed Safety Gate;
- orchestrator with deterministic fallback;
- unit coverage for fallback behavior;
- CI running the test suite.

## Local LLM

Default endpoint: `http://127.0.0.1:8080/v1/chat/completions`.

Default model identifier: `Qwen2.5-7B-Instruct-Q4_K_M.gguf`.

The server and model are host prerequisites and must remain outside the repository.

## Next gate

Before adding vision, memory, dashboard, or live input, validate the clean runtime locally and add a real localhost llama.cpp integration test that is opt-in and never required by CI.

See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).
