# Runtime / Inference / Memory — Upstream Research

## microsoft/onnxruntime
- License: MIT (verified)
- Role: production inference runtime.
- NosAi use: C#/C++ ONNX execution with GPU providers; preferred runtime boundary for compact perception models.
- Target: NosAi.Runtime perception/inference adapters.
- Priority: VERY HIGH.
- Strategy: consume official packages, do not vendor giant source tree.

## ggml-org/llama.cpp
- License: MIT (verified)
- Role: local LLM inference server/runtime.
- NosAi use: Tier-3 local reasoning provider; never blocks Safety/Recovery.
- Target: AI Provider Router.
- Priority: HIGH.

## microsoft/semantic-kernel
- License: MIT (verified)
- Role: .NET orchestration/memory/RAG patterns.
- NosAi use: reference for tool/plugin abstraction and local knowledge retrieval.
- Boundary: retrieved text cannot override WorldState provenance or Safety.
- Priority: MEDIUM-HIGH.

## asg017/sqlite-vec
- License: dual MIT/Apache-2.0 (verified)
- Role: vector search extension for SQLite.
- NosAi use: compact local semantic retrieval over knowledge/episodes while retaining SQLite-first storage.
- Target: Memory/Knowledge Base experiments.
- Priority: HIGH.
