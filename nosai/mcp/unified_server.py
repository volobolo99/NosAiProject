"""Unified MCP server — nosai-mcp-unified.

Sostituisce scripts/mcp_server.py (orchestrator) e nosai/mcp/server.py (hub)
in un unico FastMCP. Aggiunge P0-P2: CircuitBreaker, TaskRegistry,
ObservabilityEmitter, ExactMatchCache, ToolDefinition registry, vMCP bundle scoping.
"""
from __future__ import annotations

import enum
import hashlib
import json
import logging
import os
import re
import sqlite3
import sys
import threading
import time
import uuid
from dataclasses import dataclass, field
from datetime import date
from pathlib import Path
from typing import Any, Callable, Dict, List, Optional, Sequence, Tuple

import requests
from dotenv import load_dotenv

PROJECT_ROOT = Path(__file__).resolve().parent.parent.parent
load_dotenv(PROJECT_ROOT / ".env")

if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))

try:
    from mcp.server.fastmcp import FastMCP
except ImportError:
    FastMCP = None  # type: ignore[assignment,misc]

from nosai.mcp.audit import AuditLog
from nosai.mcp.config import load_config
from nosai.mcp.contracts import ActivationRequest, SimulationRequest, ToolRisk
from nosai.mcp.enforcement import RoleEnforcementError, require_capability
from nosai.mcp.roles import DEFAULT_EMPLOYEE_ROLES, RoleArchitect

# ── Lazy imports per evitare cicli al boot ──────────────────────────────────
def _import_hub_deps():
    from nosai.mcp.learning import LearningFactory
    from nosai.mcp.chief import McpChief
    from nosai.mcp.policy import McpPolicy
    from nosai.mcp.router import ModelRouter
    from nosai.mcp.inference import InferenceGateway
    from nosai.mcp.secrets import SecretStore
    from nosai.mcp.simulation import run_simulation
    return LearningFactory, McpChief, McpPolicy, ModelRouter, InferenceGateway, SecretStore, run_simulation


# ═══════════════════════════════════════════════════════════════════════════
# COSTANTI DI MODULO
# ═══════════════════════════════════════════════════════════════════════════

ROSTER: Dict[str, str] = {
    "worker": "qwen/qwen3-coder-30b-a3b-instruct",
    "auditor": "deepseek-v4-flash",
    "preflight": "google/gemini-2.5-flash-lite",
    "local_scaffold": "qwen2.5-coder:7b",
}

VALID_STATES = [
    "DRAFT", "SKELETON_OK", "INFILLED", "PREFLIGHT_OK",
    "VERIFIED", "ASAN_VERIFIED", "TEST_VERIFIED", "MERGED", "BLOCKED", "DROPPED",
]
DONE_STATES = ["VERIFIED", "ASAN_VERIFIED", "TEST_VERIFIED", "MERGED"]

TOOL_REQUIRED_CAPABILITY: Dict[str, str] = {
    "local_generate_skeleton": "scaffold",
    "cloud_infill_implementation": "coding",
    "preflight_contract_check": "contracts",
    "deep_reasoner_solve_crash": "diagnostics",
    "local_update_documentation": "documentation",
}

CB_THRESHOLDS: Dict[str, int] = {
    "cloud_infill_implementation": 3,
    "deep_reasoner_solve_crash": 2,
    "preflight_contract_check": 5,
}
CB_COOLDOWNS: Dict[str, float] = {
    "cloud_infill_implementation": 60.0,
    "deep_reasoner_solve_crash": 30.0,
    "preflight_contract_check": 20.0,
}

VCAMP_BUNDLES: Dict[str, List[str]] = {
    "employee.documentation": ["local_generate_skeleton", "local_update_documentation"],
    "employee.coding": [
        "local_generate_skeleton", "cloud_infill_implementation",
        "preflight_contract_check", "update_contract_state",
    ],
    "employee.testing": ["preflight_contract_check", "deep_reasoner_solve_crash"],
    "employee.mcp_chief": [
        "mcp_chief_health", "mcp_chief_observe_health",
        "mcp_chief_recommendations", "mcp_verify_roles",
    ],
}

LEDGER = PROJECT_ROOT / "data" / "ai_task_ledger.jsonl"

_STATO_CHIAMATA = threading.local()

OLLAMA_URL = os.getenv("OLLAMA_LOCAL_URL", "http://localhost:11434/api/generate")
OPENROUTER_URL = os.getenv("OPENROUTER_URL", "https://openrouter.ai/api/v1/chat/completions")
DEEPSEEK_URL = os.getenv("DEEPSEEK_URL", "https://api.deepseek.com/chat/completions")

_ESTENSIONI_LINGUAGGIO = {
    ".cs": "csharp", ".cpp": "cpp", ".hpp": "cpp", ".h": "cpp", ".py": "python",
}
_ISTRUZIONI_LINGUAGGIO = {
    "csharp": (
        "Il file bersaglio e' C# (.cs). Genera SOLO lo scheletro strutturale: namespace, "
        "classi/interfacce, firme dei metodi e proprieta' con i tipi esatti del contratto, "
        "corpi che sollevano 'throw new NotImplementedException();'. Nessuna logica interna."
    ),
    "cpp": (
        "Il file bersaglio e' C++ (.hpp/.h). Genera SOLO l'header strutturale: include guard, "
        "dichiarazioni di classi/struct e firme di funzione con i tipi esatti del contratto. "
        "Nessuna implementazione."
    ),
    "python": (
        "Il file bersaglio e' Python (.py). Genera SOLO lo scheletro strutturale: classi/funzioni "
        "con type annotations e corpi che sollevano 'raise NotImplementedError'. Nessuna logica interna."
    ),
}
_ISTRUZIONI_LINGUAGGIO_IGNOTO = (
    "Non e' stato possibile determinare il linguaggio del file bersaglio dalle chiavi note "
    "del contratto. Individualo dall'estensione dichiarata e genera SOLO lo scheletro strutturale. "
    "Se il linguaggio non e' determinabile, fermati e dichiaralo."
)

try:
    import free_first as _ff
    PAUSA = _ff.Pausa()
except ImportError:
    class _Pausa:
        pausa = 0.0
        def __call__(self, s): self.pausa = s
    PAUSA = _Pausa()

