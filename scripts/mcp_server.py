import os
import json
import re
import requests
from dotenv import load_dotenv
from pathlib import Path
from mcp.server.fastmcp import FastMCP

PROJECT_ROOT = Path(__file__).resolve().parent.parent
load_dotenv(PROJECT_ROOT / ".env")

mcp = FastMCP("orchestrator")

OPENROUTER_API_KEY = os.getenv("OPENROUTER_API_KEY", "")
OPENROUTER_URL = os.getenv("OPENROUTER_URL", "https://openrouter.ai/api/v1/chat/completions")
OLLAMA_URL = os.getenv("OLLAMA_LOCAL_URL", "http://localhost:11434/api/generate")

ROSTER = {
    "worker": "qwen/qwen3-coder-30b-a3b-instruct",
    "auditor": "deepseek/deepseek-r1",
    "preflight": "google/gemini-2.0-flash-001",
    "local_scaffold": "qwen2.5-coder:7b"
}

def call_openrouter(model_id: str, prompt: str, system_prompt: str, temperature: float = 0.1, max_tokens: int = 4096) -> str:
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
    return call_openrouter(ROSTER["auditor"], error_context_json, sys_prompt, temperature=0.6, max_tokens=12000)

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

if __name__ == "__main__":
    mcp.run(transport="stdio")