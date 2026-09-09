import os
import json
import re
import requests
import threading
from datetime import date
from dotenv import load_dotenv
from pathlib import Path
from mcp.server.fastmcp import FastMCP

PROJECT_ROOT = Path(__file__).resolve().parent.parent
load_dotenv(PROJECT_ROOT / ".env")

mcp = FastMCP("orchestrator")

OPENROUTER_API_KEY = os.getenv("OPENROUTER_API_KEY", "")
OPENROUTER_URL = os.getenv("OPENROUTER_URL", "https://openrouter.ai/api/v1/chat/completions")
OLLAMA_URL = os.getenv("OLLAMA_LOCAL_URL", "http://localhost:11434/api/generate")

# DeepSeek si chiama sulla sua API nativa, dove sta il credito dell'operatore.
# La chiave si legge dall'ambiente, mai dal sorgente.
DEEPSEEK_API_KEY = os.getenv("DEEPSEEK_API_KEY", "")
DEEPSEEK_URL = os.getenv("DEEPSEEK_URL", "https://api.deepseek.com/chat/completions")

ROSTER = {
    "worker": "qwen/qwen3-coder-30b-a3b-instruct",
    "auditor": "deepseek-v4-flash",
    "preflight": "google/gemini-2.5-flash-lite",
    "local_scaffold": "qwen2.5-coder:7b"
}

def call_openrouter(model_id: str, prompt: str, system_prompt: str, temperature: float = 0.1, max_tokens: int = 4096) -> str:
    if "deepseek" in model_id.lower():
        raise ValueError(
            f"DeepSeek non passa da OpenRouter: '{model_id}' va chiamato con call_deepseek "
            "sull'API nativa. Nessun instradamento alternativo."
        )
    headers = {
        "Authorization": f"Bearer {OPENROUTER_API_KEY}",
        "Content-Type": "application/json",
        "HTTP-Referer": "https://github.com/NosAiProject",
        "X-Title": "NosAi Zero-Waste Assembly"
    }
    payload = {
        "model": model_id,
        "messages": [
            {"role": "system", "content": system_prompt},
            {"role": "user", "content": prompt}
        ],
        "temperature": temperature,
        "max_tokens": max_tokens
    }
    r = requests.post(OPENROUTER_URL, headers=headers, json=payload, timeout=120)
    r.raise_for_status()
    return r.json()["choices"][0]["message"]["content"]

def call_deepseek(model_id: str, prompt: str, system_prompt: str, temperature: float = 0.1, max_tokens: int = 4096) -> str:
    if not DEEPSEEK_API_KEY:
        raise RuntimeError(
            "DEEPSEEK_API_KEY non impostata: definirla come variabile d'ambiente, "
            "mai nel sorgente."
        )
    headers = {
        "Authorization": f"Bearer {DEEPSEEK_API_KEY}",
        "Content-Type": "application/json"
    }
    payload = {
        "model": model_id,
        "messages": [
            {"role": "system", "content": system_prompt},
            {"role": "user", "content": prompt}
        ],
        "temperature": temperature,
        "max_tokens": max_tokens,
        "stream": False
    }
    r = requests.post(DEEPSEEK_URL, headers=headers, json=payload, timeout=180)
    r.raise_for_status()
    message = r.json()["choices"][0]["message"]
    content = message.get("content") or ""
    if not content.strip():
        # I modelli di ragionamento spendono il budget di max_tokens in
        # reasoning_content prima di scrivere content: un budget stretto
        # lascia content vuoto. Meglio fallire che restituire il nulla.
        raise RuntimeError(
            f"{model_id} non ha prodotto contenuto: max_tokens={max_tokens} "
            "probabilmente esaurito dal ragionamento. Alzare il budget."
        )
    return content

# =====================================================================
# STRUMENTI PER LA CATENA DI MONTAGGIO A ZERO DIFETTI
# =====================================================================

@mcp.tool()
def local_generate_skeleton(specifications_json: str) -> str:
    """
    PASSO 1 (GRATIS - OLLAMA 7B): Genera lo scheletro formale (file .hpp o classi Python con firme).
    Crea i punti di riferimento per i modelli programmatori senza sprecare token cloud.
    """
    sys_prompt = (
        "Sei uno Skeleton Architect. Genera SOLO lo scheletro strutturale del codice "
        "(header C++ o file Python con type annotations e 'raise NotImplementedError'). "
        "Non implementare gli algoritmi interni. Mantieni rigore assoluto su firme e allineamenti."
    )
    payload = {
        "model": ROSTER["local_scaffold"],
        "prompt": f"{sys_prompt}\n\nSpecifiche tecniche (JSON):\n{specifications_json}",
        "stream": False,
        "options": {"temperature": 0.1}
    }
    r = requests.post(OLLAMA_URL, json=payload, timeout=120)
    r.raise_for_status()
    return r.json().get("response", "")

