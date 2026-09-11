import os
import json
import re
import requests
import threading
from datetime import date
from dotenv import load_dotenv
from pathlib import Path
from mcp.server.fastmcp import FastMCP
from typing import List, Dict, Tuple, Sequence, Callable

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

# =====================================================================
# REGISTRO DI STATO DEI CONTRATTI
# =====================================================================

VALID_STATES = ["DRAFT", "SKELETON_OK", "INFILLED", "PREFLIGHT_OK", "VERIFIED", "ASAN_VERIFIED", "TEST_VERIFIED", "MERGED", "BLOCKED", "DROPPED"]
DONE_STATES = ["VERIFIED", "ASAN_VERIFIED", "TEST_VERIFIED", "MERGED"]

# =====================================================================
# REGISTRO DEI COSTI (stesso file di scripts/code_agent.py)
# =====================================================================

LEDGER = PROJECT_ROOT / "data" / "ai_task_ledger.jsonl"


def record(entry: dict) -> None:
    """Aggiunge una riga al registro dei costi, nello stesso formato usato da
    scripts/code_agent.py, cosi' le chiamate fatte da questi tool non restano
    invisibili al resto della catena."""
    LEDGER.parent.mkdir(parents=True, exist_ok=True)
    with LEDGER.open("a", encoding="utf-8") as handle:
        handle.write(json.dumps(entry, ensure_ascii=False) + "\n")


# Il campo "usage" dell'ultima risposta HTTP letta da call_openrouter o
# call_deepseek in QUESTO thread: un dizionario per thread, non uno globale,
# cosi' due tool invocati su thread diversi non si scambiano i token dell'altro.
_STATO_CHIAMATA = threading.local()


def _costo_reale_usd(model_id: str) -> float:
    """Costo reale in dollari dell'ultima chiamata di questo thread a
    call_openrouter o call_deepseek, calcolato sui token effettivi che il
    provider ha restituito nel campo "usage" della risposta (prompt_tokens,
    completion_tokens), non su una stima. Zero se il modello non e' nel
    listino (per esempio i modelli Ollama locali, sempre gratuiti) o se non
    e' ancora avvenuta nessuna chiamata su questo thread."""
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

# =====================================================================
# STRUMENTI PER LA CATENA DI MONTAGGIO A ZERO DIFETTI
# =====================================================================

_ESTENSIONI_LINGUAGGIO = {".cs": "csharp", ".cpp": "cpp", ".hpp": "cpp", ".h": "cpp", ".py": "python"}

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
    "del contratto. Individualo dall'estensione dichiarata (.cs = C#, .cpp/.hpp/.h = C++, "
    ".py = Python) e genera SOLO lo scheletro strutturale in quel linguaggio, con firme esatte "
    "e corpi non implementati (throw new NotImplementedException(); in C#, raise "
    "NotImplementedError in Python, nessun corpo in un header C++). Se il linguaggio non e' "
    "davvero determinabile, fermati e dichiaralo invece di indovinare."
)


def _rileva_linguaggio_bersaglio(specifications_json: str) -> str | None:
    """Determina il linguaggio del file bersaglio dal contratto di Fase 1. Cerca prima le
    chiavi note ('file', 'target_file', 'file_bersaglio') in un JSON valido, poi un percorso
    con estensione riconosciuta nel testo grezzo. Restituisce 'csharp', 'cpp', 'python' o
    None se non determinabile: un contratto senza estensione riconoscibile non deve produrre
    silenziosamente uno scheletro nel linguaggio sbagliato."""
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
    """Costruisce il system prompt per local_generate_skeleton, specifico per il linguaggio
    rilevato nel contratto. Funzione pura, separata dalla chiamata di rete, cosi' la
    rilevazione del linguaggio resta testabile senza Ollama."""
    linguaggio = _rileva_linguaggio_bersaglio(specifications_json)
    istruzioni = _ISTRUZIONI_LINGUAGGIO.get(linguaggio, _ISTRUZIONI_LINGUAGGIO_IGNOTO)
    return (
        "Sei uno Skeleton Architect. " + istruzioni + " Mantieni rigore assoluto su firme, "
        "tipi e allineamenti; nessuna API inventata, nessun segnaposto vago."
    )