POLITICHE: Dict[str, Dict] = {
    "cloud_infill_implementation": {"cascata": True, "budget_secondi": 30.0, "pagato": "openrouter/gpt-4-turbo"},
    "preflight_contract_check": {"cascata": True, "budget_secondi": 2.0, "pagato": "openrouter/gpt-4-turbo"},
    "deep_reasoner_solve_crash": {"cascata": False, "budget_secondi": 0.0, "pagato": "deepseek/deepseek-r1"},
}


# ═══════════════════════════════════════════════════════════════════════════
# P0 — CIRCUIT BREAKER
# ═══════════════════════════════════════════════════════════════════════════

class CBState(enum.Enum):
    CLOSED = 0
    OPEN = 1
    HALF_OPEN = 2


class CircuitBreaker:
    """Circuit breaker a tre stati per tool cloud. Thread-safe."""

    def __init__(
        self,
        thresholds: Dict[str, int] | None = None,
        cooldowns: Dict[str, float] | None = None,
    ) -> None:
        self._thresholds: Dict[str, int] = thresholds or {}
        self._cooldowns: Dict[str, float] = cooldowns or {}
        self._states: Dict[str, CBState] = {}
        self._failures: Dict[str, int] = {}
        self._last_open: Dict[str, float] = {}
        self._lock = threading.RLock()

    def state(self, tool: str) -> CBState:
        with self._lock:
            s = self._states.get(tool, CBState.CLOSED)
            if s == CBState.OPEN:
                cooldown = self._cooldowns.get(tool, 30.0)
                if time.monotonic() - self._last_open.get(tool, 0.0) >= cooldown:
                    self._states[tool] = CBState.HALF_OPEN
                    return CBState.HALF_OPEN
            return s

    def call(self, tool: str, fn: Callable[[], Any]) -> Any:
        s = self.state(tool)
        if s == CBState.OPEN:
            raise RuntimeError(f"Circuit OPEN per '{tool}': troppi errori recenti, attendi il cooldown.")
        try:
            result = fn()
            self._record_success(tool)
            return result
        except Exception:
            self._record_failure(tool)
            raise

    def _record_failure(self, tool: str) -> None:
        with self._lock:
            self._failures[tool] = self._failures.get(tool, 0) + 1
            threshold = self._thresholds.get(tool, 3)
            if self._failures[tool] >= threshold:
                self._states[tool] = CBState.OPEN
                self._last_open[tool] = time.monotonic()

    def _record_success(self, tool: str) -> None:
        with self._lock:
            self._failures[tool] = 0
            self._states[tool] = CBState.CLOSED


# ═══════════════════════════════════════════════════════════════════════════
# P1 — TASK REGISTRY (Tasks extension)
# ═══════════════════════════════════════════════════════════════════════════

class TaskStatus(enum.Enum):
    PENDING = "pending"
    RUNNING = "running"
    COMPLETED = "completed"
    FAILED = "failed"
    CANCELLED = "cancelled"


class TaskRegistry:
    """Registry in-memory di task asincroni. Thread-safe."""

    def __init__(self) -> None:
        self._tasks: Dict[str, dict] = {}
        self._lock = threading.RLock()

    def submit(self, tool_name: str, args: dict) -> str:
        task_id = str(uuid.uuid4())
        with self._lock:
            self._tasks[task_id] = {
                "task_id": task_id,
                "tool": tool_name,
                "args": args,
                "status": TaskStatus.PENDING.value,
                "progress": 0,
                "message": "",
                "result": None,
                "error": None,
                "created_at": time.time(),
                "updated_at": time.time(),
            }
        return task_id

    def get(self, task_id: str) -> dict:
        with self._lock:
            if task_id not in self._tasks:
                raise KeyError(f"task_id sconosciuto: {task_id}")
            return dict(self._tasks[task_id])

    def update(self, task_id: str, progress: int, message: str = "") -> dict:
        with self._lock:
            if task_id not in self._tasks:
                raise KeyError(f"task_id sconosciuto: {task_id}")
            self._tasks[task_id].update(
                status=TaskStatus.RUNNING.value,
                progress=max(0, min(100, progress)),
                message=message,
                updated_at=time.time(),
            )
            return dict(self._tasks[task_id])

    def complete(self, task_id: str, result: str) -> dict:
        with self._lock:
            if task_id not in self._tasks:
                raise KeyError(f"task_id sconosciuto: {task_id}")
            self._tasks[task_id].update(
                status=TaskStatus.COMPLETED.value,
                progress=100,
                result=result,
                updated_at=time.time(),
            )
            return dict(self._tasks[task_id])

    def fail(self, task_id: str, error: str) -> dict:
        with self._lock:
            if task_id not in self._tasks:
                raise KeyError(f"task_id sconosciuto: {task_id}")
            self._tasks[task_id].update(
                status=TaskStatus.FAILED.value,
                error=error,
                updated_at=time.time(),
            )
            return dict(self._tasks[task_id])

    def cancel(self, task_id: str) -> dict:
        with self._lock:
            if task_id not in self._tasks:
                raise KeyError(f"task_id sconosciuto: {task_id}")
            self._tasks[task_id].update(
                status=TaskStatus.CANCELLED.value,
                updated_at=time.time(),
            )
            return dict(self._tasks[task_id])


# ═══════════════════════════════════════════════════════════════════════════
# P1 — OBSERVABILITY EMITTER
# ═══════════════════════════════════════════════════════════════════════════

class ObservabilityEmitter:
    """Emette log strutturato JSON con trace_id e span_id per ogni chiamata modello."""

    def __init__(self) -> None:
        self._logger = logging.getLogger("nosai.mcp.obs")
        self._local = threading.local()

    def begin_trace(self) -> str:
        trace_id = str(uuid.uuid4())
        self._local.trace_id = trace_id
        return trace_id

    def emit(
        self,
        tool: str,
        model: str,
        duration_ms: float,
        tokens_in: int,
        tokens_out: int,
        cost_usd: float,
        status: str,
        **extra: Any,
    ) -> None:
        span_id = str(uuid.uuid4())
        record_data: Dict[str, Any] = {
            "trace_id": self.current_trace_id(),
            "span_id": span_id,
            "tool": tool,
            "model": model,
            "duration_ms": round(duration_ms, 2),
            "tokens_in": tokens_in,
            "tokens_out": tokens_out,
            "cost_usd": round(cost_usd, 6),
            "status": status,
        }
        record_data.update(extra)
        self._logger.info(json.dumps(record_data, ensure_ascii=False))

    def current_trace_id(self) -> str:
        return getattr(self._local, "trace_id", "")


