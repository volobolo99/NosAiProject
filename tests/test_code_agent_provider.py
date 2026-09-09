"""Criterio di accettazione sulle richieste che code_agent manda a OpenRouter.

Un provider che risponde 200 con il contenuto vuoto ha gia' bloccato la catena
tre volte: la richiesta deve escludere i provider guasti e, quando il contenuto
manca lo stesso, l'errore deve dire quale provider ha taciuto invece di
attribuire la colpa a un budget di token che non c'entra.
"""
from __future__ import annotations

import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))

import code_agent  # noqa: E402
import free_chain  # noqa: E402


class Risposta:
    def __init__(self, payload, status=200):
        self._payload = payload
        self.status_code = status

    def raise_for_status(self):
        if self.status_code >= 400:
            raise RuntimeError("HTTP {}".format(self.status_code))

    def json(self):
        return self._payload


def cattura(monkeypatch, payload):
    """Sostituisce requests.post e restituisce la lista dei corpi inviati."""
    inviati = []

    def finta_post(url, headers=None, json=None, timeout=None):
        inviati.append(json)
        return Risposta(payload)

    monkeypatch.setattr(code_agent.requests, "post", finta_post)
    monkeypatch.setattr(code_agent, "OPENROUTER_KEY", "chiave-finta")
    return inviati


BUONA = {
    "choices": [{"message": {"content": "codice"}}],
    "usage": {"prompt_tokens": 1, "completion_tokens": 1},
    "provider": "SiliconFlow",
}

VUOTA = {
    "choices": [{"message": {"content": None}}],
    "usage": {"prompt_tokens": 1, "completion_tokens": 0},
    "provider": "Novita",
}


def test_la_richiesta_esclude_i_provider_guasti(monkeypatch):
    inviati = cattura(monkeypatch, BUONA)
    code_agent.call_model("qwen/qwen3-coder-30b-a3b-instruct", "prompt", "sistema")
    assert len(inviati) == 1
    provider = inviati[0].get("provider")
    assert provider is not None, "la richiesta non dichiara alcuna preferenza di provider"
    ignorati = provider.get("ignore", [])
    for guasto in free_chain.PROVIDER_GUASTI:
        assert guasto in ignorati, "il provider guasto {} non e' escluso".format(guasto)


def test_l_elenco_degli_esclusi_viene_da_free_chain(monkeypatch):
    """Un solo elenco dei provider guasti, non due copie che divergono."""
    inviati = cattura(monkeypatch, BUONA)
    code_agent.call_model("qwen/qwen3-coder-30b-a3b-instruct", "prompt", "sistema")
    assert inviati[0]["provider"]["ignore"] == list(free_chain.PROVIDER_GUASTI)


def test_il_contenuto_vuoto_accusa_il_provider_non_il_budget(monkeypatch):
    cattura(monkeypatch, VUOTA)
    with pytest.raises(RuntimeError) as errore:
        code_agent.call_model("qwen/qwen3-coder-30b-a3b-instruct", "prompt", "sistema")
    messaggio = str(errore.value)
    assert "Novita" in messaggio, "l'errore non dice quale provider ha taciuto"


def test_ollama_non_riceve_preferenze_di_provider(monkeypatch):
    """Il locale non passa da OpenRouter: nessun campo provider nella richiesta."""
    payload = {"response": "codice", "prompt_eval_count": 1, "eval_count": 1}
    inviati = cattura(monkeypatch, payload)
    code_agent.call_model("qwen2.5-coder:7b", "prompt", "sistema")
    assert "provider" not in inviati[0]


def test_deepseek_resta_fuori_da_openrouter():
    """Regressione sul lucchetto: DeepSeek solo sull'API nativa."""
    with pytest.raises(RuntimeError) as errore:
        code_agent.call_model("qualcuno/deepseek-cosa", "prompt", "sistema")
    assert "OpenRouter" in str(errore.value)


# ------------------------------------------------------- il fornitore Groq


def test_groq_va_sul_suo_endpoint_con_la_sua_chiave(monkeypatch):
    """Il prefisso groq: instrada su api.groq.com, non su OpenRouter."""
    visti = []

    def finta_post(url, headers=None, json=None, timeout=None):
        visti.append((url, headers, json))
        return Risposta(BUONA)

    monkeypatch.setattr(code_agent.requests, "post", finta_post)
    monkeypatch.setattr(code_agent, "GROQ_KEY", "chiave-groq")
    code_agent.call_model("groq:openai/gpt-oss-120b", "prompt", "sistema")

    url, headers, corpo = visti[0]
    assert "api.groq.com" in url
    assert headers["Authorization"] == "Bearer chiave-groq"
    assert corpo["model"] == "openai/gpt-oss-120b", "il prefisso groq: non va inviato al fornitore"


def test_groq_non_riceve_le_preferenze_di_provider_di_openrouter(monkeypatch):
    visti = []

    def finta_post(url, headers=None, json=None, timeout=None):
        visti.append(json)
        return Risposta(BUONA)

    monkeypatch.setattr(code_agent.requests, "post", finta_post)
    monkeypatch.setattr(code_agent, "GROQ_KEY", "chiave-groq")
    code_agent.call_model("groq:openai/gpt-oss-120b", "prompt", "sistema")
    assert "provider" not in visti[0]


def test_groq_senza_chiave_lo_dice_chiaramente(monkeypatch):
    monkeypatch.setattr(code_agent, "GROQ_KEY", "")
    with pytest.raises(RuntimeError) as errore:
        code_agent.call_model("groq:openai/gpt-oss-120b", "prompt", "sistema")
    assert "GROQ_API_KEY" in str(errore.value)