@mcp.tool()
def local_generate_skeleton(specifications_json: str) -> str:
    """
    PASSO 1 (GRATIS - OLLAMA 7B): Genera lo scheletro formale (C#, C++ o Python, in base al
    file bersaglio dichiarato nel contratto) con le firme.
    Crea i punti di riferimento per i modelli programmatori senza sprecare token cloud.
    """
    sys_prompt = _prompt_scheletro(specifications_json)
    payload = {
        "model": ROSTER["local_scaffold"],
        "prompt": f"{sys_prompt}\n\nSpecifiche tecniche (JSON):\n{specifications_json}",
        "stream": False,
        "options": {"temperature": 0.1}
    }
    r = requests.post(OLLAMA_URL, json=payload, timeout=120)
    r.raise_for_status()
    risposta = r.json().get("response", "")
    record({
        "task_id": "local_generate_skeleton", "model": ROSTER["local_scaffold"],
        "calls": 1, "estimated_cost_usd": 0.0, "status": "completed",
        "file": "mcp_tool", "words": len(risposta.split()),
    })
    return risposta

@mcp.tool()
def cloud_infill_implementation(skeleton_and_contract: str) -> str:
    """
    PASSO 2 (LOW COST - QWEN3 30B): Riceve lo scheletro e il contratto, riempiendo solo la logica interna.
    Non tocca firme o interfacce predefinite.
    """
    sys_prompt = (
        "Sei il Senior Infiller. Ricevi uno scheletro strutturale e il contratto di funzionamento. "
        "Devi implementare TUTTI i corpi delle funzioni nel rispetto assoluto dei vincoli di memoria e firme. "
        "NON usare commenti // TODO o scorciatoche. Restituisci il codice completo pronto alla produzione."
    )
    testo = call_openrouter(ROSTER["worker"], skeleton_and_contract, sys_prompt, temperature=0.1, max_tokens=8192)
    record({
        "task_id": "cloud_infill_implementation", "model": ROSTER["worker"],
        "calls": 1, "estimated_cost_usd": _costo_reale_usd(ROSTER["worker"]),
        "status": "completed", "file": "mcp_tool",
        "usage": getattr(_STATO_CHIAMATA, "usage", {}),
    })
    return testo

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
    testo = call_openrouter(ROSTER["preflight"], prompt, sys_prompt, temperature=0.0, max_tokens=1024)
    record({
        "task_id": "preflight_contract_check", "model": ROSTER["preflight"],
        "calls": 1, "estimated_cost_usd": _costo_reale_usd(ROSTER["preflight"]),
        "status": "completed", "file": "mcp_tool",
        "usage": getattr(_STATO_CHIAMATA, "usage", {}),
    })
    return testo

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
    testo = call_deepseek(ROSTER["auditor"], error_context_json, sys_prompt, temperature=0.6, max_tokens=12000)
    record({
        "task_id": "deep_reasoner_solve_crash", "model": ROSTER["auditor"],
        "calls": 1, "estimated_cost_usd": _costo_reale_usd(ROSTER["auditor"]),
        "status": "completed", "file": "mcp_tool",
        "usage": getattr(_STATO_CHIAMATA, "usage", {}),
    })
    return testo

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
    risposta = r.json().get("response", "")
    record({
        "task_id": "local_update_documentation", "model": ROSTER["local_scaffold"],
        "calls": 1, "estimated_cost_usd": 0.0, "status": "completed",
        "file": "mcp_tool", "words": len(risposta.split()),
    })
    return risposta

def render_roadmap_markdown(ledger: dict) -> str:
    """Deterministic Markdown rendering of the contract ledger. No model call,
    no paraphrasing: every line comes verbatim from a ledger field, so it
    cannot misreport a fact the ledger itself doesn't contain.

    Replaces a prior implementation that asked a local 7B model to "translate"
    the raw ledger JSON into prose: it routinely reported already-closed
    questions as still open and, at least once, mixed a fragment of its own
    note fields into an unrelated contract's row.
    """
    lines: list[str] = []
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
            for field in ("updated", "metrics", "note", "blocker"):
                value = contract.get(field)
                if value:
                    lines.append(f"  - {field}: {value}")

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


