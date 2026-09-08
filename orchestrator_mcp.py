import json
import os
import pathlib
import time
import uuid
from mcp.server.fastmcp import FastMCP
import requests

mcp = FastMCP("WorkerOrchestrator")

OLLAMA_LOCAL_URL = "http://localhost:11434/api/generate"
DEEPSEEK_URL = "https://api.deepseek.com/v1/chat/completions"
SYNC_CHANNEL = "nosai-worker-sync-volob"

# Il flusso che l'ispettore mostra in diretta (tools/traffic_inspector).
# Una riga JSON per passaggio: la pagina segue questo file, non i worker, cosi'
# vede anche le chiamate che non lasciano traccia altrove (DeepSeek non ha un
# endpoint di stato, e Ollama non conta le richieste).
TRAFFIC_LOG = pathlib.Path(__file__).resolve().parent / "data" / "traffic" / "events.jsonl"


def _emit(event: dict) -> None:
  """Scrive un passaggio nel registro del traffico, senza mai far fallire una delega."""
  try:
    TRAFFIC_LOG.parent.mkdir(parents=True, exist_ok=True)
    with TRAFFIC_LOG.open("a", encoding="utf-8") as handle:
      handle.write(json.dumps(event, ensure_ascii=False) + "\n")
  except OSError:
    pass


class _Trace:
  """Un passaggio verso un worker: inizio subito visibile, esito quando arriva."""

  def __init__(self, agent: str, agent_name: str, model: str, op: str):
    self.agent = agent
    self.agent_name = agent_name
    self.model = model
    self.op = op
    self.started = time.time()
    # Lo stesso id sull'inizio e sulla fine: la pagina aggiorna la riga che
    # sta gia' mostrando invece di aggiungerne una seconda per la stessa
    # chiamata. Senza, una delega lunga compare due volte nel flusso.
    self.id = uuid.uuid4().hex[:12]
    _emit({
        "id": self.id,
        "at": self.started,
        "agent": agent,
        "agentName": agent_name,
        "model": model,
        "op": op,
        "phase": "start",
    })

  def done(self, ok: bool, tokens: int = 0, error: str = "") -> None:
    _emit({
        "id": self.id,
        "at": time.time(),
        "agent": self.agent,
        "agentName": self.agent_name,
        "model": self.model,
        "op": self.op,
        "phase": "end",
        "ok": ok,
        "tokens": tokens,
        "durationMs": int((time.time() - self.started) * 1000),
        "error": error[:120],
    })

# Cache locale in memoria per evitare query di rete superflue
_cached_colab_url = None


def resolve_colab_url() -> str:
  """Recupera l'URL attivo di Colab dal canale cloud senza intervento manuale."""
  global _cached_colab_url
  try:
    # poll=1 e' obbligatorio: senza, /raw resta uno stream aperto e il
    # timeout scatta sempre, azzerando la scoperta dell'URL.
    url = f"https://ntfy.sh/{SYNC_CHANNEL}/raw?poll=1&since=all"
    res = requests.get(url, timeout=4)
    if res.status_code == 200 and res.text.strip():
      lines = [
          line.strip() for line in res.text.strip().split("\n") if line.strip()
      ]
      if lines:
        last_url = lines[-1]
        if last_url.startswith("http"):
          _cached_colab_url = last_url.rstrip("/")
  except Exception:
    pass
  return _cached_colab_url


@mcp.tool()
def ask_local_qwen(instruction: str, context_code: str = "") -> str:
  """Worker Locale (RTX 5060 8GB): Scrittura codice rapido e test a costo zero."""
  prompt = f"Contesto:\n{context_code}\n\nIstruzione:\n{instruction}"
  trace = _Trace("local", "Qwen locale", "qwen-worker", "ask_local_qwen")
  try:
    res = requests.post(
        OLLAMA_LOCAL_URL,
        json={
            "model": "qwen-worker",
            "prompt": prompt,
            "stream": False,
            "options": {"temperature": 0.1, "num_ctx": 12288},
        },
        timeout=120,
    )
    res.raise_for_status()
    data = res.json()
    saved = data.get("prompt_eval_count", 0) + data.get("eval_count", 0)
    trace.done(ok=True, tokens=saved)
    return (
        f"{data.get('response', '')}\n\n<!-- METRICS: [LOCAL_5060]"
        f" TOKENS_SAVED={saved} -->"
    )
  except Exception as e:
    trace.done(ok=False, error=str(e))
    return f"Errore Qwen Locale: {str(e)}"


