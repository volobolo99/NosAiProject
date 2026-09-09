"""Test per update_contract_state, lo strumento MCP che aggiorna contracts/ledger.json.

Il ledger reale non viene mai toccato: ogni test lavora su una copia in tmp_path,
sostituendo PROJECT_ROOT nel modulo.
"""

import json
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "scripts"))


@pytest.fixture
def ledger_env(tmp_path, monkeypatch):
    """Un ledger minimo su disco, con PROJECT_ROOT dirottato su tmp_path."""
    import mcp_server

    (tmp_path / "contracts").mkdir()
    (tmp_path / "docs").mkdir()
    ledger = {
        "gates": [
            {
                "gate": 1,
                "title": "Gate di prova",
                "completion_pct": 0,
                "contracts": [
                    {"cid": "C-101", "status": "DRAFT"},
                    {"cid": "C-102", "status": "DRAFT"},
                ],
            }
        ]
    }
    path = tmp_path / "contracts" / "ledger.json"
    path.write_text(json.dumps(ledger), encoding="utf-8")

    monkeypatch.setattr(mcp_server, "PROJECT_ROOT", tmp_path)
    # Nessun thread verso Ollama durante i test: la rigenerazione e' fuori perimetro.
    monkeypatch.setattr(mcp_server.threading, "Thread", lambda *a, **k: type("T", (), {"start": lambda self: None})())
    return mcp_server, path


def read(path):
    return json.loads(path.read_text(encoding="utf-8"))


def test_stato_valido_aggiorna_e_ricalcola_la_percentuale(ledger_env):
    mcp_server, path = ledger_env
    out = mcp_server.update_contract_state("C-101", "MERGED")

    assert "[CID: C-101]" in out and "[STATE: MERGED]" in out
    data = read(path)
    contract = data["gates"][0]["contracts"][0]
    assert contract["status"] == "MERGED"
    assert "updated" in contract
    # Uno su due contratti e' concluso.
    assert data["gates"][0]["completion_pct"] == 50


def test_le_metriche_finiscono_nel_contratto(ledger_env):
    mcp_server, path = ledger_env
    mcp_server.update_contract_state("C-102", "TEST_VERIFIED", metrics="12 test verdi")

    assert read(path)["gates"][0]["contracts"][1]["metrics"] == "12 test verdi"


def test_stato_non_valido_non_scrive_nulla(ledger_env):
    mcp_server, path = ledger_env
    prima = path.read_text(encoding="utf-8")

    out = mcp_server.update_contract_state("C-101", "QUASI_FATTO")

    assert out.startswith("ERROR:")
    assert path.read_text(encoding="utf-8") == prima


def test_contratto_sconosciuto_non_scrive_nulla(ledger_env):
    mcp_server, path = ledger_env
    prima = path.read_text(encoding="utf-8")

    out = mcp_server.update_contract_state("C-999", "MERGED")

    assert out.startswith("ERROR:")
    assert path.read_text(encoding="utf-8") == prima


def test_ledger_corrotto_non_viene_riscritto(ledger_env):
    mcp_server, path = ledger_env
    path.write_text("{ questo non e' json", encoding="utf-8")

    out = mcp_server.update_contract_state("C-101", "MERGED")

    assert out.startswith("ERROR:")
    assert path.read_text(encoding="utf-8") == "{ questo non e' json"


def test_ledger_assente_non_viene_creato(ledger_env):
    mcp_server, path = ledger_env
    path.unlink()

    out = mcp_server.update_contract_state("C-101", "MERGED")

    assert out.startswith("ERROR:")
    assert not path.exists()


def test_nessun_file_temporaneo_resta_sul_disco(ledger_env):
    mcp_server, path = ledger_env
    mcp_server.update_contract_state("C-101", "MERGED")

    assert list(path.parent.glob("*.tmp")) == []
