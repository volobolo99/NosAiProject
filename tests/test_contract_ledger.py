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


# --- lo scopo abbandonato non e' lavoro incompleto -------------------------

def test_un_contratto_abbandonato_esce_dal_denominatore(ledger_env):
    """Misurato il 2026-09-11: done_count contava solo gli stati conclusi, ma i
    DROPPED restavano nel denominatore. Il Gate 0 reale, con tre contratti
    conclusi e tre abbandonati per decisione (ADR-0029), segnava 50% pur non
    avendo piu' nulla di aperto, e non poteva piu' raggiungere il 100%.

    Uno scopo rimosso per scelta non e' lavoro che manca: e' lavoro che non
    serve. Contarlo come mancante rende la percentuale una misura sbagliata
    proprio della domanda a cui deve rispondere.
    """
    mcp_server, path = ledger_env
    mcp_server.update_contract_state("C-101", "MERGED")
    mcp_server.update_contract_state("C-102", "DROPPED")

    data = read(path)
    assert data["gates"][0]["contracts"][1]["status"] == "DROPPED"
    assert data["gates"][0]["completion_pct"] == 100, (
        "un gate senza lavoro aperto e' completo: l'abbandonato non conta")


def test_un_gate_tutto_abbandonato_e_completo(ledger_env):
    mcp_server, path = ledger_env
    mcp_server.update_contract_state("C-101", "DROPPED")
    mcp_server.update_contract_state("C-102", "DROPPED")

    assert read(path)["gates"][0]["completion_pct"] == 100


def test_l_abbandono_non_gonfia_la_percentuale_se_resta_lavoro(ledger_env):
    """Abbandonare non deve diventare un modo di far salire il numero: con un
    contratto ancora aperto il gate resta incompleto."""
    mcp_server, path = ledger_env
    mcp_server.update_contract_state("C-101", "DROPPED")

    assert read(path)["gates"][0]["completion_pct"] == 0, (
        "C-102 e' ancora DRAFT: il gate non e' completo"
    )


# --- render_roadmap_markdown e' deterministico, non un modello -------------
#
# Prima chiamava un 7B locale con l'intero ledger dentro un prompt libero, e
# quel modello riportava domande gia' chiuse come ancora aperte, oltre a
# mischiare note del ledger in righe di contratti a cui non appartenevano.
# Un rendering deterministico non puo' commettere quell'errore: ogni riga e'
# copiata cosi' com'e' da un campo del ledger.

def test_roadmap_riporta_verbatim_una_domanda_aperta(ledger_env):
    mcp_server, _ = ledger_env
    ledger = {
        "gates": [],
        "domande_aperte": ["C-999: RISOLTA da ADR-0099 del 2026-01-01 — testo esatto."],
    }
    markdown = mcp_server.render_roadmap_markdown(ledger)

    assert "## Domande aperte" in markdown
    assert "- C-999: RISOLTA da ADR-0099 del 2026-01-01 — testo esatto." in markdown


def test_roadmap_non_riporta_domande_aperte_assenti(ledger_env):
    mcp_server, _ = ledger_env
    markdown = mcp_server.render_roadmap_markdown({"gates": []})

    assert "## Domande aperte" not in markdown


def test_roadmap_elenca_ogni_contratto_del_gate_con_stato(ledger_env):
    mcp_server, _ = ledger_env
    ledger = {
        "gates": [
            {
                "gate": 1,
                "title": "Gate di prova",
                "completion_pct": 50,
                "contracts": [
                    {"cid": "C-101", "title": "Uno", "status": "MERGED"},
                    {"cid": "C-102", "title": "Due", "status": "DRAFT", "blocker": "manca il file"},
                ],
            }
        ]
    }
    markdown = mcp_server.render_roadmap_markdown(ledger)

    assert "## Gate 1 — Gate di prova (50%)" in markdown
    assert "| C-101 | Uno | MERGED |" in markdown
    assert "| C-102 | Due | DRAFT |" in markdown
    assert "blocker: manca il file" in markdown


def test_roadmap_non_solleva_eccezioni_su_ledger_minimale(ledger_env):
    mcp_server, _ = ledger_env
    assert mcp_server.render_roadmap_markdown({}) == mcp_server.render_roadmap_markdown({"gates": []})
    mcp_server.render_roadmap_markdown({"gates": [{"gate": 1, "title": "x", "completion_pct": 0, "contracts": [{"cid": "C-1", "status": "DRAFT"}]}]})


def test_update_contract_state_scrive_la_roadmap_dal_ledger_reale(tmp_path, monkeypatch):
    """A differenza della fixture ledger_env, qui il thread NON e' mockato:
    regenerate_roadmap ora e' puro calcolo locale (nessuna rete), quindi puo'
    girare per davvero dentro il test senza renderlo lento o fragile."""
    import mcp_server

    (tmp_path / "contracts").mkdir()
    (tmp_path / "docs").mkdir()
    ledger = {
        "domande_aperte": ["C-304: RISOLTA da ADR-0031 — nessuna FSM esplicita."],
        "gates": [
            {
                "gate": 1,
                "title": "Gate di prova",
                "completion_pct": 0,
                "contracts": [{"cid": "C-101", "status": "DRAFT"}],
            }
        ],
    }
    ledger_path = tmp_path / "contracts" / "ledger.json"
    ledger_path.write_text(json.dumps(ledger), encoding="utf-8")
    monkeypatch.setattr(mcp_server, "PROJECT_ROOT", tmp_path)

    mcp_server.update_contract_state("C-101", "MERGED")

    import time

    for _ in range(50):
        roadmap_path = tmp_path / "docs" / "MASTER_ROADMAP.md"
        if roadmap_path.exists():
            break
        time.sleep(0.02)
    content = roadmap_path.read_text(encoding="utf-8")
    assert "C-304: RISOLTA da ADR-0031 — nessuna FSM esplicita." in content
    assert "| C-101 |" in content and "MERGED" in content
