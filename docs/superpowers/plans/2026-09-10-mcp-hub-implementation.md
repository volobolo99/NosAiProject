# NosAi MCP Hub Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a governed, local-first MCP Hub and dedicated control panel that supports model routing, simulation, secrets, audit and verified online-to-offline learning.

**Architecture:** Add a focused `nosai/mcp` package behind explicit contracts. Keep MCP advisory and separate from the local gameplay authority; network mode is operator-gated and disabled by default.

**Tech Stack:** Python 3.11+, standard library HTTP/MCP integration, `cryptography` Fernet, JSON Schema, pytest.

**Spec:** `docs/superpowers/specs/2026-09-10-mcp-hub-design.md`

## Global Constraints

- MCP network mode defaults to disabled.
- Only explicit operator confirmation can enable network mode.
- No privileged game/admin/database state enters MCP.
- Secret values are encrypted at rest and never returned or logged.
- MCP never receives direct gameplay execution authority.
- Simulations are deterministic and side-effect-free.

---

### Task 1: Contract and policy foundation

**Files:**
- Create: `nosai/mcp/contracts.py`
- Create: `nosai/mcp/policy.py`
- Create: `schemas/mcp_activation.schema.json`
- Create: `schemas/mcp_status.schema.json`
- Create: `contracts/mcp-hub-001.json`
- Test: `tests/test_mcp_hub_contracts.py`
- Test: `tests/test_mcp_hub_policy.py`

- [ ] Define `McpMode`, `ToolRisk`, `ActivationRequest`, `SimulationRequest`, `ProviderConfig`, and `RouteDecision`.
- [ ] Enforce `McpPolicy` defaults and operator-only activation.
- [ ] Keep privileged and secret-export risks denied regardless of mode.
- [ ] Validate JSON contracts against the typed boundary tests.

### Task 2: Routing, simulation and learning

**Files:**
- Create: `nosai/mcp/config.py`
- Create: `nosai/mcp/router.py`
- Create: `nosai/mcp/simulation.py`
- Create: `nosai/mcp/learning.py`
- Create: `config/mcp.default.json`
- Create: `schemas/mcp_simulation_request.schema.json`
- Create: `schemas/mcp_learning_candidate.schema.json`
- Test: `tests/test_mcp_hub_learning.py`

- [ ] Load a safe default configuration with local provider first.
- [ ] Select the lowest qualified provider allowed by current policy.
- [ ] Return deterministic simulation results for identical inputs.
- [ ] Persist candidates with provenance and reject candidates without observed evidence.
- [ ] Export only validated candidates as offline skills.

### Task 3: Secrets and audit

**Files:**
- Create: `nosai/mcp/secrets.py`
- Create: `nosai/mcp/audit.py`
- Test: `tests/test_mcp_hub_secrets.py`
- Test: `tests/test_mcp_hub_audit.py`

- [ ] Encrypt provider secrets with Fernet using `NOSAI_MCP_MASTER_KEY`.
- [ ] Refuse plaintext storage when the master key is absent.
- [ ] Return metadata and fingerprints only.
- [ ] Redact key/token/secret/password fields before append-only audit persistence.

### Task 4: MCP server and dashboard

**Files:**
- Create: `nosai/mcp/server.py`
- Create: `scripts/mcp_hub_server.py`
- Create: `nosai/mcp/dashboard.py`
- Create: `scripts/mcp_dashboard_server.py`
- Create: `nosai/mcp/static/index.html`
- Modify: `.mcp.json`
- Modify: `pyproject.toml`
- Test: `tests/test_mcp_hub_server.py`

- [ ] Register the versioned MCP tools/resources.
- [ ] Expose dashboard status, activation, provider and metadata endpoints.
- [ ] Add the new MCP server entry while preserving the legacy orchestrator entry.
- [ ] Package the dashboard static asset.
- [ ] Ensure malformed JSON, unauthorized activation and missing key configuration fail closed.

### Task 5: Verification and integration

**Files:**
- Modify: `docs/mcp/README.md`
- Modify: `docs/mcp/AGENT_QUICK_INDEX.md`
- Modify: `docs/mcp/CONTROL_PANEL.md`
- Modify: `docs/mcp/DEVELOPMENT_CONTRACTS.md`
- Modify: `docs/mcp/THREAT_MODEL.md`

- [ ] Run Python smoke tests and the repository Python test suite.
- [ ] Validate JSON schemas and importability without the optional MCP dependency.
- [ ] Start the dashboard in a disposable process and verify `/api/mcp/status`.
- [ ] Verify no plaintext secret appears in response or audit output.
- [ ] Update the project worklog and changelog with exact files and checks.


