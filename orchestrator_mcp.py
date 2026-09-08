import json
import os
from mcp.server.fastmcp import FastMCP
import requests

mcp = FastMCP("WorkerOrchestrator")

# ==========================================
# CONFIGURAZIONE ENDPOINT E CHIAVI
# ==========================================
OLLAMA_LOCAL_URL = "http://localhost:11434/api/generate"
COLAB_WORKER_URL = "https://phd-yale-depot-ryan.trycloudflare.com/api/generate"

# The key is never written in this file. Same convention the shipping Node
# server already follows (tools/deepseek-mcp/src/config.mjs:99): read
# DEEPSEEK_API_KEY from the process environment. A key in the source is one
# `git add` away from being public -- CLAUDE.md point 12.
DEEPSEEK_API_KEY = os.environ.get("DEEPSEEK_API_KEY", "")
DEEPSEEK_URL = "https://api.deepseek.com/v1/chat/completions"
# ==========================================


@mcp.tool()
def ask_local_qwen(instruction: str, context_code: str = "") -> str:
  """Worker Locale (RTX 5060 8GB): Scrittura codice rapido e test a costo zero."""
  prompt = f"Contesto:\n{context_code}\n\nIstruzione:\n{instruction}"
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
    saved_tokens = data.get("prompt_eval_count", 0) + data.get("eval_count", 0)
    return (
        f"{data.get('response', '')}\n\n<!-- METRICS: [LOCAL_5060]"
        f" TOKENS_SAVED={saved_tokens} -->"
    )
  except Exception as e:
    return f"Errore Qwen Locale: {str(e)}"


@mcp.tool()
def ask_cloud_qwen_14b(instruction: str, context_code: str = "") -> str:
  """Worker Cloud Gratuito (Google Colab 16GB VRAM): Refactor e task intermedi su Qwen 14B."""
  prompt = f"Contesto:\n{context_code}\n\nIstruzione:\n{instruction}"
  try:
    res = requests.post(
        COLAB_WORKER_URL,
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
    saved_tokens = data.get("prompt_eval_count", 0) + data.get("eval_count", 0)
    return (
        f"{data.get('response', '')}\n\n<!-- METRICS: [COLAB_14B]"
        f" TOKENS_SAVED={saved_tokens} -->"
    )
  except Exception as e:
    return f"Errore Cloud Colab: {str(e)}"


@mcp.tool()
def ask_deepseek_reasoner(task_description: str, context: str = "") -> str:
  """DeepSeek Flash: Analisi logico-matematica ad altissima velocità e zero fronzoli."""
  key = DEEPSEEK_API_KEY or os.environ.get("DEEPSEEK_API_KEY", "")
  if not key or "INSERISCI_QUI" in key:
    return "Errore: DEEPSEEK_API_KEY non impostata."

  payload = {
      "model": "deepseek-v4-flash",  # Versione Flash ad alta efficienza
      "messages": [{
          "role": "user",
          "content": f"SPECIFICHE:\n{context}\n\nTASK:\n{task_description}",
      }],
      "temperature": 0.1,
  }
  headers = {"Authorization": f"Bearer {key}", "Content-Type": "application/json"}

  try:
    res = requests.post(
        DEEPSEEK_URL, json=payload, headers=headers, timeout=60
    )
    res.raise_for_status()
    return res.json()["choices"][0]["message"]["content"]
  except Exception as e:
    return f"Errore DeepSeek Flash: {str(e)}"


if __name__ == "__main__":
  mcp.run()