# ═══════════════════════════════════════════════════════════════════════════
# P1 — EXACT MATCH CACHE (L1)
# ═══════════════════════════════════════════════════════════════════════════

class ExactMatchCache:
    """Cache L1 exact-match SQLite. Chiave = SHA256 hex del payload. TTL default 86400s."""

    def __init__(self, db_path: Path, ttl_seconds: int = 86400) -> None:
        self._path = db_path
        self._ttl = ttl_seconds
        self._lock = threading.RLock()
        db_path.parent.mkdir(parents=True, exist_ok=True)
        with self._connect() as conn:
            conn.execute(
                "CREATE TABLE IF NOT EXISTS cache "
                "(key TEXT PRIMARY KEY, result TEXT NOT NULL, created_at REAL NOT NULL)"
            )

    def _connect(self) -> sqlite3.Connection:
        conn = sqlite3.connect(self._path, timeout=10, isolation_level=None)
        conn.execute("PRAGMA journal_mode=WAL")
        return conn

    def _key(self, payload: str) -> str:
        return hashlib.sha256(payload.encode("utf-8")).hexdigest()

    def get(self, payload: str) -> str | None:
        key = self._key(payload)
        now = time.time()
        with self._lock, self._connect() as conn:
            row = conn.execute(
                "SELECT result, created_at FROM cache WHERE key = ?", (key,)
            ).fetchone()
        if row is None:
            return None
        result, created_at = row
        if now - created_at > self._ttl:
            return None
        return result

    def put(self, payload: str, result: str) -> None:
        key = self._key(payload)
        now = time.time()
        with self._lock, self._connect() as conn:
            conn.execute(
                "INSERT OR REPLACE INTO cache (key, result, created_at) VALUES (?, ?, ?)",
                (key, result, now),
            )


# ═══════════════════════════════════════════════════════════════════════════
# P2 — TOOL DEFINITION REGISTRY
# ═══════════════════════════════════════════════════════════════════════════

@dataclass
class ToolDefinition:
    name: str
    description: str
    risk: ToolRisk
    allowed_roles: List[str]
    circuit_breaker: bool = False
    cacheable: bool = False


TOOL_REGISTRY: List[ToolDefinition] = [
    ToolDefinition("local_generate_skeleton", "Genera scheletro via Ollama 7B (gratis)", ToolRisk.SAFE, ["employee.coding", "employee.documentation"], False, False),
    ToolDefinition("cloud_infill_implementation", "Riempie corpi via Qwen3 30B", ToolRisk.NETWORK, ["employee.coding"], True, False),
    ToolDefinition("preflight_contract_check", "Valida contratto vs codice via Gemini Flash", ToolRisk.NETWORK, ["employee.coding", "employee.testing"], True, True),
    ToolDefinition("deep_reasoner_solve_crash", "Risolve crash via DeepSeek", ToolRisk.NETWORK, ["employee.testing"], True, False),
    ToolDefinition("local_update_documentation", "Aggiorna docs via Ollama 7B (gratis)", ToolRisk.SAFE, ["employee.documentation", "employee.coding"], False, False),
    ToolDefinition("update_contract_state", "Aggiorna ledger.json e roadmap (nessun modello)", ToolRisk.SAFE, ["*"], False, False),
    ToolDefinition("mcp_status", "Stato e catalog provider", ToolRisk.SAFE, ["*"], False, False),
    ToolDefinition("mcp_activate_network", "Attiva/disattiva rete", ToolRisk.PRIVILEGED, ["*"], False, False),
    ToolDefinition("mcp_provider_catalog", "Lista provider disponibili", ToolRisk.SAFE, ["*"], False, False),
    ToolDefinition("mcp_choose_provider", "Sceglie provider per capability", ToolRisk.SAFE, ["*"], False, False),
    ToolDefinition("mcp_infer", "Inferenza con capability routing", ToolRisk.NETWORK, ["*"], False, False),
    ToolDefinition("mcp_run_simulation", "Esegue simulazione", ToolRisk.NETWORK, ["*"], False, False),
    ToolDefinition("mcp_learning_candidate", "Registra candidato learning", ToolRisk.SAFE, ["*"], False, False),
    ToolDefinition("mcp_validate_learning", "Valida candidato learning", ToolRisk.SAFE, ["*"], False, False),
    ToolDefinition("mcp_propose_change", "Propone miglioramento", ToolRisk.SAFE, ["*"], False, False),
    ToolDefinition("mcp_audit_change", "Audita proposta", ToolRisk.SAFE, ["*"], False, False),
    ToolDefinition("mcp_role_catalog", "Lista binding e proposte", ToolRisk.SAFE, ["*"], False, False),
    ToolDefinition("mcp_record_evidence", "Registra evidenza", ToolRisk.SAFE, ["*"], False, False),
    ToolDefinition("mcp_role_propose_binding", "Propone binding ruolo", ToolRisk.PRIVILEGED, ["*"], False, False),
    ToolDefinition("mcp_role_promote_binding", "Promuove binding", ToolRisk.PRIVILEGED, ["*"], False, False),
    ToolDefinition("mcp_role_rollback_binding", "Rollback binding", ToolRisk.PRIVILEGED, ["*"], False, False),
    ToolDefinition("mcp_chief_health", "Report salute MCP Chief", ToolRisk.OBSERVATION, ["employee.mcp_chief", "*"], False, False),
    ToolDefinition("mcp_chief_observe_health", "Registra osservazione salute", ToolRisk.OBSERVATION, ["employee.mcp_chief", "*"], False, False),
    ToolDefinition("mcp_chief_recommendations", "Raccomandazioni senza mutazione", ToolRisk.SAFE, ["*"], False, False),
    ToolDefinition("mcp_verify_roles", "Valida ruoli e catalogo", ToolRisk.SAFE, ["*"], False, False),
    ToolDefinition("tasks_get", "Recupera stato task asincrono", ToolRisk.SAFE, ["*"], False, False),
    ToolDefinition("tasks_update", "Aggiorna progresso task", ToolRisk.SAFE, ["*"], False, False),
    ToolDefinition("tasks_cancel", "Cancella task in coda", ToolRisk.SAFE, ["*"], False, False),
]


