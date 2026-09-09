import json
import os
import pathlib
import subprocess
import time
import uuid
from mcp.server.fastmcp import FastMCP
import requests

# Inizializzazione Server MCP
mcp = FastMCP("WorkerOrchestrator")

# Percorsi e file di log traffico
BASE_DIR = pathlib.Path(__file__).resolve().parent
TRAFFIC_LOG = BASE_DIR / "data" / "traffic" / "events.jsonl"
CHAT_LOG = BASE_DIR / "data" / "traffic" / "chats.jsonl"
CHAT_TEXT_LIMIT = 20000
ENV_FILE = BASE_DIR / ".env"

# Caricamento automatico delle variabili da .env se presente
if ENV_FILE.exists():
    with open(ENV_FILE, "r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line and not line.startswith("#") and "=" in line:
                key, val = line.split("=", 1)
                os.environ.setdefault(key.strip(), val.strip().strip('"').strip("'"))

# Identificatori dei Modelli
MODEL_LOCAL_7B = "qwen2.5-coder:7b"            # Locale via Ollama (RTX 5060)
MODEL_CODER_32B = "qwen/qwen3-32b"             # Cloud via OpenRouter
MODEL_DEEPSEEK_CHAT = "deepseek-chat"          # Cloud via DeepSeek API ufficiale (V3 / V4)
MODEL_DEEPSEEK_DEBUG = "deepseek-reasoner"      # Cloud via DeepSeek API ufficiale (R1 Reasoning)

# Endpoint di rete
OLLAMA_LOCAL_URL = "http://localhost:11434/api/generate"
OPENROUTER_URL = "https://openrouter.ai/api/v1/chat/completions"
DEEPSEEK_URL = "https://api.deepseek.com/chat/completions"
SYNC_CHANNEL = "nosai-worker-sync-volob"

# Chiavi API
OPENROUTER_API_KEY = os.environ.get("OPENROUTER_API_KEY", "")
DEEPSEEK_API_KEY = os.environ.get("DEEPSEEK_API_KEY", "")


def log_traffic(worker: str, model: str, status: str, duration: float, prompt_len: int,
                response_len: int, tokens: int = 0, usage: dict | None = None) -> str:
    """Registra l'evento per il traffic inspector in data/traffic/events.jsonl.

    I token non sono un dettaglio del registro: la console li somma per agente,
    e una delega che non li dichiara sparisce dal conto. Restituisce l'id
    dell'evento, che log_chat riusa per legare il testo al passaggio.
    """
    event_id = str(uuid.uuid4())
    try:
        TRAFFIC_LOG.parent.mkdir(parents=True, exist_ok=True)
        event = {
            "id": event_id,
            "timestamp": time.time(),
            "channel": SYNC_CHANNEL,
            "worker": worker,
            "model": model,
            "status": status,
            "duration_sec": round(duration, 3),
            "prompt_len": prompt_len,
            "response_len": response_len,
            "tokens": tokens,
        }
        event.update(usage or {})
        with open(TRAFFIC_LOG, "a", encoding="utf-8") as f:
            f.write(json.dumps(event) + "\n")
    except Exception:
        pass
    return event_id


def log_chat(event_id: str, worker: str, model: str, prompt: str, response: str,
             ok: bool, duration: float, tokens: int = 0, usage: dict | None = None,
             error: str = "") -> None:
    """Registra la conversazione: cosa e' stato chiesto e cosa e' tornato.

    Sta in un file separato da events.jsonl perche' quella e' la coda che la
    console segue riga per riga, e un prompt da 12 KB dentro quella coda
    rallenterebbe il flusso in diretta. Testi tagliati a CHAT_TEXT_LIMIT.
    """
    def clip(text: str) -> str:
        text = text or ""
        if len(text) <= CHAT_TEXT_LIMIT:
            return text
        return text[:CHAT_TEXT_LIMIT] + f"{chr(10)}{chr(10)}[...troncato: {len(text) - CHAT_TEXT_LIMIT} caratteri]"

    try:
        CHAT_LOG.parent.mkdir(parents=True, exist_ok=True)
        record = {
            "id": event_id,
            "at": time.time() - duration,
            "endedAt": time.time(),
            "agent": worker,
            "agentName": worker,
            "model": model,
            "op": "delega MCP",
            "ok": ok,
            "durationMs": int(duration * 1000),
            "error": error[:400],
            "prompt": clip(prompt),
            "response": clip(response),
            "tokens": tokens,
            "free": worker == "ollama-local",
        }
        record.update(usage or {})
        with open(CHAT_LOG, "a", encoding="utf-8") as f:
            f.write(json.dumps(record, ensure_ascii=False) + chr(10))
    except Exception:
        pass


@mcp.tool()
def delegate_to_local_7b(prompt: str) -> str:
    """Esegue task veloci e compatti sulla GPU locale (RTX 5060) a costo zero tramite Ollama.
    Ideale per: pre-filtraggio log, estrazione firme o formattazione.
    """
    start_t = time.time()
    try:
        res = requests.post(
            OLLAMA_LOCAL_URL,
            json={"model": MODEL_LOCAL_7B, "prompt": prompt, "stream": False},
            timeout=120,
        )
        duration = time.time() - start_t
        if res.status_code == 200:
            data = res.json()
            text = data.get("response", "")
            usage = {
                "promptTokens": data.get("prompt_eval_count", 0),
                "completionTokens": data.get("eval_count", 0),
                "channel": "LOCAL_5060",
                "free": True,
            }
            tokens = usage["promptTokens"] + usage["completionTokens"]
            event_id = log_traffic("ollama-local", MODEL_LOCAL_7B, "success", duration,
                                   len(prompt), len(text), tokens, usage)
            log_chat(event_id, "ollama-local", MODEL_LOCAL_7B, prompt, text, True,
                     duration, tokens, usage)
            return text
        else:
            err = f"Ollama HTTP {res.status_code}: {res.text}"
            event_id = log_traffic("ollama-local", MODEL_LOCAL_7B, "error", duration,
                                   len(prompt), len(err))
            log_chat(event_id, "ollama-local", MODEL_LOCAL_7B, prompt, "", False,
                     duration, error=err)
            return err
    except Exception as e:
        duration = time.time() - start_t
        event_id = log_traffic("ollama-local", MODEL_LOCAL_7B, "error", duration, len(prompt), 0)
        log_chat(event_id, "ollama-local", MODEL_LOCAL_7B, prompt, "", False,
                 duration, error=str(e))
        return f"Errore connessione Ollama locale: {e}"


@mcp.tool()
def delegate_to_qwen_coder(task_description: str, existing_code: str = "") -> str:
    """Generatore primario di codice C# e unit test xUnit tramite Qwen3 32B su OpenRouter.
    Usa questo tool per la scrittura rigorosa del codice su specifiche di Claude.
    """
    start_t = time.time()
    prompt_full = f"Contesto:\n{existing_code}\n\nTask:\n{task_description}"
    headers = {
        "Authorization": f"Bearer {OPENROUTER_API_KEY}",
        "Content-Type": "application/json",
    }
    payload = {
        "model": MODEL_CODER_32B,
        "messages": [
            {
                "role": "system",
                "content": "Sei un Senior C#/.NET Software Engineer. Scrivi solo codice C# pulito, solido e compilabile.",
            },
            {"role": "user", "content": prompt_full},
        ],
        "temperature": 0.2,
    }

    try:
        res = requests.post(OPENROUTER_URL, headers=headers, json=payload, timeout=120)
        duration = time.time() - start_t
        if res.status_code == 200:
            data = res.json()
            content = data["choices"][0]["message"]["content"]
            api_usage = data.get("usage") or {}
            usage = {
                "promptTokens": api_usage.get("prompt_tokens", 0),
                "completionTokens": api_usage.get("completion_tokens", 0),
                "channel": "OPENROUTER_32B",
                "free": False,
            }
            tokens = api_usage.get("total_tokens", 0)
            event_id = log_traffic("openrouter-qwen", MODEL_CODER_32B, "success", duration,
                                   len(prompt_full), len(content), tokens, usage)
            log_chat(event_id, "openrouter-qwen", MODEL_CODER_32B, prompt_full, content,
                     True, duration, tokens, usage)
            return content
        else:
            err = f"OpenRouter HTTP {res.status_code}: {res.text}"
            event_id = log_traffic("openrouter-qwen", MODEL_CODER_32B, "error", duration,
                                   len(prompt_full), len(err))
            log_chat(event_id, "openrouter-qwen", MODEL_CODER_32B, prompt_full, "", False,
                     duration, error=err)
            return err
    except Exception as e:
        duration = time.time() - start_t
        event_id = log_traffic("openrouter-qwen", MODEL_CODER_32B, "error", duration,
                               len(prompt_full), 0)
        log_chat(event_id, "openrouter-qwen", MODEL_CODER_32B, prompt_full, "", False,
                 duration, error=str(e))
        return f"Errore chiamata OpenRouter Qwen32B: {e}"


@mcp.tool()
def delegate_to_deepseek(prompt: str, mode: str = "chat") -> str:
    """Esegue richieste tramite l'API ufficiale DeepSeek.
    Parametro 'mode':
      - 'chat': DeepSeek-Chat (V3/V4 veloce per refactoring e compiti massivi)
      - 'reasoner': DeepSeek-Reasoner (R1 per errori di compilazione, collisioni LINQ e fallimenti di test)
    """
    start_t = time.time()
    model = MODEL_DEEPSEEK_DEBUG if mode == "reasoner" else MODEL_DEEPSEEK_CHAT
    headers = {
        "Authorization": f"Bearer {DEEPSEEK_API_KEY}",
        "Content-Type": "application/json",
    }
    payload = {
        "model": model,
        "messages": [
            {"role": "system", "content": "Sei un programmatore esperto in architettura C# e debugging .NET."},
            {"role": "user", "content": prompt},
        ],
    }

    try:
        res = requests.post(DEEPSEEK_URL, headers=headers, json=payload, timeout=180)
        duration = time.time() - start_t
        if res.status_code == 200:
            data = res.json()
            content = data["choices"][0]["message"]["content"]
            api_usage = data.get("usage") or {}
            details = api_usage.get("completion_tokens_details") or {}
            usage = {
                "promptTokens": api_usage.get("prompt_tokens", 0),
                "completionTokens": api_usage.get("completion_tokens", 0),
                "reasoningTokens": details.get("reasoning_tokens", 0),
                "cacheHitTokens": api_usage.get("prompt_cache_hit_tokens", 0),
                "servedModel": data.get("model", "unknown"),
                "channel": "DEEPSEEK",
                "free": False,
            }
            tokens = api_usage.get("total_tokens", 0)
            event_id = log_traffic("deepseek-official", model, "success", duration,
                                   len(prompt), len(content), tokens, usage)
            log_chat(event_id, "deepseek-official", model, prompt, content, True,
                     duration, tokens, usage)
            return content
        else:
            err = f"DeepSeek HTTP {res.status_code}: {res.text}"
            event_id = log_traffic("deepseek-official", model, "error", duration,
                                   len(prompt), len(err))
            log_chat(event_id, "deepseek-official", model, prompt, "", False,
                     duration, error=err)
            return err
    except Exception as e:
        duration = time.time() - start_t
        event_id = log_traffic("deepseek-official", model, "error", duration, len(prompt), 0)
        log_chat(event_id, "deepseek-official", model, prompt, "", False,
                 duration, error=str(e))
        return f"Errore chiamata DeepSeek API: {e}"


@mcp.tool()
def run_dotnet_tests(test_project_path: str = "tests/NosAi.Runtime.Tests") -> str:
    """Esegue 'dotnet test' e restituisce l'output dettagliato per verificare lo stato della suite."""
    try:
        cmd = ["dotnet", "test", test_project_path, "-c", "Release", "--nologo", "-v", "minimal"]
        result = subprocess.run(cmd, capture_output=True, text=True, timeout=120)
        output = result.stdout + "\n" + result.stderr
        status = "SUPERATO" if result.returncode == 0 else "FALLITO"
        return f"Esito Test: {status}\n\nOutput:\n{output}"
    except Exception as e:
        return f"Errore esecuzione dotnet test: {e}"


if __name__ == "__main__":
    mcp.run()