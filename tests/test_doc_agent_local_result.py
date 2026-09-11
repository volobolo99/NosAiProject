"""Verifica che scripts/doc_agent.py::run() applichi davvero
nosai.orchestration.local_result.validate_local_result() al risultato che
costruisce, invece di limitarsi a dichiararlo conforme per convenzione.
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))

import doc_agent  # noqa: E402
from nosai.orchestration.local_result import validate_local_result  # noqa: E402

TASK = {"file": "docs/example.md", "purpose": "prova", "format": "markdown", "min_words": 1, "max_words": 700}


def test_run_produce_un_risultato_conforme_allo_schema(monkeypatch):
    monkeypatch.setattr(doc_agent, "call_local", lambda prompt: "# Titolo\n\nContenuto di prova sufficiente.")
    monkeypatch.setattr(doc_agent, "record", lambda entry: None)
    risultato = doc_agent.run(TASK, dry_run=True)
    assert risultato["status"] == "completed"
    assert validate_local_result(risultato) == []


def test_run_degrada_a_blocked_se_lo_schema_non_e_rispettato(monkeypatch):
    """Un futuro drift fra il dict costruito a mano e lo schema deve essere
    colto da questa funzione, non scoperto in produzione."""
    monkeypatch.setattr(doc_agent, "call_local", lambda prompt: "# Titolo\n\nContenuto di prova sufficiente.")
    registrati = []
    monkeypatch.setattr(doc_agent, "record", lambda entry: registrati.append(entry))
    monkeypatch.setattr(doc_agent, "validate_local_result", lambda payload: ["difetto simulato"])

    risultato = doc_agent.run(TASK, dry_run=True)

    assert risultato["status"] == "blocked"
    assert "difetto simulato" in risultato["missing_items"]
    assert registrati[-1]["status"] == "blocked"
