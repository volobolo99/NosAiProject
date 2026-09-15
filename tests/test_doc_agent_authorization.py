"""Test di accettazione per il contratto C-309 (contracts/mcp-role-enforcement-gaps-015.json).

scripts/doc_agent.py produceva scheletri e documenti senza mai passare da
nosai.mcp.enforcement.require_capability: lo stesso risultato che i tool MCP
local_generate_skeleton/local_update_documentation autorizzano per ruolo restava
raggiungibile senza controllo passando dal CLI. _autorizza_incarico chiude quella
via, e revisore.md/verificatore.md guadagnano il tool MCP per il ruolo che gia'
dichiarano (Reviewer -> preflight_contract_check, Testing -> deep_reasoner_solve_crash).
"""

import sys
from pathlib import Path

import pytest

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "scripts"))

import doc_agent  # noqa: E402
from nosai.mcp.enforcement import RoleEnforcementError  # noqa: E402


def test_autorizza_incarico_blocca_senza_employee_id():
    with pytest.raises(PermissionError):
        doc_agent._autorizza_incarico({"format": "markdown"})


def test_autorizza_incarico_blocca_employee_id_vuoto():
    with pytest.raises(PermissionError):
        doc_agent._autorizza_incarico({"format": "markdown", "employee_id": ""})


def test_autorizza_incarico_permette_capability_giusta():
    # employee.documentation ha "documentation" fra le proprie capability.
    doc_agent._autorizza_incarico({"format": "markdown", "employee_id": "employee.documentation"})


def test_autorizza_incarico_permette_scaffold_su_python():
    # "scaffold" e' stato aggiunto a employee.documentation ed employee.coding in bdb6f5f.
    doc_agent._autorizza_incarico({"format": "python", "employee_id": "employee.coding"})


def test_autorizza_incarico_blocca_capability_mancante():
    # employee.security ha solo security/audit/policy: niente scaffold per un incarico Python.
    with pytest.raises(RoleEnforcementError):
        doc_agent._autorizza_incarico({"format": "python", "employee_id": "employee.security"})


def test_run_blocca_senza_employee_id_senza_chiamare_il_modello(monkeypatch, tmp_path):
    def _non_deve_essere_chiamato(prompt):
        raise AssertionError("call_local non doveva essere invocato senza employee_id")

    monkeypatch.setattr(doc_agent, "call_local", _non_deve_essere_chiamato)
    monkeypatch.setattr(doc_agent, "LEDGER", tmp_path / "ai_task_ledger.jsonl")

    result = doc_agent.run(
        {"file": "scratch/non_creato.md", "purpose": "test", "format": "markdown"},
        dry_run=True,
    )

    assert result["status"] == "blocked"
    assert any("employee_id" in item for item in result["missing_items"])


def test_run_blocca_su_ruolo_senza_capability_senza_chiamare_il_modello(monkeypatch, tmp_path):
    def _non_deve_essere_chiamato(prompt):
        raise AssertionError("call_local non doveva essere invocato con un ruolo senza capability")

    monkeypatch.setattr(doc_agent, "call_local", _non_deve_essere_chiamato)
    monkeypatch.setattr(doc_agent, "LEDGER", tmp_path / "ai_task_ledger.jsonl")

    result = doc_agent.run(
        {
            "file": "scratch/non_creato.py",
            "purpose": "test",
            "format": "python",
            "employee_id": "employee.security",
        },
        dry_run=True,
    )

    assert result["status"] == "blocked"


def _tools_dichiarati(agent_md_path: Path) -> str:
    for line in agent_md_path.read_text(encoding="utf-8").splitlines():
        if line.strip().startswith("tools:"):
            return line
    return ""


def test_revisore_ha_il_tool_preflight_per_il_proprio_ruolo():
    riga = _tools_dichiarati(ROOT / ".claude" / "agents" / "revisore.md")
    assert "mcp__orchestrator__preflight_contract_check" in riga


def test_verificatore_ha_il_tool_diagnostics_per_il_proprio_ruolo():
    riga = _tools_dichiarati(ROOT / ".claude" / "agents" / "verificatore.md")
    assert "mcp__orchestrator__deep_reasoner_solve_crash" in riga
