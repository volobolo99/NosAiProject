import os
import json
import re
import requests
import threading
from datetime import date
from dotenv import load_dotenv
from pathlib import Path
from mcp.server.fastmcp import FastMCP
from typing import List, Dict, Tuple, Sequence

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
        "NON usare commenti // TODO o scorciatoche. Restituisci il codice completo pronto alla produzione."
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

def giudizio_infill(scheletro_e_contratto: str) -> callable[[str], list[str]]:
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
