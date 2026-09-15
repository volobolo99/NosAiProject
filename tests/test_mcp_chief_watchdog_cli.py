"""Criterio di accettazione del contratto C-316 per lo strumento a riga di
comando, contracts/mcp-chief-watchdog-023.json.

code_agent.py non ha un tests_command per questo file (nessun test lo
esercitava), e main() costruiva ModelRouter.from_config(config) con un
argomento mancante (policy): un TypeError a ogni esecuzione reale, mai
scoperto perche' import_check importa il modulo senza chiamare main().
Isolato con ROOT e load_config di monkeypatch, cosi' non tocca
data/mcp/role_bindings.json ne' data/mcp/state.sqlite3 del repository reale.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))

import mcp_chief_watchdog  # noqa: E402


def _config_isolato(tmp_path):
    return {
        "schema_version": "mcp.config.v1",
        "network_enabled": False,
        "role_bindings_path": str(tmp_path / "mcp" / "role_bindings.json"),
        "providers": [],
    }


def test_main_senza_argomenti_esegue_un_tick_senza_sollevare(tmp_path, monkeypatch):
    monkeypatch.setattr(mcp_chief_watchdog, "ROOT", tmp_path)
    monkeypatch.setattr(mcp_chief_watchdog, "load_config", lambda *a, **k: _config_isolato(tmp_path))

    esito = mcp_chief_watchdog.main([])

    assert esito in (0, 1)
    log_path = tmp_path / "data" / "mcp" / "chief_watchdog.jsonl"
    righe = log_path.read_text(encoding="utf-8").splitlines()
    assert len(righe) == 1
    record = json.loads(righe[0])
    assert record["schema_version"] == "mcp.chief.watchdog.v1"


def test_due_esecuzioni_di_seguito_appendono_due_righe(tmp_path, monkeypatch):
    monkeypatch.setattr(mcp_chief_watchdog, "ROOT", tmp_path)
    monkeypatch.setattr(mcp_chief_watchdog, "load_config", lambda *a, **k: _config_isolato(tmp_path))

    mcp_chief_watchdog.main([])
    mcp_chief_watchdog.main([])

    log_path = tmp_path / "data" / "mcp" / "chief_watchdog.jsonl"
    assert len(log_path.read_text(encoding="utf-8").splitlines()) == 2


def test_tail_restituisce_i_tick_recenti_senza_costruire_il_chief(tmp_path, monkeypatch):
    monkeypatch.setattr(mcp_chief_watchdog, "ROOT", tmp_path)

    def _load_config_che_non_dovrebbe_servire(*a, **k):
        raise AssertionError("--tail non deve costruire policy/router/chief")

    monkeypatch.setattr(mcp_chief_watchdog, "load_config", _load_config_che_non_dovrebbe_servire)
    log_path = tmp_path / "data" / "mcp" / "chief_watchdog.jsonl"
    log_path.parent.mkdir(parents=True)
    log_path.write_text(
        json.dumps({"schema_version": "mcp.chief.watchdog.v1", "ticked_at": "2026-09-12T10:00:00+00:00"}) + "\n",
        encoding="utf-8",
    )

    esito = mcp_chief_watchdog.main(["--tail", "5"])

    assert esito == 0
