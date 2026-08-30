# ADR-0003 — Runtime Safety Authority

**Status:** Accepted  
**Date:** 2026-08-30

## Decision

The NosAi runtime is authoritative for safety, authorization and execution validation. Control Panel, smartphone clients and other remote clients may request supported operations but cannot enforce safety solely on their side.

## Consequences

- A compromised or outdated client cannot directly bypass server/runtime safety.
- Every privileged operation has one authoritative enforcement point.
- UI tests complement but never replace runtime security tests.
