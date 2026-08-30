# ADR-0004 — Verification Before Release

**Status:** Accepted  
**Date:** 2026-08-30

## Decision

A milestone can be marked `DONE` after implementation, build and required local tests. It can be marked `VERIFIED` only after the required integration, end-to-end and, where applicable, real-environment evidence is available.

## Consequences

- Source code presence is not treated as product completion.
- Release readiness has objective evidence requirements.
- Real-environment blockers remain visible instead of being hidden by optimistic status labels.
