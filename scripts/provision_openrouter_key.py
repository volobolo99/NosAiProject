#!/usr/bin/env python3
"""Crea la chiave di inferenza OpenRouter a partire dalla chiave di provisioning.

Perche' serve: una chiave con `is_provisioning_key: true` gestisce le altre chiavi
ma non fa inferenza. Con quella, `/api/v1/key` e `/api/v1/credits` rispondono 200
mentre `/api/v1/chat/completions` risponde 401 "User not found". La soluzione non
e' rigenerare la chiave, e' creare accanto una normale chiave di inferenza.

Cosa fa questo script:
  1. legge OPENROUTER_API_KEY (o OPENROUTER_PROVISIONING_KEY) da .env;
  2. verifica che sia davvero una chiave di provisioning;
  3. crea una chiave di inferenza sull'account;
  4. riscrive .env: la provisioning finisce in OPENROUTER_PROVISIONING_KEY e
     OPENROUTER_API_KEY diventa la nuova chiave di inferenza;
  5. prova una vera chiamata di completions per confermare che funziona.

La chiave non viene mai stampata per intero. `.env` e' escluso da git.

Uso:
    python scripts/provision_openrouter_key.py [--limit 5]
"""
from __future__ import annotations

import os
import sys
from pathlib import Path

import requests
from dotenv import load_dotenv

ROOT = Path(__file__).resolve().parents[1]
ENV = ROOT / ".env"
load_dotenv(ENV)

BASE = "https://openrouter.ai/api/v1"
MODELLO_DI_PROVA = "qwen/qwen3-coder-30b-a3b-instruct"


def abbrevia(chiave: str) -> str:
    return "{}...{} ({} caratteri)".format(chiave[:12], chiave[-4:], len(chiave))


def main() -> int:
    provisioning = os.getenv("OPENROUTER_PROVISIONING_KEY") or os.getenv("OPENROUTER_API_KEY", "")
    if not provisioning:
        print("Nessuna chiave OpenRouter trovata in .env")
        return 2

    limite = None
    if "--limit" in sys.argv:
        limite = float(sys.argv[sys.argv.index("--limit") + 1])

    intestazioni = {"Authorization": "Bearer " + provisioning, "Content-Type": "application/json"}

    stato = requests.get(BASE + "/key", headers=intestazioni, timeout=30)
    if stato.status_code != 200:
        print("La chiave in .env non e' utilizzabile ({}): {}".format(
            stato.status_code, stato.text[:200]))
        print("Creane una nuova su https://openrouter.ai/settings/keys")
        return 1

    dati = stato.json().get("data", {})
    if not dati.get("is_provisioning_key"):
        print("La chiave in .env NON e' di provisioning: non serve questo script.")
        print("Se le completions falliscono, il problema e' un altro. Esegui:")
        print("    python scripts/check_credentials.py")
        return 1

    print("Chiave di provisioning riconosciuta:", abbrevia(provisioning))

    corpo = {"name": "NosAi inference key"}
    if limite is not None:
        corpo["limit"] = limite
    creazione = requests.post(BASE + "/keys", headers=intestazioni, json=corpo, timeout=60)
    if creazione.status_code not in (200, 201):
        print("Creazione fallita ({}): {}".format(creazione.status_code, creazione.text[:300]))
        return 1

    risposta = creazione.json()
    nuova = risposta.get("key") or risposta.get("data", {}).get("key", "")
    if not nuova:
        print("La risposta non contiene la chiave. Campi ricevuti:", list(risposta))
        return 1
    print("Chiave di inferenza creata:", abbrevia(nuova))

    # La variabile d'ambiente di Windows vince sul file: nessuno degli script del
    # progetto usa override=True. La chiave va quindi impostata dove viene letta
    # per prima, altrimenti resterebbe invisibile.
    import subprocess

    esito = subprocess.run(
        ["setx", "OPENROUTER_API_KEY", nuova], capture_output=True, text=True, shell=True
    )
    if esito.returncode != 0:
        print("setx non riuscito:", (esito.stderr or esito.stdout).strip()[:200])
        print("Impostala a mano con: setx OPENROUTER_API_KEY \"<chiave>\"")
        return 1
    os.environ["OPENROUTER_API_KEY"] = nuova
    print("Chiave registrata in HKCU\\Environment (vale dal prossimo terminale)")

    # Nel file resta solo la provisioning, sotto il suo nome: niente doppia fonte
    # per la stessa variabile.
    testo = ENV.read_text(encoding="utf-8")
    righe = [
        r
        for r in testo.splitlines()
        if not r.startswith(("OPENROUTER_API_KEY=", "OPENROUTER_PROVISIONING_KEY="))
    ]
    righe.append("OPENROUTER_PROVISIONING_KEY=" + provisioning)
    ENV.write_text("\n".join(righe) + "\n", encoding="utf-8")
    print(".env ripulito: contiene la sola chiave di provisioning, sotto il suo nome")

    prova = requests.post(
        BASE + "/chat/completions",
        headers={"Authorization": "Bearer " + nuova, "Content-Type": "application/json"},
        json={
            "model": MODELLO_DI_PROVA,
            "messages": [{"role": "user", "content": "rispondi solo: ok"}],
            "max_tokens": 10,
        },
        timeout=90,
    )
    if prova.status_code != 200:
        print("La chiave e' stata creata ma la prova fallisce ({}): {}".format(
            prova.status_code, prova.text[:200]))
        return 1

    print("Prova di inferenza riuscita su", MODELLO_DI_PROVA)
    print()
    print("Ultimo passo: riavvia Claude Code, perche' il server MCP legge le chiavi all'avvio.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
