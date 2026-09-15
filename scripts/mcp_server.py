"""Backward-compatibility shim + funzioni monkeypatchabili dai test.

Le funzioni che i test patchano con monkeypatch.setattr(mcp_server, ...) DEVONO
essere definite QUI (non importate), perché Python risolve i nomi globali nel
modulo in cui la funzione è definita. Quelle patchabili sono:
  - update_contract_state  (usa PROJECT_ROOT e threading)
  - call_deepseek          (usa DEEPSEEK_API_KEY)
  - chiama_gruppo          (chiama chiama_groq e chiama_openrouter per nome)

Tutto il resto viene importato da nosai/mcp/unified_server.py.
"""
from __future__ import annotations

import json
import os
import sys
import threading  # noqa: F401 — i test patchano mcp_server.threading.Thread
import requests   # noqa: F401 — i test patchano mcp_server.requests.post
from datetime import date
from pathlib import Path
from typing import Any, Dict, List, Sequence, Tuple

_PROJECT_ROOT_SCRIPTS = Path(__file__).resolve().parent.parent
if str(_PROJECT_ROOT_SCRIPTS) not in sys.path:
    sys.path.insert(0, str(_PROJECT_ROOT_SCRIPTS))

# ── Costanti di modulo monkeypatchabili ──────────────────────────────────────
PROJECT_ROOT = _PROJECT_ROOT_SCRIPTS
DEEPSEEK_API_KEY: str = os.getenv("DEEPSEEK_API_KEY", "")
DEEPSEEK_URL: str = os.getenv("DEEPSEEK_URL", "https://api.deepseek.com/chat/completions")

# ── Re-export dal server unificato (tutto ciò che i test non patchano) ───────
from nosai.mcp.unified_server import (  # noqa: F401
    create_unified_server,
    run,
    # Tool orchestrator (non patchati, ma usati direttamente)
    local_generate_skeleton,
    cloud_infill_implementation,
    preflight_contract_check,
    deep_reasoner_solve_crash,
    local_update_documentation,
    # Helpers di rete (non patchati direttamente)
    call_openrouter,
    chiama_groq,
    chiama_openrouter,
    controlla_quota,
    modelli_gratuiti,
    # Validatori
    giudizio_infill,
    giudizio_preflight,
    # Roadmap
    render_roadmap_markdown,
    # Interni
    record,
    _costo_reale_usd,
    _autorizza,
    _rileva_linguaggio_bersaglio,
    _prompt_scheletro,
    _STATO_CHIAMATA,
    # Costanti
    ROSTER,
    VALID_STATES,
    DONE_STATES,
    TOOL_REQUIRED_CAPABILITY,
    PAUSA,
    POLITICHE,
    # Nuova infrastruttura P0-P2
    CircuitBreaker,
    CBState,
    TaskRegistry,
    TaskStatus,
    ObservabilityEmitter,
    ExactMatchCache,
    ToolDefinition,
    TOOL_REGISTRY,
    VCAMP_BUNDLES,
    # P3
    SemanticCache,
    AgentRegistry,
    _begin_call,
    get_current_nonce,
)


# ═══════════════════════════════════════════════════════════════════════════
# FUNZIONI DEFINITE QUI — i test le patchano via monkeypatch.setattr(mcp_server, ...)
# ═══════════════════════════════════════════════════════════════════════════

def call_deepseek(
    model_id: str,
    prompt: str,
    system_prompt: str,
    temperature: float = 0.1,
    max_tokens: int = 4096,
) -> str:
    """Usa il module-level DEEPSEEK_API_KEY (patchabile dai test)."""
    if not DEEPSEEK_API_KEY:
        raise RuntimeError(
            "DEEPSEEK_API_KEY non impostata: definirla come variabile d'ambiente, mai nel sorgente."
        )
    headers = {"Authorization": f"Bearer {DEEPSEEK_API_KEY}", "Content-Type": "application/json"}
    payload = {
        "model": model_id,
        "messages": [
            {"role": "system", "content": system_prompt},
            {"role": "user", "content": prompt},
        ],
        "temperature": temperature,
        "max_tokens": max_tokens,
        "stream": False,
    }
    r = requests.post(DEEPSEEK_URL, headers=headers, json=payload, timeout=180)
    r.raise_for_status()
    corpo = r.json()
    _STATO_CHIAMATA.usage = corpo.get("usage", {})
    message = corpo["choices"][0]["message"]
    content = message.get("content") or ""
    if not content.strip():
        raise RuntimeError(
            f"{model_id} non ha prodotto contenuto: max_tokens={max_tokens} "
            "probabilmente esaurito dal ragionamento. Alzare il budget."
        )
    return content