# ═══════════════════════════════════════════════════════════════════════════
# HELPERS INTERNI (da scripts/mcp_server.py — codice verificato)
# ═══════════════════════════════════════════════════════════════════════════

def record(entry: dict) -> None:
    LEDGER.parent.mkdir(parents=True, exist_ok=True)
    with LEDGER.open("a", encoding="utf-8") as handle:
        handle.write(json.dumps(entry, ensure_ascii=False) + "\n")


def _costo_reale_usd(model_id: str) -> float:
    try:
        prezzi = json.loads(
            (PROJECT_ROOT / "scripts" / "model_prices.json").read_text(encoding="utf-8")
        )["models"]
    except (FileNotFoundError, json.JSONDecodeError, KeyError):
        return 0.0
    prezzo = prezzi.get(model_id)
    if not prezzo:
        return 0.0
    usage = getattr(_STATO_CHIAMATA, "usage", {})
    costo = (
        usage.get("prompt_tokens", 0) * prezzo.get("input", 0.0)
        + usage.get("completion_tokens", 0) * prezzo.get("output", 0.0)
    )
    return round(costo, 6)


def _autorizza(tool_name: str, employee_id: str) -> None:
    capability_richiesta = TOOL_REQUIRED_CAPABILITY.get(tool_name)
    if capability_richiesta is None:
        raise KeyError(f"tool non censito in TOOL_REQUIRED_CAPABILITY: {tool_name}")
    try:
        require_capability(employee_id, capability_richiesta)
    except RoleEnforcementError as exc:
        raise PermissionError(
            f"{employee_id} non e' autorizzato a chiamare {tool_name}: {exc}"
        ) from exc


def _rileva_linguaggio_bersaglio(specifications_json: str) -> str | None:
    percorso = None
    try:
        dati = json.loads(specifications_json)
    except (json.JSONDecodeError, TypeError):
        dati = None
    if isinstance(dati, dict):
        for chiave in ("file", "target_file", "file_bersaglio"):
            valore = dati.get(chiave)
            if isinstance(valore, str) and valore:
                percorso = valore
                break
    if percorso is None:
        match = re.search(r'["\']([\w./\\-]+\.(?:cs|cpp|hpp|h|py))["\']', specifications_json)
        if match:
            percorso = match.group(1)
    if percorso is None:
        return None
    return _ESTENSIONI_LINGUAGGIO.get(Path(percorso).suffix.lower())


def _prompt_scheletro(specifications_json: str) -> str:
    linguaggio = _rileva_linguaggio_bersaglio(specifications_json)
    istruzioni = _ISTRUZIONI_LINGUAGGIO.get(linguaggio, _ISTRUZIONI_LINGUAGGIO_IGNOTO)
    return (
        "Sei uno Skeleton Architect. " + istruzioni
        + " Mantieni rigore assoluto su firme, tipi e allineamenti; "
        "nessuna API inventata, nessun segnaposto vago."
    )


def render_roadmap_markdown(ledger: dict) -> str:
    lines: List[str] = []
    lines.append("# NosAi — Master Roadmap")
    lines.append("")
    lines.append(
        "_Generato automaticamente da `update_contract_state` a partire da "
        "`contracts/ledger.json`. Non modificare a mano: verra' sovrascritto "
        "alla prossima chiamata._"
    )
    stack = ledger.get("stack", {})
    if stack:
        parts = [stack[key] for key in ("managed", "python", "native") if stack.get(key)]
        if parts:
            lines.append("**Stack**: " + " · ".join(parts))
    vocabulary = ledger.get("vocabulary")
    if vocabulary:
        lines.append(f"**Vocabolario**: {vocabulary}")
    note_di_lettura = ledger.get("note_di_lettura")
    if note_di_lettura:
        lines.append("")
        lines.append(f"> {note_di_lettura}")
    for gate in ledger.get("gates", []):
        gate_num = gate.get("gate", "")
        gate_title = gate.get("title", "")
        completion_pct = gate.get("completion_pct", "")
        lines.append("")
        lines.append(f"## Gate {gate_num} — {gate_title} ({completion_pct}%)")
        lines.append("")
        lines.append("| CID | Titolo | Stato |")
        lines.append("|---|---|---|")
        for contract in gate.get("contracts", []):
            cid = contract.get("cid", "")
            title = contract.get("title", "")
            status = contract.get("status", "")
            lines.append(f"| {cid} | {title} | {status} |")
            for fld in ("updated", "metrics", "note", "blocker"):
                value = contract.get(fld)
                if value:
                    lines.append(f"  - {fld}: {value}")
    domande_aperte = ledger.get("domande_aperte", [])
    if domande_aperte:
        lines.append("")
        lines.append("## Domande aperte")
        lines.append("")
        for domanda in domande_aperte:
            lines.append(f"- {domanda}")
    phase_mapping_note = ledger.get("phase_mapping_note")
    signature_resolution_note = ledger.get("signature_resolution_note")
    if phase_mapping_note or signature_resolution_note:
        lines.append("")
        lines.append("## Note")
        lines.append("")
        if phase_mapping_note:
            lines.append(f"- {phase_mapping_note}")
        if signature_resolution_note:
            lines.append(f"- {signature_resolution_note}")
    return "\n".join(lines) + "\n"


# ── Token Store: legge env var a ogni chiamata (P0 — rotazione senza restart) ──

def _openrouter_key() -> str:
    return os.environ.get("OPENROUTER_API_KEY", "")


def _deepseek_key() -> str:
    return os.environ.get("DEEPSEEK_API_KEY", "")


def _groq_key() -> str:
    return os.environ.get("GROQ_API_KEY", "")


# ── Chiamate modelli (da scripts/mcp_server.py — codice verificato) ──────────

def call_openrouter(
    model_id: str,
    prompt: str,
    system_prompt: str,
    temperature: float = 0.1,
    max_tokens: int = 4096,
) -> str:
    if "deepseek" in model_id.lower():
        raise ValueError(
            f"DeepSeek non passa da OpenRouter: '{model_id}' va chiamato con call_deepseek "
            "sull'API nativa. Nessun instradamento alternativo."
        )
    headers = {
        "Authorization": f"Bearer {_openrouter_key()}",
        "Content-Type": "application/json",
        "HTTP-Referer": "https://github.com/NosAiProject",
        "X-Title": "NosAi Zero-Waste Assembly",
    }
    payload = {
        "model": model_id,
        "messages": [
            {"role": "system", "content": system_prompt},
            {"role": "user", "content": prompt},
        ],
        "temperature": temperature,
        "max_tokens": max_tokens,
    }
    r = requests.post(OPENROUTER_URL, headers=headers, json=payload, timeout=120)
    r.raise_for_status()
    corpo = r.json()
    _STATO_CHIAMATA.usage = corpo.get("usage", {})
    return corpo["choices"][0]["message"]["content"]


