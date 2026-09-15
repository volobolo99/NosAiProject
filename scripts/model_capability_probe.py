"""Banco funzionale rapido su piu' fornitori AI grezzi (fuori da OpenRouter).

Differisce da model_scout.py: quello confronta prezzi nel catalogo OpenRouter senza
mai chiamare i modelli. Questo esegue una chiamata reale (chat/completions) su ogni
modello testabile di un fornitore, con timeout stretto, e misura se risponde davvero
e in quanto tempo. Le chiavi non vengono mai lette da qui: arrivano da variabili
d'ambiente al momento dell'esecuzione (CEREBRAS_API_KEY, GEMINI_API_KEY,
NVIDIA_API_KEY). Non scrive mai una chiave su disco.

Uso:
    CEREBRAS_API_KEY=... GEMINI_API_KEY=... NVIDIA_API_KEY=... \
        python scripts/model_capability_probe.py

Scrive data/model_capability_scan.json con, per ogni modello: fornitore, id,
modalita', stato, latenza, corretto (risposta all'aritmetica di controllo),
finestra di contesto quando nota, nota.
"""
from __future__ import annotations

import json
import os
import sys
import time
from concurrent.futures import ThreadPoolExecutor, as_completed
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, List, Optional

import requests

OUT_PATH = Path(__file__).resolve().parents[1] / "data" / "model_capability_scan.json"
TIMEOUT_S = 20
MAX_TOKENS = 64
PROMPT_ARITMETICO = "Quanto fa 12*7? Rispondi solo con il numero, senza spiegazioni."
RISPOSTA_ATTESA = "84"

# Sottostringhe che segnalano un modello non testabile con /chat/completions
# (embedding, classificatori, traduzione, guardrail): endpoint diverso, salterebbe
# solo con un 404 che non dice nulla sulla qualita' del modello.
ESCLUSI_CHAT = (
    "embed", "guard", "safety", "reward", "detector", "translate",
    "parse", "nvclip", "riva-", "-tts", "transcribe",
)


def _classifica_modalita(model_id: str) -> str:
    mid = model_id.lower()
    if any(k in mid for k in ("image", "banana", "diffusion")):
        return "immagine"
    if any(k in mid for k in ("veo", "video")):
        return "video"
    if any(k in mid for k in ("lyria", "-tts", "audio", "transcribe")):
        return "audio"
    if any(k in mid for k in ("vision", "vila", "kosmos", "neva", "fuyu")):
        return "visione(input)"
    if any(k in mid for k in ("embed", "nvclip")):
        return "embedding"
    if any(k in mid for k in ("guard", "safety", "reward", "detector")):
        return "sicurezza/classificazione"
    if "translate" in mid or "riva-" in mid:
        return "traduzione"
    return "chat/testo"


def _probe_chat(base_url: str, key: str, model_id: str, auth_style: str = "bearer") -> Dict[str, Any]:
    headers = {"Content-Type": "application/json"}
    if auth_style == "bearer":
        headers["Authorization"] = f"Bearer {key}"
    payload = {
        "model": model_id,
        "messages": [{"role": "user", "content": PROMPT_ARITMETICO}],
        "max_tokens": MAX_TOKENS,
    }
    t0 = time.monotonic()
    try:
        resp = requests.post(f"{base_url}/chat/completions", headers=headers, json=payload, timeout=TIMEOUT_S)
    except requests.exceptions.Timeout:
        return {"stato": "timeout", "latenza_s": round(time.monotonic() - t0, 2), "corretto": False, "nota": f">{TIMEOUT_S}s senza risposta"}
    except requests.exceptions.RequestException as exc:
        return {"stato": "errore_rete", "latenza_s": round(time.monotonic() - t0, 2), "corretto": False, "nota": str(exc)[:200]}
    latenza = round(time.monotonic() - t0, 2)

    if resp.status_code != 200:
        try:
            dettaglio = resp.json()
        except ValueError:
            dettaglio = resp.text[:200]
        return {"stato": f"http_{resp.status_code}", "latenza_s": latenza, "corretto": False, "nota": json.dumps(dettaglio)[:200]}

    try:
        dati = resp.json()
        contenuto = dati["choices"][0]["message"].get("content") or ""
        finish = dati["choices"][0].get("finish_reason")
    except (KeyError, IndexError, ValueError) as exc:
        return {"stato": "risposta_malformata", "latenza_s": latenza, "corretto": False, "nota": str(exc)[:200]}

    corretto = RISPOSTA_ATTESA in contenuto
    nota = "" if contenuto.strip() else f"content vuoto (finish_reason={finish}): budget token insufficiente per il ragionamento"
    return {"stato": "ok" if contenuto.strip() else "vuoto", "latenza_s": latenza, "corretto": corretto, "nota": nota, "risposta": contenuto.strip()[:80]}


def scan_cerebras(key: Optional[str]) -> List[Dict[str, Any]]:
    risultati = []
    if not key:
        return risultati
    modelli = ["gemma-4-31b", "qwen-3.8-27b", "gpt-oss-120b"]
    for mid in modelli:
        esito = _probe_chat("https://api.cerebras.ai/v1", key, mid)
        risultati.append({"fornitore": "cerebras", "id": mid, "modalita": _classifica_modalita(mid), **esito})
    return risultati


