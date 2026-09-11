"""R-003 (docs/REMAINING_WORK.md): verifica l'esecuzione reale del percorso
Direttore -> Auditor attraverso la facciata McpChief (la stessa che i tool MCP
mcp_propose_change/mcp_audit_change usano in nosai/mcp/server.py), con
evidenza persistita su un AuditLog reale -- non solo un'asserzione in memoria.

Prima di questo file, test_mcp_hub_governance.py copriva solo il percorso di
veto chiamando direttamente McpDirector/McpAuditor: nessun test passava dalla
facciata Chief, nessuno registrava un evento reale su AuditLog, e il percorso
approvato (handoff valido, nessun veto) non era mai stato esercitato.
"""

from pathlib import Path

from nosai.mcp.audit import AuditLog
from nosai.mcp.chief import McpChief
from nosai.mcp.contracts import SimulationRequest
from nosai.mcp.simulation import run_simulation


def _chief(tmp_path: Path) -> McpChief:
    return McpChief(tmp_path / "mcp")


def _record_proposal(audit: AuditLog, proposal: dict) -> None:
    # Stessa sequenza di nosai/mcp/server.py:mcp_propose_change.
    audit.append("change_proposed", {"proposal_id": proposal["proposal_id"], "component": proposal["component"], "files": list(proposal["files"])})


def _record_verdict(audit: AuditLog, verdict: dict) -> None:
    # Stessa sequenza di nosai/mcp/server.py:mcp_audit_change.
    audit.append("change_audited", {"proposal_id": verdict["proposal_id"], "approved": verdict["approved"], "rollback_required": verdict["rollback_required"]})


def test_veto_path_is_recorded_as_real_audit_evidence(tmp_path):
    chief = _chief(tmp_path)
    audit = AuditLog(tmp_path / "audit.jsonl")

    proposal = chief.propose_improvement("router", "cambia la soglia di timeout", ["nosai/mcp/router.py"])
    _record_proposal(audit, proposal)

    verdict = chief.audit_proposal(proposal, {"tests_passed": True, "contract_compatible": True, "safety_unchanged": False})
    _record_verdict(audit, verdict)

    assert verdict["approved"] is False
    assert verdict["rollback_required"] is True

    events = [line for line in audit.path.read_text(encoding="utf-8").splitlines() if line.strip()]
    assert len(events) == 2
    assert '"event": "change_proposed"' in events[0]
    assert '"event": "change_audited"' in events[1]
    assert '"rollback_required": true' in events[1]


def test_approved_path_is_a_valid_handoff_with_no_veto(tmp_path):
    chief = _chief(tmp_path)
    audit = AuditLog(tmp_path / "audit.jsonl")

    proposal = chief.propose_improvement("router", "cambia la soglia di timeout", ["nosai/mcp/router.py"])
    _record_proposal(audit, proposal)

    verdict = chief.audit_proposal(proposal, {"tests_passed": True, "contract_compatible": True, "safety_unchanged": True})
    _record_verdict(audit, verdict)

    assert verdict["approved"] is True
    assert verdict["rollback_required"] is False

    events = audit.path.read_text(encoding="utf-8")
    assert '"approved": true' in events
    assert '"rollback_required": false' in events


def test_director_refuses_a_protected_path_before_the_auditor_ever_sees_it(tmp_path):
    chief = _chief(tmp_path)

    raised = False
    try:
        chief.propose_improvement("policy", "allenta un vincolo", ["nosai/mcp/policy.py"])
    except PermissionError:
        raised = True

    assert raised, "il Direttore deve rifiutare i percorsi protetti prima che l'Auditor entri in gioco"


def test_auditor_independently_vetoes_a_protected_path_even_if_somehow_proposed(tmp_path):
    # L'Auditor "non si fida mai dello stato del Direttore" (nosai/mcp/auditor.py):
    # lo stesso controllo sui percorsi protetti va rifatto qui, non ereditato.
    chief = _chief(tmp_path)
    from dataclasses import asdict

    from nosai.mcp.director import ChangeProposal

    bypassed = ChangeProposal("fake-id", "policy", "aggirare il Direttore", ("nosai/mcp/policy.py",))
    verdict = chief.audit_proposal(asdict(bypassed), {"tests_passed": True, "contract_compatible": True, "safety_unchanged": True})

    assert verdict["approved"] is False
    assert verdict["rollback_required"] is True


def test_simulation_replay_is_deterministic_given_the_same_scenario_and_seed():
    request = SimulationRequest(scenario_id="patrol-loop-01", seed=42, horizon_ticks=10, actions=[{"kind": "move"}])

    first = run_simulation(request)
    second = run_simulation(request)

    assert first == second, "un replay con lo stesso scenario e lo stesso seed deve produrre lo stesso esito, altrimenti non e' un replay"
    assert first["side_effects"] == []


def test_simulation_replay_is_recorded_as_real_audit_evidence(tmp_path):
    audit = AuditLog(tmp_path / "audit.jsonl")
    request = SimulationRequest(scenario_id="patrol-loop-01", seed=42, horizon_ticks=10)

    result = run_simulation(request)
    # Stessa sequenza di nosai/mcp/server.py:mcp_run_simulation.
    audit.append("simulation_completed", {"scenario_id": request.scenario_id, "seed": request.seed})

    logged = audit.path.read_text(encoding="utf-8")
    assert '"event": "simulation_completed"' in logged
    assert '"scenario_id": "patrol-loop-01"' in logged
    assert result["scenario_id"] == "patrol-loop-01"


def test_a_different_seed_changes_the_simulated_outcome():
    baseline = run_simulation(SimulationRequest(scenario_id="patrol-loop-01", seed=1))
    other = run_simulation(SimulationRequest(scenario_id="patrol-loop-01", seed=2))

    assert baseline["score"] != other["score"], "un seed diverso deve produrre un esito diverso, altrimenti il seed non e' usato davvero"