def call_deepseek(
    model_id: str,
    prompt: str,
    system_prompt: str,
    temperature: float = 0.1,
    max_tokens: int = 4096,
) -> str:
    key = _deepseek_key()
    if not key:
        raise RuntimeError(
            "DEEPSEEK_API_KEY non impostata: definirla come variabile d'ambiente, mai nel sorgente."
        )
    headers = {"Authorization": f"Bearer {key}", "Content-Type": "application/json"}
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


def controlla_quota(risposta: Any) -> None:
    if risposta.status_code == 429:
        retry_after = risposta.headers.get("retry-after")
        if retry_after is not None:
            attendi = float(retry_after)
        else:
            try:
                import free_first
                attendi = free_first.PAUSA_PREDEFINITA
            except ImportError:
                attendi = 10.0
        try:
            import free_first
            raise free_first.LimiteRaggiunto("Limite raggiunto", attendi=attendi)
        except ImportError:
            raise RuntimeError(f"Limite raggiunto, attendere {attendi} secondi")


def chiama_groq(
    modello: str,
    messaggi: List[Dict[str, str]],
    max_tokens: int,
    temperatura: float,
) -> Tuple[str, str]:
    modello_senza_prefisso = modello[len("groq:"):] if modello.startswith("groq:") else modello
    GROQ_URL = "https://api.groq.com/openai/v1/chat/completions"
    headers = {
        "Authorization": f"Bearer {_groq_key()}",
        "Content-Type": "application/json",
    }
    payload = {
        "model": modello_senza_prefisso,
        "messages": messaggi,
        "max_tokens": max_tokens,
        "temperature": temperatura,
    }
    r = requests.post(GROQ_URL, headers=headers, json=payload, timeout=120)
    controlla_quota(r)
    r.raise_for_status()
    testo = r.json()["choices"][0]["message"]["content"]
    return (testo, modello)


def chiama_openrouter(
    gruppo: Sequence[str],
    messaggi: List[Dict[str, str]],
    max_tokens: int,
    temperatura: float,
) -> Tuple[str, str]:
    if len(gruppo) > 3:
        gruppo = gruppo[:3]
    headers = {
        "Authorization": f"Bearer {_openrouter_key()}",
        "Content-Type": "application/json",
        "HTTP-Referer": "https://github.com/NosAiProject",
        "X-Title": "NosAi Zero-Waste Assembly",
    }
    payload = {
        "models": list(gruppo),
        "messages": messaggi,
        "temperature": temperatura,
        "max_tokens": max_tokens,
        "provider": {"max_price": 0.0},
    }
    r = requests.post(OPENROUTER_URL, headers=headers, json=payload, timeout=120)
    controlla_quota(r)
    r.raise_for_status()
    response_data = r.json()
    testo = response_data["choices"][0]["message"]["content"]
    modello_risposto = response_data["choices"][0]["model"]
    return (testo, modello_risposto)


def chiama_gruppo(
    gruppo: Sequence[str],
    messaggi: List[Dict[str, str]],
    max_tokens: int,
    temperatura: float,
) -> Tuple[str, str]:
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


def modelli_gratuiti() -> List[str]:
    roster_path = PROJECT_ROOT / "scripts" / "free_roster.json"
    try:
        with open(roster_path, "r", encoding="utf-8") as f:
            roster = json.load(f)
    except (FileNotFoundError, json.JSONDecodeError):
        return []
    validati = roster.get("validati", [])
    scartati = roster.get("scartati", {})
    modelli = [m["id"] for m in validati if m["id"] not in scartati]
    modelli.sort(
        key=lambda m: next(
            (item["sec_medi"] for item in validati if item["id"] == m), float("inf")
        )
    )
    groq_modelli = [m for m in modelli if m.startswith("groq:")]
    altri_modelli = [m for m in modelli if not m.startswith("groq:")]
    return groq_modelli + altri_modelli


def giudizio_infill(scheletro_e_contratto: str) -> Callable[[str], List[str]]:
    def giudice(codice_candidato: str) -> List[str]:
        difetti: List[str] = []
        try:
            compile(codice_candidato, "<string>", "exec")
        except SyntaxError as e:
            return [f"sintassi non valida: {str(e)}"]
        try:
            scheletro_firme: set = set()
            for line in scheletro_e_contratto.split("\n"):
                if line.strip().startswith("def "):
                    match = re.match(r"def\s+(\w+)\s*\(([^)]*)\)", line)
                    if match:
                        scheletro_firme.add(f"{match.group(1)}({match.group(2)})")
        except Exception:
            scheletro_firme = set()
        if scheletro_firme:
            for line in codice_candidato.split("\n"):
                if line.strip().startswith("def "):
                    match = re.match(r"def\s+(\w+)\s*\(([^)]*)\)", line)
                    if match:
                        nome = match.group(1)
                        parametri = match.group(2)
                        firma = f"{nome}({parametri})"
                        if firma not in scheletro_firme:
                            for orig in scheletro_firme:
                                orig_nome = orig.split("(")[0]
                                if orig_nome == nome:
                                    orig_params = [p.strip() for p in orig.split("(")[1].rstrip(")").split(",") if p.strip()]
                                    new_params = [p.strip() for p in parametri.split(",") if p.strip()]
                                    if len(new_params) > len(orig_params) and all(p1 == p2 for p1, p2 in zip(orig_params, new_params)):
                                        difetti.append(f"firma alterata: {nome} ha parametri aggiunti")
                                    break
        lines = codice_candidato.split("\n")
        i = 0
        while i < len(lines):
            line = lines[i]
            if line.strip().startswith("def "):
                match = re.match(r"def\s+(\w+)\s*\(([^)]*)\)", line)
                if match:
                    nome_funzione = match.group(1)
                    i += 1
                    while i < len(lines):
                        current_line = lines[i].strip()
                        if current_line == "raise NotImplementedError":
                            difetti.append(f"corpo della funzione '{nome_funzione}' solleva NotImplementedError")
                            break
                        elif current_line in ("", ) or current_line.startswith("#"):
                            i += 1
                            continue
                        elif current_line.startswith("def ") or current_line.startswith("class "):
                            break
                        else:
                            break
            i += 1
        for pattern in (r"TODO", r"FIXME", r"XXX"):
            if re.search(pattern, codice_candidato, re.IGNORECASE):
                difetti.append(f"contiene segnaposto '{pattern}'")
        return difetti
    return giudice