def scan_nvidia(key: Optional[str]) -> List[Dict[str, Any]]:
    risultati: List[Dict[str, Any]] = []
    if not key:
        return risultati
    try:
        resp = requests.get("https://integrate.api.nvidia.com/v1/models", headers={"Authorization": f"Bearer {key}"}, timeout=30)
        resp.raise_for_status()
        catalogo = [m["id"] for m in resp.json().get("data", [])]
    except requests.exceptions.RequestException as exc:
        return [{"fornitore": "nvidia", "id": "*", "stato": "errore_catalogo", "nota": str(exc)[:200]}]

    testabili = [m for m in catalogo if not any(esc in m.lower() for esc in ESCLUSI_CHAT)]
    non_testabili = [m for m in catalogo if m not in testabili]

    with ThreadPoolExecutor(max_workers=10) as pool:
        future_to_id = {pool.submit(_probe_chat, "https://integrate.api.nvidia.com/v1", key, mid): mid for mid in testabili}
        for future in as_completed(future_to_id):
            mid = future_to_id[future]
            esito = future.result()
            risultati.append({"fornitore": "nvidia", "id": mid, "modalita": _classifica_modalita(mid), **esito})

    for mid in non_testabili:
        risultati.append({
            "fornitore": "nvidia", "id": mid, "modalita": _classifica_modalita(mid),
            "stato": "non_testato", "latenza_s": None, "corretto": False,
            "nota": "endpoint diverso da /chat/completions, escluso dal banco rapido",
        })
    return risultati


def scan_gemini(key: Optional[str]) -> List[Dict[str, Any]]:
    risultati: List[Dict[str, Any]] = []
    if not key:
        return risultati
    try:
        resp = requests.get(f"https://generativelanguage.googleapis.com/v1beta/models?key={key}", timeout=30)
        resp.raise_for_status()
        catalogo = resp.json().get("models", [])
    except requests.exceptions.RequestException as exc:
        return [{"fornitore": "gemini", "id": "*", "stato": "errore_catalogo", "nota": str(exc)[:200]}]

    # Una sola chiamata di conferma: se l'account e' bloccato a livello di progetto
    # (credito prepagato esaurito) ogni modello darebbe lo stesso esito, ripetere
    # la chiamata su ognuno sarebbe solo spreco di tempo e quota.
    sonda_id = "gemini-flash-latest"
    try:
        r = requests.post(
            f"https://generativelanguage.googleapis.com/v1beta/models/{sonda_id}:generateContent?key={key}",
            json={"contents": [{"parts": [{"text": PROMPT_ARITMETICO}]}], "generationConfig": {"maxOutputTokens": MAX_TOKENS}},
            timeout=TIMEOUT_S,
        )
        bloccato_progetto = r.status_code == 429 and "prepayment" in r.text.lower()
        sonda_nota = r.text[:200]
    except requests.exceptions.RequestException as exc:
        bloccato_progetto = False
        sonda_nota = str(exc)[:200]

    for m in catalogo:
        mid = m["name"].replace("models/", "")
        modalita = _classifica_modalita(mid)
        if bloccato_progetto:
            risultati.append({
                "fornitore": "gemini", "id": mid, "modalita": modalita,
                "stato": "bloccato_account", "latenza_s": None, "corretto": False,
                "nota": "credito prepagato esaurito a livello di progetto (verificato su " + sonda_id + "): " + sonda_nota,
                "context_input": m.get("inputTokenLimit"), "context_output": m.get("outputTokenLimit"),
            })
        else:
            risultati.append({
                "fornitore": "gemini", "id": mid, "modalita": modalita,
                "stato": "non_testato_singolarmente", "latenza_s": None, "corretto": False,
                "nota": "sonda di conferma non ha rilevato blocco account: servirebbe test per-modello",
                "context_input": m.get("inputTokenLimit"), "context_output": m.get("outputTokenLimit"),
            })
    return risultati


def scan_github_models() -> List[Dict[str, Any]]:
    return [{
        "fornitore": "github-models", "id": "*", "modalita": "n/d",
        "stato": "servizio_ritirato", "latenza_s": None, "corretto": False,
        "nota": "GitHub Models ritirato definitivamente il 30 luglio 2026 (annuncio GitHub Changelog): nessuna chiave funziona piu', nessun retest utile.",
    }]


def main() -> int:
    risultati: List[Dict[str, Any]] = []
    risultati += scan_cerebras(os.environ.get("CEREBRAS_API_KEY"))
    risultati += scan_nvidia(os.environ.get("NVIDIA_API_KEY"))
    risultati += scan_gemini(os.environ.get("GEMINI_API_KEY"))
    risultati += scan_github_models()

    out = {
        "schema_version": "nosai.model.capability_scan.v1",
        "generato_il": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "prompt_di_controllo": PROMPT_ARITMETICO,
        "risposta_attesa": RISPOSTA_ATTESA,
        "timeout_s": TIMEOUT_S,
        "risultati": risultati,
    }
    OUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    OUT_PATH.write_text(json.dumps(out, indent=2, ensure_ascii=False), encoding="utf-8")

    testati = [r for r in risultati if r.get("stato") not in ("non_testato", "servizio_ritirato", "bloccato_account", "non_testato_singolarmente", "errore_catalogo")]
    ok = [r for r in testati if r.get("stato") == "ok" and r.get("corretto")]
    print(f"Scan completato: {len(risultati)} voci totali, {len(testati)} chiamate reali eseguite, {len(ok)} rispondono correttamente.")
    print(f"Scritto in {OUT_PATH}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