@mcp.tool()
def ask_cloud_qwen_14b(instruction: str, context_code: str = "") -> str:
  """Worker Cloud Gratuito (Colab 16GB VRAM): Refactor e task complessi con auto-discovery."""
  trace = _Trace("colab", "Qwen 14B Colab", "qwen2.5-coder:14b", "ask_cloud_qwen_14b")
  base_url = resolve_colab_url()
  if not base_url:
    trace.done(ok=False, error="nessun tunnel sul canale di sincronizzazione")
    return (
        "Errore: Nessun worker Colab rilevato sul canale di sincronizzazione."
        " Avvia la cella su Google Colab."
    )

  endpoint = f"{base_url}/api/generate"
  prompt = f"Contesto:\n{context_code}\n\nIstruzione:\n{instruction}"
  try:
    res = requests.post(
        endpoint,
        json={
            "model": "qwen2.5-coder:14b",
            "prompt": prompt,
            "stream": False,
            "options": {"temperature": 0.2, "num_ctx": 16384},
        },
        timeout=180,
    )
    res.raise_for_status()
    data = res.json()
    saved = data.get("prompt_eval_count", 0) + data.get("eval_count", 0)
    trace.done(ok=True, tokens=saved)
    return (
        f"{data.get('response', '')}\n\n<!-- METRICS: [COLAB_14B]"
        f" TOKENS_SAVED={saved} -->"
    )
  except Exception as e:
    trace.done(ok=False, error=str(e))
    return f"Errore Cloud Colab ({endpoint}): {str(e)}"


@mcp.tool()
def ask_deepseek_reasoner(task_description: str, context: str = "") -> str:
  """DeepSeek Flash: Logica pura, calcoli e interfacce senza testo superfluo."""
  trace = _Trace("deepseek", "DeepSeek Flash", "deepseek-v4-flash", "ask_deepseek_reasoner")
  key = os.environ.get("DEEPSEEK_API_KEY", "")
  if not key:
    trace.done(ok=False, error="DEEPSEEK_API_KEY non configurata")
    return "Errore: DEEPSEEK_API_KEY non configurata nelle variabili d'ambiente."

  payload = {
      "model": "deepseek-v4-flash",
      "messages": [{
          "role": "user",
          "content": f"SPECIFICHE:\n{context}\n\nTASK:\n{task_description}",
      }],
      "temperature": 0.1,
  }
  headers = {"Authorization": f"Bearer {key}", "Content-Type": "application/json"}
  try:
    # 60 s non bastano a un reasoner: i token di ragionamento precedono
    # l'output e una specifica estesa andava in Read timeout (2026-09-08).
    res = requests.post(
        DEEPSEEK_URL, json=payload, headers=headers, timeout=300
    )
    res.raise_for_status()
    data = res.json()
    # L'API restituisce sempre `usage`: senza questo blocco il canale
    # DeepSeek finiva a registro con `+0` (CLAUDE.md 23).
    usage = data.get("usage") or {}
    details = usage.get("completion_tokens_details") or {}
    choices = data.get("choices") or [{}]
    content = (choices[0].get("message") or {}).get("content") or ""
    trace.done(ok=True, tokens=usage.get("total_tokens", 0))
    return (
        f"{content}\n\n<!-- METRICS: [DEEPSEEK_FLASH]"
        f" TOKENS_SAVED={usage.get('total_tokens', 0)}"
        f" MODEL={data.get('model', 'unknown')}"
        f" PROMPT={usage.get('prompt_tokens', 0)}"
        f" COMPLETION={usage.get('completion_tokens', 0)}"
        f" REASONING={details.get('reasoning_tokens', 0)}"
        f" CACHE_HIT={usage.get('prompt_cache_hit_tokens', 0)} -->"
    )
  except Exception as e:
    trace.done(ok=False, error=str(e))
    return f"Errore DeepSeek: {str(e)}"


if __name__ == "__main__":
  mcp.run()