def giudizio_preflight(risposta: str) -> List[str]:
    risposta = risposta.strip()
    if not risposta:
        return ["risposta vuota"]
    if "APPROVED" in risposta:
        return []
    lines = risposta.split("\n")
    if any(line.strip().startswith("-") for line in lines):
        return []
    return ["risposta non contiene un verdetto valido: né APPROVED né elenco di difetti"]


# ═══════════════════════════════════════════════════════════════════════════
# ISTANZE GLOBALI DI INFRASTRUTTURA
# ═══════════════════════════════════════════════════════════════════════════

_circuit_breaker = CircuitBreaker(thresholds=CB_THRESHOLDS, cooldowns=CB_COOLDOWNS)
_task_registry = TaskRegistry()
_obs = ObservabilityEmitter()
_cache = ExactMatchCache(PROJECT_ROOT / "data" / "mcp_cache.db")


# ═══════════════════════════════════════════════════════════════════════════
# SERVER UNIFICATO
# ═══════════════════════════════════════════════════════════════════════════


# ═══════════════════════════════════════════════════════════════════════════
# TOOL ORCHESTRATOR A LIVELLO DI MODULO
# (esposti qui per i test che li chiamano direttamente via import mcp_server)
# ═══════════════════════════════════════════════════════════════════════════

def local_generate_skeleton(specifications_json: str, employee_id: str) -> str:
    """PASSO 1 (GRATIS - OLLAMA 7B): Genera lo scheletro formale con le firme."""
    _autorizza("local_generate_skeleton", employee_id)
    sys_prompt = _prompt_scheletro(specifications_json)
    payload = {
        "model": ROSTER["local_scaffold"],
        "prompt": f"{sys_prompt}\n\nSpecifiche tecniche (JSON):\n{specifications_json}",
        "stream": False,
        "options": {"temperature": 0.1},
    }
    t0 = time.monotonic()
    r = requests.post(OLLAMA_URL, json=payload, timeout=120)
    r.raise_for_status()
    risposta = r.json().get("response", "")
    _obs.emit("local_generate_skeleton", ROSTER["local_scaffold"], (time.monotonic() - t0) * 1000, 0, 0, 0.0, "completed")
    record({
        "task_id": "local_generate_skeleton", "model": ROSTER["local_scaffold"],
        "calls": 1, "estimated_cost_usd": 0.0, "status": "completed",
        "file": "mcp_tool", "words": len(risposta.split()), "employee_id": employee_id,
    })
    return risposta


def cloud_infill_implementation(skeleton_and_contract: str, employee_id: str) -> str:
    """PASSO 2 (LOW COST - QWEN3 30B): Riempie solo la logica interna senza toccare firme."""
    _autorizza("cloud_infill_implementation", employee_id)
    sys_prompt = (
        "Sei il Senior Infiller. Ricevi uno scheletro strutturale e il contratto di funzionamento. "
        "Devi implementare TUTTI i corpi delle funzioni nel rispetto assoluto dei vincoli di memoria e firme. "
        "NON usare commenti // TODO o scorciatoche. Restituisci il codice completo pronto alla produzione."
    )
    t0 = time.monotonic()
    testo = _circuit_breaker.call(
        "cloud_infill_implementation",
        lambda: call_openrouter(ROSTER["worker"], skeleton_and_contract, sys_prompt, temperature=0.1, max_tokens=8192),
    )
    usage = getattr(_STATO_CHIAMATA, "usage", {})
    cost = _costo_reale_usd(ROSTER["worker"])
    _obs.emit("cloud_infill_implementation", ROSTER["worker"], (time.monotonic() - t0) * 1000, usage.get("prompt_tokens", 0), usage.get("completion_tokens", 0), cost, "completed")
    record({
        "task_id": "cloud_infill_implementation", "model": ROSTER["worker"],
        "calls": 1, "estimated_cost_usd": cost,
        "status": "completed", "file": "mcp_tool", "employee_id": employee_id,
        "usage": usage,
    })
    return testo


def preflight_contract_check(contract_json: str, generated_code: str, employee_id: str) -> str:
    """PASSO 3 (ULTRA-FAST - GEMINI FLASH): Controlla discrepanze prima della compilazione."""
    _autorizza("preflight_contract_check", employee_id)
    cache_key = contract_json + "|||" + generated_code
    cached = _cache.get(cache_key)
    if cached is not None:
        return cached
    sys_prompt = (
        "Sei il Controllore di Qualità di Pre-Flight. Verifica se il codice generato rispetta il contratto "
        "e i limiti di memoria. Rispondi 'APPROVED' se è impeccabile, oppure elenca in modo sintetico "
        "i problemi critici riscontrati."
    )
    prompt = f"CONTRATTO:\n{contract_json}\n\nCODICE PRODOTTO:\n{generated_code}"
    t0 = time.monotonic()
    testo = _circuit_breaker.call(
        "preflight_contract_check",
        lambda: call_openrouter(ROSTER["preflight"], prompt, sys_prompt, temperature=0.0, max_tokens=1024),
    )
    usage = getattr(_STATO_CHIAMATA, "usage", {})
    cost = _costo_reale_usd(ROSTER["preflight"])
    _obs.emit("preflight_contract_check", ROSTER["preflight"], (time.monotonic() - t0) * 1000, usage.get("prompt_tokens", 0), usage.get("completion_tokens", 0), cost, "completed")
    record({
        "task_id": "preflight_contract_check", "model": ROSTER["preflight"],
        "calls": 1, "estimated_cost_usd": cost,
        "status": "completed", "file": "mcp_tool", "employee_id": employee_id,
        "usage": usage,
    })
    _cache.put(cache_key, testo)
    return testo


