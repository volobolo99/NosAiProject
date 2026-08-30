# ADR-0005 — Versioned API and Protocol Contracts

**Status:** Accepted  
**Date:** 2026-08-30

## Decision

Public API, network protocol and persisted contract changes must be explicitly versioned when compatibility can be affected. Changes require corresponding contract tests and documentation updates.

## Consequences

- Client/runtime compatibility becomes explicit.
- Silent protocol drift is prevented.
- AI agents must not casually modify public contracts while implementing unrelated tasks.
