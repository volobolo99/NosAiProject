# SOURCE: joslat/agent-memory-dotnet
# UPSTREAM PATH: README.md
# UPSTREAM REVISION (blob SHA): 7821d97d7ce3b3c37c55537e557c932e7aa0dd71
# LICENSE: MIT
# STATUS: reference snapshot; NOT wired into NosAi runtime

Agent Memory for .NET is a graph-native persistent memory engine. The project documents three first-class memory layers: short-term conversation history, long-term facts/preferences/entities, and reasoning traces. It supports vector, full-text, hybrid and graph traversal retrieval, optional GraphRAG, bitemporal recall, explicit ownership/scope, and audit trails.

NosAi relevance:
- persistent episodic/semantic/reasoning memory design;
- temporal validity and non-destructive invalidation;
- provenance from memory back to source messages/extractors;
- auditable retrieval and ownership boundaries.

Important adaptation rule: NosAi must keep provenance (`Network`, `Memory`, `Screen`, `Local`, `Operator`, `Unknown`) and must never let recalled memory silently become authoritative gameplay truth.

This file is a research synopsis rather than a verbatim copy. See upstream repository and recorded SHA for complete implementation and documentation.