@mcp.tool()
def update_contract_state(contract_id: str, new_state: str, metrics: str = "") -> str:
    """
    Aggiorna lo stato di un contratto in contracts/ledger.json, ricalcola la
    percentuale del suo Gate e rigenera docs/MASTER_ROADMAP.md in background
    con la funzione deterministica render_roadmap_markdown(ledger): nessun
    modello, nessuna chiamata di rete, nessuna chiamata a pagamento.
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

    # Calcolo della percentuale di completamento del gate
    # Il denominatore: numero di contratti del gate che NON sono in stato DROPPED
    non_dropped_contracts = [c for c in owning_gate["contracts"] if c["status"] != "DROPPED"]
    total_non_dropped = len(non_dropped_contracts)
    
    # Il numeratore: numero di contratti con stato in DONE_STATES
    done_count = sum(1 for c in owning_gate["contracts"] if c["status"] in DONE_STATES)
    
    if total_non_dropped == 0:
        # Ogni contratto del gate è abbandonato oppure il gate è vuoto
        completion_pct = 100
    else:
        completion_pct = round(100 * done_count / total_non_dropped)
    
    owning_gate["completion_pct"] = completion_pct

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
            content = render_roadmap_markdown(ledger)
            with open(PROJECT_ROOT / "docs" / "MASTER_ROADMAP.md", "w", encoding="utf-8") as f:
                f.write(content)
        except Exception:
            pass

    threading.Thread(target=regenerate_roadmap, daemon=True).start()

    pct = owning_gate["completion_pct"]
    return f"[CID: {contract_id}] [STATE: {new_state}] gate {gate_number} -> {pct}% | roadmap in rigenerazione locale"

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
    corpo = r.json()
    _STATO_CHIAMATA.usage = corpo.get("usage", {})
    return corpo["choices"][0]["message"]["content"]

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
    corpo = r.json()
    _STATO_CHIAMATA.usage = corpo.get("usage", {})
    message = corpo["choices"][0]["message"]
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

def modelli_gratuiti() -> list[str]:
    """Legge scripts/free_roster.json e restituisce gli identificativi validati, ordinati per latenza misurata crescente."""
    roster_path = PROJECT_ROOT / "scripts" / "free_roster.json"
    try:
        with open(roster_path, "r", encoding="utf-8") as f:
            roster = json.load(f)
    except (FileNotFoundError, json.JSONDecodeError):
        return []
    
    validati = roster.get("validati", [])
    scartati = roster.get("scartati", {})
    
    # Filtra i modelli validati e non scartati
    modelli = [m["id"] for m in validati if m["id"] not in scartati]
    
    # Ordina per sec_medi crescente
    modelli.sort(key=lambda m: next((item["sec_medi"] for item in validati if item["id"] == m), float('inf')))
    
    # Assicura che i modelli groq: vengano prima di tutti gli altri
    groq_modelli = [m for m in modelli if m.startswith("groq:")]
    altri_modelli = [m for m in modelli if not m.startswith("groq:")]
    
    return groq_modelli + altri_modelli

def giudizio_infill(scheletro_e_contratto: str) -> Callable[[str], list[str]]:
    """Riceve in una sola stringa lo scheletro e il contratto, come li riceve il tool cloud_infill_implementation."""
    def giudice(codice_candidato: str) -> list[str]:
        difetti = []
        
        # 1) Controllo sintassi
        try:
            compile(codice_candidato, '<string>', 'exec')
        except SyntaxError as e:
            return [f"sintassi non valida: {str(e)}"]
        
        # Estrai firme dallo scheletro
        try:
            scheletro_firme = set()
            lines = scheletro_e_contratto.split('\n')
            for line in lines:
                if line.strip().startswith('def '):
                    # Estrai nome e parametri della funzione
                    match = re.match(r'def\s+(\w+)\s*\(([^)]*)\)', line)
                    if match:
                        nome = match.group(1)
                        parametri = match.group(2)
                        scheletro_firme.add(f"{nome}({parametri})")
        except Exception:
            # Se non riesce a estrarre le firme, continua con i controlli successivi
            scheletro_firme = set()
        
        # 2) Controllo firme alterate o allargate
        if scheletro_firme:
            codice_lines = codice_candidato.split('\n')
            for line in codice_lines:
                if line.strip().startswith('def '):
                    match = re.match(r'def\s+(\w+)\s*\(([^)]*)\)', line)
                    if match:
                        nome = match.group(1)
                        parametri = match.group(2)
                        firma = f"{nome}({parametri})"
                        if firma not in scheletro_firme:
                            # Verifica se è un allargamento (aggiunta di parametro con valore predefinito)
                            # Cerca se la firma originale è una sottostringa della firma attuale
                            found = False
                            for orig_firma in scheletro_firme:
                                orig_nome = orig_firma.split('(')[0]
                                if orig_nome == nome:
                                    found = True
                                    break
                            if found:
                                # Se la firma originale è una sottosequenza, controlla se è un allargamento
                                # Per semplicità, consideriamo allargamento se il codice ha più parametri
                                orig_params = [p.strip() for p in orig_firma.split('(')[1].rstrip(')').split(',') if p.strip()]
                                new_params = [p.strip() for p in parametri.split(',') if p.strip()]
                                if len(new_params) > len(orig_params):
                                    # Controlla se i parametri iniziali sono uguali
                                    if all(p1 == p2 for p1, p2 in zip(orig_params, new_params)):
                                        # Se tutti i parametri iniziali sono uguali, è un allargamento
                                        difetti.append(f"firma alterata: {nome} ha parametri aggiunti")
                            else:
                                difetti.append(f"firma alterata: {nome} non presente nello scheletro")
        
        # 3) Controllo corpi che si limitano a sollevare NotImplementedError
        lines = codice_candidato.split('\n')
        i = 0
        while i < len(lines):
            line = lines[i]
            if line.strip().startswith('def '):
                # Trova il nome della funzione
                match = re.match(r'def\s+(\w+)\s*\(([^)]*)\)', line)
                if match:
                    nome_funzione = match.group(1)
                    # Cerca il corpo della funzione
                    i += 1
                    while i < len(lines):
                        current_line = lines[i].strip()
                        if current_line == 'raise NotImplementedError':
                            difetti.append(f"corpo della funzione '{nome_funzione}' solleva NotImplementedError")
                            break
                        elif current_line == '' or current_line.startswith('#'):
                            i += 1
                            continue
                        elif current_line.startswith('def ') or current_line.startswith('class '):
                            break
                        else:
                            break
            i += 1
        
        # 4) Controllo segnaposto
        segnaposto_patterns = [r'TODO', r'FIXME', r'XXX']
        for pattern in segnaposto_patterns:
            if re.search(pattern, codice_candidato, re.IGNORECASE):
                difetti.append(f"contiene segnaposto '{pattern}'")
        
        return difetti
    
    return giudice

def giudizio_preflight(risposta: str) -> list[str]:
    """Giudica la risposta di un pre-flight."""
    risposta = risposta.strip()
    
    # Risposta vuota o composta di soli spazi
    if not risposta:
        return ["risposta vuota"]
    
    # Approvazione
    if "APPROVED" in risposta:
        return []
    
    # Controllo se è un elenco di difetti (righe che iniziano con trattino)
    lines = risposta.split('\n')
    if any(line.strip().startswith('-') for line in lines):
        return []
    
    # Nessun verdetto
    return ["risposta non contiene un verdetto valido: né APPROVED né elenco di difetti"]

# =====================================================================
# IMPLEMENTAZIONE DELLA RETE DELLA CASCATA
# =====================================================================

# Registro delle pause condiviso
PAUSA = None
try:
    import free_first
    PAUSA = free_first.Pausa()
except ImportError:
    # Fallback se free_first non è disponibile
    class Pausa:
        def __init__(self):
            self.pausa = 0.0
        def __call__(self, secondi):
            self.pausa = secondi
    PAUSA = Pausa()

POLITICHE = {
    "cloud_infill_implementation": {
        "cascata": True,
        "budget_secondi": 30.0,
        "pagato": "openrouter/gpt-4-turbo"
    },
    "preflight_contract_check": {
        "cascata": True,
        "budget_secondi": 2.0,
        "pagato": "openrouter/gpt-4-turbo"
    },
    "deep_reasoner_solve_crash": {
        "cascata": False,
        "budget_secondi": 0.0,
        "pagato": "deepseek/deepseek-r1"
    }
}

def chiama_groq(modello: str, messaggi: List[Dict[str, str]], max_tokens: int, temperatura: float) -> Tuple[str, str]:
    """Chiama UN SOLO modello su api.groq.com, togliendo il prefisso groq: dall'identificativo prima di mandarlo sul filo. Restituisce la coppia (testo, identificativo del modello che ha risposto). Solleva su qualunque guasto: e' il chiamante che decide se proseguire. Passa la risposta a controlla_quota prima di leggerne il corpo."""
    # Rimuovi il prefisso groq: se presente
    modello_senza_prefisso = modello[len("groq:"):] if modello.startswith("groq:") else modello
    
    # Configura l'URL e le intestazioni per Groq
    GROQ_URL = "https://api.groq.com/openai/v1/chat/completions"
    headers = {
        "Authorization": f"Bearer {os.getenv('GROQ_API_KEY', '')}",
        "Content-Type": "application/json"
    }
    
    payload = {
        "model": modello_senza_prefisso,
        "messages": messaggi,
        "max_tokens": max_tokens,
        "temperature": temperatura
    }
    
    r = requests.post(GROQ_URL, headers=headers, json=payload, timeout=120)
    
    # Controlla la quota prima di leggere il corpo
    controlla_quota(r)
    
    r.raise_for_status()
    
    testo = r.json()["choices"][0]["message"]["content"]
    return (testo, modello)

def chiama_openrouter(gruppo: Sequence[str], messaggi: List[Dict[str, str]], max_tokens: int, temperatura: float) -> Tuple[str, str]:
    """Chiama OpenRouter una volta sola per l'intero gruppo, usando l'array models del corpo, che accetta al massimo tre elementi, e il lucchetto provider.max_price a zero su prompt e completion. Restituisce (testo, modello che ha effettivamente risposto, letto dal campo model della risposta). Nessun identificativo con prefisso groq: puo' essere passato qui. Solleva su guasto. Passa la risposta a controlla_quota."""
    # Assicurati che il gruppo non superi 3 modelli
    if len(gruppo) > 3:
        gruppo = gruppo[:3]
    
    headers = {
        "Authorization": f"Bearer {OPENROUTER_API_KEY}",
        "Content-Type": "application/json",
        "HTTP-Referer": "https://github.com/NosAiProject",
        "X-Title": "NosAi Zero-Waste Assembly"
    }
    
    payload = {
        "models": list(gruppo),
        "messages": messaggi,
        "temperature": temperatura,
        "max_tokens": max_tokens,
        "provider": {
            "max_price": 0.0
        }
    }
    
    r = requests.post(OPENROUTER_URL, headers=headers, json=payload, timeout=120)
    
    # Controlla la quota prima di leggere il corpo
    controlla_quota(r)
    
    r.raise_for_status()
    
    response_data = r.json()
    testo = response_data["choices"][0]["message"]["content"]
    modello_risposto = response_data["choices"][0]["model"]
    
    return (testo, modello_risposto)