@mcp.tool()
def cloud_infill_implementation(skeleton_and_contract: str) -> str:
    """
    PASSO 2 (LOW COST - QWEN3 30B): Riceve lo scheletro e il contratto, riempiendo solo la logica interna.
    Non tocca firme o interfacce predefinite.
    """
    sys_prompt = (
        "Sei il Senior Infiller. Ricevi uno scheletro strutturale e il contratto di funzionamento. "
        "Devi implementare TUTTI i corpi delle funzioni nel rispetto assoluto dei vincoli di memoria e firme. "
        "NON usare commenti // TODO o scorciatoie. Restituisci il codice completo pronto alla produzione."
    )
    return call_openrouter(ROSTER["worker"], skeleton_and_contract, sys_prompt, temperature=0.1, max_tokens=8192)

@mcp.tool()
def preflight_contract_check(contract_json: str, generated_code: str) -> str:
    """
    PASSO 3 (ULTRA-FAST - GEMINI FLASH): Controlla discrepanze prima della compilazione.
    Individua buffer non controllati, violazioni di tipi o firme alterate in 1 secondo.
    """
    sys_prompt = (
        "Sei il Controllore di Qualità di Pre-Flight. Verifica se il codice generato rispetta il contratto "
        "e i limiti di memoria. Rispondi 'APPROVED' se è impeccabile, oppure elenca in modo sintetico "
        "i problemi critici riscontrati."
    )
    prompt = f"CONTRATTO:\n{contract_json}\n\nCODICE PRODOTTO:\n{generated_code}"
    return call_openrouter(ROSTER["preflight"], prompt, sys_prompt, temperature=0.0, max_tokens=1024)

@mcp.tool()
def deep_reasoner_solve_crash(error_context_json: str) -> str:
    """
    PASSO 4 (DEBUG PROFONDO - DEEPSEEK R1): Risolve crash di memoria ASan o deadlock logici.
    Invocato SOLO se i test falliscono.
    """
    sys_prompt = (
        "Sei il Principal Systems & Memory Security Engineer. Analizza il crash nativo o la violazione di invarianti. "
        "Identifica l'errore logico o di puntatore e fornisci la correzione chirurgica in C++ o Python."
    )
    return call_deepseek(ROSTER["auditor"], error_context_json, sys_prompt, temperature=0.6, max_tokens=12000)

@mcp.tool()
def local_update_documentation(doc_payload_json: str) -> str:
    """
    PASSO 5 (GRATIS - OLLAMA 7B): Aggiorna roadmap, changelog e commenti sui pacchetti senza spendere token cloud.
    """
    payload = {
        "model": ROSTER["local_scaffold"],
        "prompt": f"Sei un Technical Writer. Aggiorna la documentazione/roadmap in Markdown basandoti su questi dati:\n{doc_payload_json}",
        "stream": False
    }
    r = requests.post(OLLAMA_URL, json=payload, timeout=120)
    r.raise_for_status()
    return r.json().get("response", "")

# =====================================================================
# REGISTRO DI STATO DEI CONTRATTI
# =====================================================================

VALID_STATES = ["DRAFT", "SKELETON_OK", "INFILLED", "PREFLIGHT_OK", "VERIFIED", "ASAN_VERIFIED", "TEST_VERIFIED", "MERGED", "BLOCKED", "DROPPED"]
DONE_STATES = ["VERIFIED", "ASAN_VERIFIED", "TEST_VERIFIED", "MERGED"]


@mcp.tool()
def update_contract_state(contract_id: str, new_state: str, metrics: str = "") -> str:
    """
    Aggiorna lo stato di un contratto in contracts/ledger.json, ricalcola la
    percentuale del suo Gate e rigenera docs/MASTER_ROADMAP.md in background
    con il 7B locale. Nessuna chiamata a pagamento.
    """
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

    done_count = sum(1 for c in owning_gate["contracts"] if c["status"] in DONE_STATES)
    owning_gate["completion_pct"] = round(100 * done_count / len(owning_gate["contracts"]))

    tmp_path = ledger_path.with_suffix(".json.tmp")
    try:
        with open(tmp_path, "w", encoding="utf-8") as f:
            json.dump(ledger, f, indent=2, ensure_ascii=False)
        os.replace(tmp_path, ledger_path)
    except Exception:
        if tmp_path.exists():
            tmp_path.unlink()
        return "ERROR: impossibile aggiornare il ledger"

    def regenerate_roadmap():
        try:
            prompt = (
                "Sei un Technical Writer. Traduci questo registro di contratti in una "
                "roadmap Markdown con una sezione per Gate, la percentuale di ogni Gate "
                "e una checklist dei contratti. Nessuna prosa introduttiva.\n\n"
                f"{json.dumps(ledger, ensure_ascii=False)}"
            )
            response = requests.post(
                OLLAMA_URL,
                json={"model": ROSTER["local_scaffold"], "prompt": prompt, "stream": False},
                timeout=180
            )
            response.raise_for_status()
            content = response.json().get("response", "")
            if content:
                with open(PROJECT_ROOT / "docs" / "MASTER_ROADMAP.md", "w", encoding="utf-8") as f:
                    f.write(content)
        except Exception:
            pass

    threading.Thread(target=regenerate_roadmap, daemon=True).start()

    pct = owning_gate["completion_pct"]
    return f"[CID: {contract_id}] [STATE: {new_state}] gate {gate_number} -> {pct}% | roadmap in rigenerazione locale"

if __name__ == "__main__":
    mcp.run(transport="stdio")