def deep_reasoner_solve_crash(error_context_json: str, employee_id: str) -> str:
    """PASSO 4 (DEBUG PROFONDO - DEEPSEEK): Risolve crash di memoria o deadlock logici."""
    _autorizza("deep_reasoner_solve_crash", employee_id)
    sys_prompt = (
        "Sei il Principal Systems & Memory Security Engineer. Analizza il crash nativo o la violazione di invarianti. "
        "Identifica l'errore logico o di puntatore e fornisci la correzione chirurgica in C++ o Python."
    )
    t0 = time.monotonic()
    testo = _circuit_breaker.call(
        "deep_reasoner_solve_crash",
        lambda: call_deepseek(ROSTER["auditor"], error_context_json, sys_prompt, temperature=0.6, max_tokens=12000),
    )
    usage = getattr(_STATO_CHIAMATA, "usage", {})
    cost = _costo_reale_usd(ROSTER["auditor"])
    _obs.emit("deep_reasoner_solve_crash", ROSTER["auditor"], (time.monotonic() - t0) * 1000, usage.get("prompt_tokens", 0), usage.get("completion_tokens", 0), cost, "completed")
    record({
        "task_id": "deep_reasoner_solve_crash", "model": ROSTER["auditor"],
        "calls": 1, "estimated_cost_usd": cost,
        "status": "completed", "file": "mcp_tool", "employee_id": employee_id,
        "usage": usage,
    })
    return testo


def local_update_documentation(doc_payload_json: str, employee_id: str) -> str:
    """PASSO 5 (GRATIS - OLLAMA 7B): Aggiorna roadmap, changelog senza spendere token cloud."""
    _autorizza("local_update_documentation", employee_id)
    payload = {
        "model": ROSTER["local_scaffold"],
        "prompt": f"Sei un Technical Writer. Aggiorna la documentazione/roadmap in Markdown basandoti su questi dati:\n{doc_payload_json}",
        "stream": False,
    }
    t0 = time.monotonic()
    r = requests.post(OLLAMA_URL, json=payload, timeout=120)
    r.raise_for_status()
    risposta = r.json().get("response", "")
    _obs.emit("local_update_documentation", ROSTER["local_scaffold"], (time.monotonic() - t0) * 1000, 0, 0, 0.0, "completed")
    record({
        "task_id": "local_update_documentation", "model": ROSTER["local_scaffold"],
        "calls": 1, "estimated_cost_usd": 0.0, "status": "completed",
        "file": "mcp_tool", "words": len(risposta.split()), "employee_id": employee_id,
    })
    return risposta


def update_contract_state(contract_id: str, new_state: str, metrics: str = "") -> str:
    """Aggiorna lo stato di un contratto in contracts/ledger.json e rigenera MASTER_ROADMAP.md."""
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
    owning_gate["completion_pct"] = 100 if total_non_dropped == 0 else round(100 * done_count / total_non_dropped)
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


# ═══════════════════════════════════════════════════════════════════════════
# SERVER FACTORY
# ═══════════════════════════════════════════════════════════════════════════

