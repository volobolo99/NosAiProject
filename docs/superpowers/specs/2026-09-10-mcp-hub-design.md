# NosAi MCP Hub Design

**Status:** approved for implementation  
**Version:** 1.0.0  
**Date:** 2026-09-10

## Goal

Provide NosAiProject with a local-first MCP Hub that improves the existing local engine through qualified model routing, deterministic simulation, governed network assistance, encrypted API-key management, auditability and verified online-to-offline learning.

## Architectural decisions

1. The MCP Hub is additive. Existing runtime, `Guard`, `Trust`, `Safety`, perception and simulation modules remain authoritative for gameplay execution.
2. MCP network mode is disabled by default. Only an explicit operator action with `confirmation: "operator"` can enable it.
3. The MCP Director may propose or stage changes, but an external Auditor/Safety path controls promotion. The Director cannot modify policy, tests, contracts or audit storage to approve itself.
4. Provider routing is capability-aware and local/free-first. A remote provider is selected only when network mode is active and the provider is enabled and qualified.
5. Secrets are encrypted at rest and are never returned by MCP tools, dashboard responses or audit records. The dashboard exposes metadata only.
6. Simulation is deterministic and side-effect-free. Learning records are candidates until observed evidence and validation promote them to offline skills.

## Components

- `nosai/mcp/contracts.py`: versioned typed request/result boundaries.
- `nosai/mcp/policy.py`: immutable minimum policy and risk authorization.
- `nosai/mcp/router.py`: provider catalog and local/free-first route selection.
- `nosai/mcp/simulation.py`: deterministic short-horizon evaluator.
- `nosai/mcp/learning.py`: candidate, validation and offline skill export.
- `nosai/mcp/secrets.py`: encrypted local secret store.
- `nosai/mcp/audit.py`: append-only redacted event log.
- `nosai/mcp/server.py`: MCP tools/resources.
- `nosai/mcp/dashboard.py`: dedicated local control API.
- `nosai/mcp/static/index.html`: operator panel.

## Data flow

`Dashboard/Operator → McpPolicy → ModelRouter/Simulation → Audit → LearningFactory → Offline skill store`

MCP outputs are advisory. The gameplay chain remains `Observe → World Model → Planner → Guard → Trust → Safety → Execute → Verify`.

## API surface

MCP tools: `mcp_status`, `mcp_activate_network`, `mcp_provider_catalog`, `mcp_choose_provider`, `mcp_run_simulation`, `mcp_learning_candidate`, `mcp_validate_learning`.

Dashboard endpoints: `GET /api/mcp/status`, `GET /api/mcp/providers`, `GET /api/mcp/secrets`, `POST /api/mcp/activate`, `POST /api/mcp/secrets`, `DELETE /api/mcp/secrets?provider_id=<id>`.

## Acceptance criteria

- Fresh process starts in offline mode even if configuration contains `network_enabled: true`.
- Non-operator activation is rejected.
- Privileged and secret-export risks are denied.
- Identical simulation request and seed produce identical output.
- A candidate without observed evidence cannot become an offline skill.
- Secret values are absent from metadata and audit output.
- Existing gameplay execution path is not bypassed.