def chiama_gruppo(gruppo: Sequence[str], messaggi: List[Dict[str, str]], max_tokens: int, temperatura: float) -> Tuple[str, str]:
    """Due protocolli in un ordine vincolante. Primo: i modelli con prefisso groq:, uno per uno e nell'ordine in cui compaiono nel gruppo, tramite chiama_groq; il primo che risponde ferma tutto e OpenRouter non viene interpellato affatto. Secondo: i modelli restanti, cioe' quelli senza prefisso groq:, in UNA sola chiamata a chiama_openrouter. Se nessuno risponde solleva. Va chiamato tramite la ricerca del nome a livello di modulo, perche' i test sostituiscono chiama_groq e chiama_openrouter con monkeypatch."""
    # Separa i modelli groq: da quelli senza prefisso
    groq_modelli = [m for m in gruppo if m.startswith("groq:")]
    altri_modelli = [m for m in gruppo if not m.startswith("groq:")]
    
    # Primo protocollo: chiama i modelli groq: uno per uno
    for modello in groq_modelli:
        try:
            testo, modello_effettivo = chiama_groq(modello, messaggi, max_tokens, temperatura)
            return (testo, modello_effettivo)
        except Exception:
            # Se il modello non risponde, passa al successivo
            continue
    
    # Secondo protocollo: chiama tutti gli altri modelli in un'unica chiamata
    if altri_modelli:
        try:
            testo, modello_effettivo = chiama_openrouter(altri_modelli, messaggi, max_tokens, temperatura)
            return (testo, modello_effettivo)
        except Exception:
            # Se nessun modello risponde, solleva l'eccezione
            raise
    
    # Se non ci sono modelli da chiamare, solleva un'eccezione
    raise RuntimeError("Nessun modello disponibile per la chiamata")

def controlla_quota(risposta) -> None:
    """Legge lo stato di una risposta HTTP. Se e' 429 solleva free_first.LimiteRaggiunto con il campo attendi valorizzato dall'header retry-after convertito in float, oppure free_first.PAUSA_PREDEFINITA quando l'header manca. Per qualunque altro stato restituisce None. Una quota esaurita non e' un guasto: e' un'attesa."""
    if risposta.status_code == 429:
        retry_after = risposta.headers.get("retry-after")
        if retry_after is not None:
            attendi = float(retry_after)
        else:
            try:
                import free_first
                attendi = free_first.PAUSA_PREDEFINITA
            except ImportError:
                attendi = 10.0  # PAUSA_PREDEFINITA
        try:
            import free_first
            raise free_first.LimiteRaggiunto("Limite raggiunto", attendi=attendi)
        except ImportError:
            # Fallback se free_first non è disponibile
            raise RuntimeError(f"Limite raggiunto, attendere {attendi} secondi")

if __name__ == "__main__":
    mcp.run(transport="stdio")
