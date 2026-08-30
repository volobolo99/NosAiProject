# ADR-0002 — Real/Demo Data Separation

**Status:** Accepted  
**Date:** 2026-08-30

## Decision

All externally visible observations must carry an explicit source classification: `LIVE`, `DERIVED`, `CACHED`, `SIMULATED` or `UNKNOWN`.

Simulated providers and fixtures must be isolated from production selection. Dashboards must never present simulated values as live telemetry.

## Rationale

The current Gate 1 implementation contains both real integration foundations and demonstrative/mixed telemetry. Explicit classification prevents false confidence during integration and release validation.

## Consequences

- Test fixtures remain useful without contaminating production behavior.
- UI can accurately communicate data freshness/source.
- Gate 1 acceptance tests can assert real-data provenance.