def create_unified_server(config_path: Path | str | None = None):
    if FastMCP is None:
        raise RuntimeError("MCP runtime dependency is missing; install the project's mcp extra")

    config = load_config(config_path)
    if config.get("network_enabled"):
        config["network_enabled"] = False

    (
        LearningFactory, McpChief, McpPolicy, ModelRouter,
        InferenceGateway, SecretStore, run_simulation,
    ) = _import_hub_deps()

    policy = McpPolicy()
    router = ModelRouter.from_config(config, policy)
    chief = McpChief(
        Path(config.get("role_bindings_path", "data/mcp/role_bindings.json")).parent,
        bindings=router.role_bindings,
        router=router,
    )
    audit = AuditLog(config["audit_path"])
    learning = LearningFactory(config["learning_path"])
    try:
        secret_store = SecretStore(config["secret_path"])
    except RuntimeError:
        secret_store = None
    inference = InferenceGateway(router, secret_store)

    server = FastMCP("nosai-mcp-unified")

    # ── Registra i tool orchestrator (già definiti a livello di modulo) ───
    server.tool()(local_generate_skeleton)
    server.tool()(cloud_infill_implementation)
    server.tool()(preflight_contract_check)
    server.tool()(deep_reasoner_solve_crash)
    server.tool()(local_update_documentation)
    server.tool()(update_contract_state)

    # ── Hub tools ─────────────────────────────────────────────────────────

    @server.tool()
    def mcp_status() -> str:
        return json.dumps({
            "schema_version": "mcp.status.v1",
            "mode": policy.mode.value,
            "network_enabled": policy.network_enabled,
            "providers": router.catalog(),
        }, ensure_ascii=False)

    @server.tool()
    def mcp_activate_network(request_json: str) -> str:
        request = ActivationRequest.from_mapping(json.loads(request_json))
        policy.apply(request)
        audit.append("network_mode_changed", {"enabled": policy.network_enabled, "mode": policy.mode.value})
        return mcp_status()

    @server.tool()
    def mcp_provider_catalog() -> str:
        return json.dumps({
            "schema_version": "mcp.provider.catalog.v1",
            "providers": router.catalog(),
        }, ensure_ascii=False)

    @server.tool()
    def mcp_choose_provider(capability: str = "") -> str:
        decision = router.choose(capability or None)
        audit.append("provider_selected", {"provider_id": decision.provider_id, "model_id": decision.model_id, "network_used": decision.network_used})
        return json.dumps(decision.__dict__, ensure_ascii=False)

    @server.tool()
    def mcp_infer(prompt: str, capability: str = "", role_id: str = "") -> str:
        result = inference.infer(prompt, capability or None, role_id=role_id or None)
        audit.append("inference_completed", {"capability": capability, "role_id": role_id or None, "provider": router.choose(capability or None, role_id or None).provider_id})
        return json.dumps(result, ensure_ascii=False)

    @server.tool()
    def mcp_run_simulation(request_json: str) -> str:
        request = SimulationRequest.from_mapping(json.loads(request_json))
        result = run_simulation(request)
        audit.append("simulation_completed", {"scenario_id": request.scenario_id, "seed": request.seed})
        return json.dumps(result, ensure_ascii=False)

    @server.tool()
    def mcp_learning_candidate(topic: str, payload_json: str, source: str, evidence_count: int) -> str:
        candidate = learning.record_candidate(topic, json.loads(payload_json), source, evidence_count)
        return json.dumps({"candidate_id": candidate.candidate_id, "status": candidate.status}, ensure_ascii=False)

    @server.tool()
    def mcp_validate_learning(candidate_id: str) -> str:
        result = learning.validate_candidate(candidate_id)
        return json.dumps({"candidate_id": result.candidate_id, "status": result.status.value, "reason": result.reason}, ensure_ascii=False)

    @server.tool()
    def mcp_propose_change(component: str, summary: str, files_json: str) -> str:
        proposal = chief.propose_improvement(component, summary, json.loads(files_json))
        audit.append("change_proposed", {"proposal_id": proposal["proposal_id"], "component": component, "files": list(proposal["files"])})
        return json.dumps({"proposal_id": proposal["proposal_id"], "status": proposal["status"], "files": list(proposal["files"])}, ensure_ascii=False)

    @server.tool()
    def mcp_audit_change(proposal_json: str, checks_json: str) -> str:
        proposal = json.loads(proposal_json)
        verdict = chief.audit_proposal(proposal, json.loads(checks_json))
        audit.append("change_audited", {"proposal_id": verdict["proposal_id"], "approved": verdict["approved"], "rollback_required": verdict["rollback_required"]})
        return json.dumps(verdict, ensure_ascii=False)

    @server.tool()
    def mcp_role_catalog() -> str:
        bindings = router.role_bindings.list() if router.role_bindings is not None else []
        proposals = router.role_bindings.proposals() if router.role_bindings is not None else []
        return json.dumps({"schema_version": "mcp.role.catalog.v1", "bindings": bindings, "proposals": proposals}, ensure_ascii=False)

    @server.tool()
    def mcp_record_evidence(
        candidate_digest: str,
        evidence_kind: str,
        executor_id: str,
        result_json: str,
        signer_id: str = "",
    ) -> str:
        rec = chief.record_evidence(
            candidate_digest, evidence_kind, executor_id,
            json.loads(result_json), signer_id=signer_id or "mcp-evidence-authority",
        )
        audit.append("evidence_recorded", {"evidence_id": rec["evidence_id"], "evidence_kind": rec["evidence_kind"], "executor_id": rec["executor_id"]})
        return json.dumps(rec, ensure_ascii=False)

    @server.tool()
    def mcp_role_propose_binding(
        employee_id: str,
        primary_model: str,
        fallback_models_json: str = "[]",
        author_id: str = "operator",
    ) -> str:
        if router.role_bindings is None:
            raise RuntimeError("role binding registry is not configured")
        proposal = chief.bindings.propose(
            employee_id, primary_model,
            json.loads(fallback_models_json), author_id=author_id,
        )
        audit.append("role_binding_proposed", {"proposal_id": proposal["proposal_id"], "employee_id": employee_id})
        return json.dumps(proposal, ensure_ascii=False)

    @server.tool()
    def mcp_role_promote_binding(proposal_id: str, evidence_ids_json: str, confirmation: str = "") -> str:
        if router.role_bindings is None:
            raise RuntimeError("role binding registry is not configured")
        result = chief.promote_binding(proposal_id, json.loads(evidence_ids_json), confirmation)
        audit.append("role_binding_promoted", {"proposal_id": proposal_id, "employee_id": result["employee_id"], "version": result["version"]})
        return json.dumps(result, ensure_ascii=False)

    @server.tool()
    def mcp_role_rollback_binding(employee_id: str, confirmation: str = "") -> str:
        if router.role_bindings is None:
            raise RuntimeError("role binding registry is not configured")
        result = chief.rollback_binding(employee_id, confirmation)
        audit.append("role_binding_rolled_back", {"employee_id": employee_id, "version": result["version"]})
        return json.dumps(result, ensure_ascii=False)

    @server.tool()
    def mcp_chief_health() -> str:
        result = chief.health_check()
        audit.append("chief_health_checked", {"status": result["status"], "failed_checks": result["failed_checks"]})
        return json.dumps(result, ensure_ascii=False)

    @server.tool()
    def mcp_chief_observe_health(
        name: str,
        status: str,
        details_json: str = "{}",
        ttl_s: int = 60,
    ) -> str:
        observation = chief.observe_health(name, status, json.loads(details_json), ttl_s=ttl_s)
        audit.append("chief_health_observed", {"name": observation["name"], "status": observation["status"]})
        return json.dumps(observation, ensure_ascii=False)

    @server.tool()
    def mcp_chief_recommendations() -> str:
        result = chief.health_check()
        return json.dumps({"schema_version": "mcp.chief.recommendations.v1", "recommendations": result["recommendations"]}, ensure_ascii=False)

    @server.tool()
    def mcp_verify_roles() -> str:
        errors = RoleArchitect().verify()
        employee_errors = RoleArchitect.verify_employee_catalog(DEFAULT_EMPLOYEE_ROLES)
        all_errors = errors + employee_errors
        return json.dumps({"valid": not all_errors, "errors": all_errors, "employee_count": len(DEFAULT_EMPLOYEE_ROLES)}, ensure_ascii=False)

    # ── Task tools (Tasks extension P1) ───────────────────────────────────

    @server.tool()
    def tasks_get(task_id: str) -> str:
        """Recupera lo stato di un task asincrono per il suo task_id."""
        try:
            return json.dumps(_task_registry.get(task_id), ensure_ascii=False)
        except KeyError as exc:
            return json.dumps({"error": str(exc)}, ensure_ascii=False)

    @server.tool()
    def tasks_update(task_id: str, progress: int, message: str = "") -> str:
        """Aggiorna il progresso di un task in esecuzione (0-100)."""
        try:
            return json.dumps(_task_registry.update(task_id, progress, message), ensure_ascii=False)
        except KeyError as exc:
            return json.dumps({"error": str(exc)}, ensure_ascii=False)

    @server.tool()
    def tasks_cancel(task_id: str) -> str:
        """Cancella un task in attesa o in esecuzione."""
        try:
            return json.dumps(_task_registry.cancel(task_id), ensure_ascii=False)
        except KeyError as exc:
            return json.dumps({"error": str(exc)}, ensure_ascii=False)

    # ── Resource ──────────────────────────────────────────────────────────

    @server.resource("nosai://mcp/policy")
    def mcp_policy_resource() -> str:
        return json.dumps({
            "network_default": False,
            "privileged_tools": "denied",
            "secret_export": "denied",
            "execution_authority": "local-safety-gate",
        }, ensure_ascii=False)

    return server


def run(config_path: Path | str | None = None) -> None:
    create_unified_server(config_path).run(transport="stdio")


if __name__ == "__main__":
    run()
