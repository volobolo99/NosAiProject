#!/usr/bin/env python3
"""Verifica le credenziali dei modelli e dice esattamente cosa non va.

Distingue i tre casi che si confondono facilmente:
  - chiave assente (non e' stata impostata, o la sessione non l'ha ancora letta);
  - chiave invalida (revocata, scaduta, copiata male);
  - chiave valida ma senza credito.

Uso:
    python scripts/check_credentials.py
"""
from __future__ import annotations

import os
import sys
from pathlib import Path

import requests
from dotenv import load_dotenv

ROOT = Path(__file__).resolve().parents[1]
load_dotenv(ROOT / ".env")

TIMEOUT = 20


def mostra(nome: str, valore: str) -> str:
    if not valore:
        return "assente"
    return "{}... ({} caratteri)".format(valore[:8], len(valore))


def controlla_ollama() -> tuple[bool, str]:
    url = os.getenv("OLLAMA_LOCAL_URL", "http://localhost:11434/api/generate")
    tags = url.replace("/api/generate", "/api/tags")
    try:
        risposta = requests.get(tags, timeout=TIMEOUT)
        risposta.raise_for_status()
        modelli = [m["name"] for m in risposta.json().get("models", [])]
    except Exception as exc:
        return False, "Ollama non raggiungibile su {}: {}".format(tags, exc)
    if "qwen2.5-coder:7b" not in modelli:
        return False, "Ollama attivo ma manca qwen2.5-coder:7b. Presenti: " + ", ".join(modelli)
    return True, "attivo, qwen2.5-coder:7b disponibile"


def controlla_openrouter() -> tuple[bool, str]:
    chiave = os.getenv("OPENROUTER_API_KEY", "")
    if not chiave:
        return False, "OPENROUTER_API_KEY assente da .env"
    try:
        risposta = requests.get(
            "https://openrouter.ai/api/v1/key",
            headers={"Authorization": "Bearer " + chiave},
            timeout=TIMEOUT,
        )
    except Exception as exc:
        return False, "rete non raggiungibile: {}".format(exc)

    if risposta.status_code == 401:
        return False, "chiave rifiutata (401): va rigenerata su https://openrouter.ai/settings/keys"
    if risposta.status_code != 200:
        return False, "risposta inattesa {}: {}".format(risposta.status_code, risposta.text[:160])

    dati = risposta.json().get("data", {})
    limite = dati.get("limit")
    usato = dati.get("usage")
    if limite is not None and usato is not None and usato >= limite:
        return False, "chiave valida ma credito esaurito: usato {} su {}".format(usato, limite)
    residuo = "illimitato" if limite is None else "{} residuo".format(round(limite - (usato or 0), 4))
    return True, "valida, credito {}".format(residuo)


def controlla_deepseek() -> tuple[bool, str]:
    chiave = os.getenv("DEEPSEEK_API_KEY", "")
    if not chiave:
        return False, (
            "DEEPSEEK_API_KEY assente dall'ambiente. Se l'hai appena impostata con setx, "
            "apri un terminale nuovo: setx non tocca le sessioni gia' aperte"
        )
    try:
        risposta = requests.get(
            "https://api.deepseek.com/user/balance",
            headers={"Authorization": "Bearer " + chiave},
            timeout=TIMEOUT,
        )
    except Exception as exc:
        return False, "rete non raggiungibile: {}".format(exc)

    if risposta.status_code == 401:
        return False, (
            "chiave rifiutata (401): va rigenerata su https://platform.deepseek.com/api_keys"
        )
    if risposta.status_code != 200:
        return False, "risposta inattesa {}: {}".format(risposta.status_code, risposta.text[:160])

    dati = risposta.json()
    if not dati.get("is_available", False):
        return False, "chiave valida ma account senza credito disponibile"
    saldi = dati.get("balance_infos", [])
    if saldi:
        primo = saldi[0]
        return True, "valida, saldo {} {}".format(
            primo.get("total_balance"), primo.get("currency")
        )
    return True, "valida"


def main() -> int:
    print("Chiavi lette:")
    print("  OPENROUTER_API_KEY :", mostra("openrouter", os.getenv("OPENROUTER_API_KEY", "")), "(da .env)")
    print("  DEEPSEEK_API_KEY   :", mostra("deepseek", os.getenv("DEEPSEEK_API_KEY", "")), "(da HKCU\\Environment)")
    print()

    esiti = {
        "Ollama locale (qwen2.5-coder:7b)": controlla_ollama(),
        "OpenRouter (Qwen3 Coder 30B, Gemini 2.5 Flash Lite)": controlla_openrouter(),
        "DeepSeek (V4 Flash)": controlla_deepseek(),
    }
    for nome, (ok, dettaglio) in esiti.items():
        print("[{}] {}\n     {}".format("OK " if ok else "KO ", nome, dettaglio))

    mancanti = [n for n, (ok, _) in esiti.items() if not ok]
    print()
    if mancanti:
        print("Da sistemare:", len(mancanti), "su", len(esiti))
        print("Dopo aver aggiornato una chiave, riavvia Claude Code: il server MCP legge le")
        print("credenziali all'avvio e continuerebbe a usare quella vecchia.")
        return 1
    print("Tutte le credenziali sono valide: la catena puo' usare ogni modello del roster.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
