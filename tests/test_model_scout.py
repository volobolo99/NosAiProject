"""Criterio di accettazione di scripts/model_scout.py.

Lo scout propone, non dispone: nessun test qui gli concede di modificare il roster.
"""
from __future__ import annotations

import sys
from pathlib import Path
from unittest import mock

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))

import model_scout  # noqa: E402


def _modello(mid, prompt, completion, ctx=100000):
    return {"id": mid, "name": mid, "context_length": ctx,
            "pricing": {"prompt": str(prompt), "completion": str(completion)}}


def test_fetch_catalog_restituisce_data():
    risposta = mock.Mock(status_code=200)
    risposta.json.return_value = {"data": [_modello("a/b", 0, 0)]}
    with mock.patch.object(model_scout.requests, "get", return_value=risposta):
        assert model_scout.fetch_catalog() == [_modello("a/b", 0, 0)]


def test_fetch_catalog_errore_http():
    risposta = mock.Mock(status_code=503, text="giu'")
    with mock.patch.object(model_scout.requests, "get", return_value=risposta):
        with pytest.raises(RuntimeError) as exc:
            model_scout.fetch_catalog()
    assert "503" in str(exc.value)


def test_is_free():
    assert model_scout.is_free(_modello("x/y", 0, 0)) is True
    assert model_scout.is_free(_modello("x/y", "0.0000007", "0")) is False
    assert model_scout.is_free(_modello("x/y", "0", "0.0000007")) is False


def test_cost_per_mtok():
    ingresso, uscita = model_scout.cost_per_mtok(_modello("x/y", "0.0000007", "0.0000028"))
    assert round(ingresso, 4) == 0.7
    assert round(uscita, 4) == 2.8


def test_compare_segnala_gratuiti_e_piu_economici():
    catalogo = [
        _modello("qwen/qwen3-coder-30b-a3b-instruct", "0.00000007", "0.00000028"),
        _modello("nuovo/gratis", 0, 0),
        _modello("nuovo/economico", "0.00000001", "0.00000005"),
        _modello("nuovo/caro", "0.000005", "0.00001"),
    ]
    roster = {"worker": "qwen/qwen3-coder-30b-a3b-instruct"}
    esito = model_scout.compare_to_roster(catalogo, roster)
    worker = esito["ruoli"]["worker"]
    assert worker["in_uso"] == "qwen/qwen3-coder-30b-a3b-instruct"
    assert "nuovo/gratis" in worker["gratuiti_nuovi"]
    economici = [c["id"] for c in worker["pagamento_piu_economici"]]
    assert "nuovo/economico" in economici
    assert "nuovo/caro" not in economici
    assert esito["roster_mancanti"] == []


def test_compare_rileva_modello_sparito():
    catalogo = [_modello("altro/modello", "0.000001", "0.000002")]
    esito = model_scout.compare_to_roster(catalogo, {"worker": "sparito/modello"})
    assert "sparito/modello" in esito["roster_mancanti"]


def test_render_report_dichiara_il_consenso():
    catalogo = [
        _modello("qwen/qwen3-coder-30b-a3b-instruct", "0.00000007", "0.00000028"),
        _modello("nuovo/economico", "0.00000001", "0.00000005"),
    ]
    esito = model_scout.compare_to_roster(catalogo, {"worker": "qwen/qwen3-coder-30b-a3b-instruct"})
    testo = model_scout.render_report(esito)
    assert "worker" in testo
    assert "consenso" in testo.lower()
    assert testo.lstrip().startswith("#")
