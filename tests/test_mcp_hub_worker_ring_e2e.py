"""R-003 (docs/REMAINING_WORK.md): esercita l'anello "worker" del percorso MCP
Director -> worker -> Auditor, mai testato prima insieme al resto del ciclo.
`mcp_infer` esegue DAVVERO un modello (Ollama locale, gratuito), non un mock,
nella stessa sequenza di nosai/mcp/server.py: mcp_propose_change ->
mcp_audit_change -> mcp_infer, con evidenza persistita su un AuditLog reale.

test_mcp_hub_director_auditor_e2e.py copre gia' Director<->Auditor da soli;
qui manca solo l'anello che quel file dichiara esplicitamente aperto.

Il test che chiama davvero Ollama richiede il provider locale in esecuzione su
127.0.0.1:11434 (lo stesso "ollama-local" del catalogo di default, tier 0,
network_required=False, gia' verificato attivo da scripts/check_credentials.py
in questa sessione): se non risponde, si dichiara saltato con il motivo esatto
invece di fallire o di passare senza aver verificato nulla -- stessa regola
ambiente-reale di ClientWindowDpiProbeTests/InteractiveDesktopOnlyFactAttribute
sul lato C#. Il test di instradamento non ha invece bisogno di Ollama: sceglie
il provider senza eseguirlo, quindi gira sempre.
"""

from __future__ import annotations

import json
import urllib.error
import urllib.request
from pathlib import Path

import pytest

from nosai.mcp.audit import AuditLog
from nosai.mcp.chief import McpChief
from nosai.mcp.config import DEFAULT_CONFIG
from nosai.mcp.inference import InferenceGateway
from nosai.mcp.policy import McpPolicy
from nosai.mcp.router import ModelRouter


def _ollama_reachable() -> bool:
    try:
        urllib.request.urlopen("http://127.0.0.1:11434/api/tags", timeout=3)
        return True
    except (urllib.error.URLError, TimeoutError, OSError):
        return False


requires_ollama = pytest.mark.skipif(
    not _ollama_reachable(),
    reason="Ollama non raggiungibile su http://127.0.0.1:11434: l'anello worker non e' verificabile senza il provider locale reale",
)


def _server_wiring(tmp_path: Path):
    """Stessa costruzione di create_server() in nosai/mcp/server.py (server.py:28-44),
    isolata su tmp_path cosi' il test non tocca mai data/mcp/ reale."""
    config = json.loads(json.dumps(DEFAULT_CONFIG))
    config["role_bindings_path"] = str(tmp_path / "role_bindings.json")
    config["state_path"] = str(tmp_path / "state.sqlite3")
    policy = McpPolicy()
    router = ModelRouter.from_config(config, policy)
    chief = McpChief(tmp_path / "mcp", bindings=router.role_bindings, router=router)
    audit = AuditLog(tmp_path / "audit.jsonl")
    inference = InferenceGateway(router, secrets=None)
    return chief, audit, inference, router


def test_documentation_capability_routes_to_the_free_local_provider(tmp_path):
    """Instradamento puro, senza eseguire nulla: non richiede Ollama in esecuzione."""
    _, _, _, router = _server_wiring(tmp_path)

    decision = router.choose("documentation")

    assert decision.provider_id == "ollama-local", "il capability 'documentation' non deve atterrare silenziosamente su un provider a pagamento"
    assert decision.network_used is False


@requires_ollama
def test_the_worker_ring_runs_a_real_model_after_director_and_auditor_approve(tmp_path):
    chief, audit, inference, router = _server_wiring(tmp_path)

    # 1) Direttore: propone, come mcp_propose_change (server.py:91-94).
    proposal = chief.propose_improvement("router", "aumenta il timeout di richiesta", ["nosai/mcp/router.py"])
    audit.append("change_proposed", {"proposal_id": proposal["proposal_id"], "component": proposal["component"], "files": list(proposal["files"])})

    # 2) Auditor: approva, come mcp_audit_change (server.py:96-101).
    verdict = chief.audit_proposal(proposal, {"tests_passed": True, "contract_compatible": True, "safety_unchanged": True})
    audit.append("change_audited", {"proposal_id": verdict["proposal_id"], "approved": verdict["approved"], "rollback_required": verdict["rollback_required"]})
    assert verdict["approved"] is True, "il worker non deve girare se l'Auditor non ha approvato"

    # 3) Worker: mcp_infer esegue DAVVERO qwen2.5-coder:7b via Ollama locale (server.py:67-71).
    decision = router.choose("documentation")
    result = inference.infer("Rispondi con una sola parola: pronto.", "documentation")
    audit.append("inference_completed", {"capability": "documentation", "role_id": None, "provider": decision.provider_id})

    assert isinstance(result, dict)
    assert result.get("response", "").strip() != "", "Ollama ha risposto ma senza testo: l'anello worker non ha prodotto nulla di verificabile"

    events = [json.loads(line) for line in audit.path.read_text(encoding="utf-8").splitlines() if line.strip()]
    assert [event["event"] for event in events] == ["change_proposed", "change_audited", "inference_completed"]
    assert events[2]["payload"]["provider"] == "ollama-local"
