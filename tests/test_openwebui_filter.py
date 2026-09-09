"""Criterio di accettazione del filtro per Open WebUI.

Il filtro porta in chat le due leve verificate della catena: la cascata di
ripiego e il lucchetto sul costo. La regola piu' importante e' la piu' facile
da sbagliare: il lucchetto non deve mai scattare su un modello a pagamento
scelto apposta dall'utente, perche' lo bloccherebbe con un 404.
"""
from __future__ import annotations

import asyncio
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools" / "open-webui"))

import nosai_free_router  # noqa: E402


def esegui(coro):
    return asyncio.run(coro)


@pytest.fixture()
def filtro():
    return nosai_free_router.Filter()


def test_riconosce_i_gratuiti(filtro):
    assert filtro.e_gratuito("nex-agi/nex-n2.5-mini:free") is True
    assert filtro.e_gratuito("openrouter/free") is True
    assert filtro.e_gratuito("qwen/qwen3-coder-30b-a3b-instruct") is False
    assert filtro.e_gratuito("") is False
    assert filtro.e_gratuito(None) is False


def test_gratuito_riceve_lucchetto_e_cascata(filtro):
    corpo = esegui(filtro.inlet({"model": "nex-agi/nex-n2.5-mini:free", "messages": [{"role": "user", "content": "c"}]}))
    assert corpo["provider"]["max_price"] == {"prompt": 0, "completion": 0}
    assert corpo["models"][0] == "nex-agi/nex-n2.5-mini:free"
    assert len(corpo["models"]) <= 3
    assert len(corpo["models"]) == len(set(corpo["models"])), "nessun modello duplicato"
    assert "Novita" in corpo["provider"]["ignore"]


def test_il_pagamento_non_viene_mai_bloccato(filtro):
    """Se l'utente sceglie un modello a pagamento lo ha voluto: il lucchetto
    lo farebbe fallire con 404."""
    corpo = esegui(filtro.inlet({"model": "qwen/qwen3-coder-30b-a3b-instruct", "messages": []}))
    assert "max_price" not in corpo.get("provider", {})
    assert "models" not in corpo
    assert "Novita" in corpo["provider"]["ignore"]


def test_valvola_spenta_non_tocca_nulla(filtro):
    filtro.valves.abilitato = False
    originale = {"model": "nex-agi/nex-n2.5-mini:free", "messages": [{"role": "user", "content": "c"}]}
    corpo = esegui(filtro.inlet(dict(originale)))
    assert corpo == originale


def test_i_messaggi_restano_intatti(filtro):
    messaggi = [{"role": "system", "content": "sei un agente"}, {"role": "user", "content": "ciao"}]
    corpo = esegui(filtro.inlet({"model": "openrouter/free", "messages": messaggi, "temperature": 0.7}))
    assert corpo["messages"] == messaggi
    assert corpo["temperature"] == 0.7


def test_modello_mancante_non_rompe(filtro):
    corpo = esegui(filtro.inlet({"messages": []}))
    assert "models" not in corpo
    assert "max_price" not in corpo.get("provider", {})


def test_outlet_non_altera_la_risposta(filtro):
    risposta = {"messages": [{"role": "assistant", "content": "risposta"}]}
    assert esegui(filtro.outlet(dict(risposta))) == risposta


def test_il_lucchetto_si_puo_disattivare(filtro):
    filtro.valves.lucchetto_sui_gratuiti = False
    corpo = esegui(filtro.inlet({"model": "nex-agi/nex-n2.5-mini:free", "messages": []}))
    assert "max_price" not in corpo.get("provider", {})
    assert corpo["models"][0] == "nex-agi/nex-n2.5-mini:free"