def chiama_gruppo(
    gruppo: Sequence[str],
    messaggi: List[Dict[str, str]],
    max_tokens: int,
    temperatura: float,
) -> Tuple[str, str]:
    """Chiama groq uno per uno poi openrouter.

    Usa chiama_groq e chiama_openrouter dal namespace di QUESTO modulo
    (non da unified_server) affinché monkeypatch.setattr(mcp_server, ...) funzioni.
    """
    groq_modelli = [m for m in gruppo if m.startswith("groq:")]
    altri_modelli = [m for m in gruppo if not m.startswith("groq:")]
    for modello in groq_modelli:
        try:
            return chiama_groq(modello, messaggi, max_tokens, temperatura)
        except Exception:
            continue
    if altri_modelli:
        return chiama_openrouter(altri_modelli, messaggi, max_tokens, temperatura)
    raise RuntimeError("Nessun modello disponibile per la chiamata")


def update_contract_state(contract_id: str, new_state: str, metrics: str = "") -> str:
    """Usa il module-level PROJECT_ROOT e threading (entrambi patchabili dai test)."""
    if new_state not in VALID_STATES:
        return f"ERROR: stato non valido '{new_state}'. Ammessi: {', '.join(VALID_STATES)}"
    ledger_path = PROJECT_ROOT / "contracts" / "ledger.json"
    try:
        with open(ledger_path, "r", encoding="utf-8") as f:
            ledger = json.load(f)
    except (FileNotFoundError, json.JSONDecodeError):
        return "ERROR: ledger mancante o corrotto"
    gate_number = None
    contract = None
    owning_gate = None
    for gate in ledger["gates"]:
        for c in gate["contracts"]:
            if c["cid"] == contract_id:
                contract = c
                owning_gate = gate
                gate_number = gate["gate"]
                break
        if contract:
            break
    if contract is None:
        return f"ERROR: contratto '{contract_id}' assente dal ledger"
    contract["status"] = new_state
    contract["updated"] = date.today().isoformat()
    if metrics:
        contract["metrics"] = metrics
    non_dropped = [c for c in owning_gate["contracts"] if c["status"] != "DROPPED"]
    total_non_dropped = len(non_dropped)
    done_count = sum(1 for c in owning_gate["contracts"] if c["status"] in DONE_STATES)
    owning_gate["completion_pct"] = (
        100 if total_non_dropped == 0 else round(100 * done_count / total_non_dropped)
    )
    tmp_path = ledger_path.with_suffix(".json.tmp")
    try:
        with open(tmp_path, "w", encoding="utf-8") as f:
            json.dump(ledger, f, indent=2, ensure_ascii=False)
        os.replace(tmp_path, ledger_path)
    except Exception:
        if tmp_path.exists():
            tmp_path.unlink()
        return "ERROR: impossibile aggiornare il ledger"

    def _regen():
        try:
            content = render_roadmap_markdown(ledger)
            with open(PROJECT_ROOT / "docs" / "MASTER_ROADMAP.md", "w", encoding="utf-8") as f:
                f.write(content)
        except Exception:
            pass

    threading.Thread(target=_regen, daemon=True).start()
    pct = owning_gate["completion_pct"]
    return f"[CID: {contract_id}] [STATE: {new_state}] gate {gate_number} -> {pct}% | roadmap in rigenerazione locale"


if __name__ == "__main__":
    run()
