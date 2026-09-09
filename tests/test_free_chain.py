"""Criterio di accettazione della catena di riserva gratuita.

La catena non decide quale modello sia il migliore: usa l'ordine gia' misurato
dal banco e si limita a non fermarsi quando uno dei modelli tace.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))

import free_chain  # noqa: E402


def test_percorso_ancorato_al_file():
    assert isinstance(free_chain.FREE_ROSTER_PATH, Path)
    assert free_chain.FREE_ROSTER_PATH.is_absolute()
    assert free_chain.FREE_ROSTER_PATH.name == "free_roster.json"


def test_free_models_ordina_per_latenza(tmp_path, monkeypatch):
    roster = {"validati": [
        {"id": "lento/uno", "sec_medi": 60},
        {"id": "svelto/due", "sec_medi": 8},
        {"id": "medio/tre", "sec_medi": 22},
    ]}
    f = tmp_path / "free_roster.json"
    f.write_text(json.dumps(roster), encoding="utf-8")
    monkeypatch.setattr(free_chain, "FREE_ROSTER_PATH", f)
    assert free_chain.free_models() == ["svelto/due", "medio/tre", "lento/uno"]


def test_free_models_scarta_i_lenti(tmp_path, monkeypatch):
    roster = {"validati": [
        {"id": "svelto/uno", "sec_medi": 8},
        {"id": "lumaca/due", "sec_medi": 422},
        {"id": "confine/tre", "sec_medi": 200},
    ]}
    f = tmp_path / "free_roster.json"
    f.write_text(json.dumps(roster), encoding="utf-8")
    monkeypatch.setattr(free_chain, "FREE_ROSTER_PATH", f)
    assert free_chain.free_models() == ["svelto/uno"]
    assert free_chain.free_models(escludi_lenti=False) == ["svelto/uno", "confine/tre", "lumaca/due"]


def test_free_models_file_mancante(tmp_path, monkeypatch):
    monkeypatch.setattr(free_chain, "FREE_ROSTER_PATH", tmp_path / "non_esiste.json")
    assert free_chain.free_models() == []


def test_roster_reale_del_progetto():
    modelli = free_chain.free_models()
    assert modelli, "il roster del progetto non deve essere vuoto"
    assert all(m.endswith(":free") or m == "openrouter/free" for m in modelli)


def test_primo_modello_riuscito_ferma_la_catena():
    provati = []

    def chiamata(modello):
        provati.append(modello)
        return "risposta buona"

    testo, usato = free_chain.call_with_fallback(chiamata, ["a/uno", "b/due"])
    assert (testo, usato) == ("risposta buona", "a/uno")
    assert provati == ["a/uno"]


def test_eccezione_passa_al_successivo():
    def chiamata(modello):
        if modello == "a/uno":
            raise RuntimeError("429 dal provider")
        return "seconda risposta"

    testo, usato = free_chain.call_with_fallback(chiamata, ["a/uno", "b/due"])
    assert testo == "seconda risposta"
    assert usato == "b/due"


def test_risposta_vuota_conta_come_fallimento():
    def chiamata(modello):
        return "   " if modello == "a/uno" else "contenuto vero"

    testo, usato = free_chain.call_with_fallback(chiamata, ["a/uno", "b/due"])
    assert testo == "contenuto vero"
    assert usato == "b/due"


def test_tutti_falliti_solleva_ed_elenca():
    def chiamata(modello):
        raise RuntimeError("guasto su " + modello)

    with pytest.raises(RuntimeError) as exc:
        free_chain.call_with_fallback(chiamata, ["a/uno", "b/due"])
    messaggio = str(exc.value)
    assert "a/uno" in messaggio and "b/due" in messaggio


def test_lista_vuota_solleva():
    with pytest.raises(RuntimeError):
        free_chain.call_with_fallback(lambda m: "x", [])
