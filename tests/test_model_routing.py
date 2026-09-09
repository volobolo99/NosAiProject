"""Test dell'instradamento dei modelli.

Regola dell'operatore: DeepSeek si chiama sulla sua API nativa, dove sta il
credito. Da OpenRouter non deve partire nessuna richiesta DeepSeek, mai.
"""

import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "scripts"))

import mcp_server


class FakeResponse:
    def __init__(self, payload):
        self._payload = payload

    def raise_for_status(self):
        return None

    def json(self):
        return self._payload


@pytest.mark.parametrize(
    "model_id",
    ["deepseek/deepseek-r1", "deepseek-v4-flash", "DeepSeek/Chat", "some-vendor/deepseek-coder"],
)
def test_openrouter_rifiuta_qualunque_modello_deepseek(model_id, monkeypatch):
    def esplodi(*a, **k):
        raise AssertionError("nessuna richiesta deve partire verso OpenRouter")

    monkeypatch.setattr(mcp_server.requests, "post", esplodi)

    with pytest.raises(ValueError, match="non passa da OpenRouter"):
        mcp_server.call_openrouter(model_id, "p", "s")


def test_openrouter_lascia_passare_gli_altri_modelli(monkeypatch):
    visti = {}

    def finta_post(url, headers=None, json=None, timeout=None):
        visti["url"] = url
        visti["model"] = json["model"]
        return FakeResponse({"choices": [{"message": {"content": "ok"}}]})

    monkeypatch.setattr(mcp_server.requests, "post", finta_post)

    assert mcp_server.call_openrouter("qwen/qwen3-coder-30b-a3b-instruct", "p", "s") == "ok"
    assert visti["model"] == "qwen/qwen3-coder-30b-a3b-instruct"


def test_call_deepseek_va_sull_endpoint_nativo(monkeypatch):
    visti = {}

    def finta_post(url, headers=None, json=None, timeout=None):
        visti["url"] = url
        visti["auth"] = headers["Authorization"]
        visti["model"] = json["model"]
        return FakeResponse({"choices": [{"message": {"content": "diagnosi"}}]})

    monkeypatch.setattr(mcp_server, "DEEPSEEK_API_KEY", "sk-finta")
    monkeypatch.setattr(mcp_server.requests, "post", finta_post)

    assert mcp_server.call_deepseek("deepseek-v4-flash", "crash", "sys") == "diagnosi"
    assert "api.deepseek.com" in visti["url"]
    assert "openrouter" not in visti["url"]
    assert visti["auth"] == "Bearer sk-finta"


def test_call_deepseek_senza_chiave_fallisce_chiuso(monkeypatch):
    monkeypatch.setattr(mcp_server, "DEEPSEEK_API_KEY", "")

    def esplodi(*a, **k):
        raise AssertionError("nessuna richiesta deve partire senza chiave")

    monkeypatch.setattr(mcp_server.requests, "post", esplodi)

    with pytest.raises(RuntimeError, match="DEEPSEEK_API_KEY"):
        mcp_server.call_deepseek("deepseek-v4-flash", "p", "s")


def test_auditor_e_un_modello_nativo_non_un_percorso_openrouter():
    auditor = mcp_server.ROSTER["auditor"]

    # Un id nativo DeepSeek non ha il prefisso di vendor che usa OpenRouter.
    assert "/" not in auditor
    # L'operatore ha vietato il modello pro il 2026-09-08.
    assert "pro" not in auditor


def test_nessun_modello_deepseek_resta_nel_roster_openrouter():
    per_openrouter = [v for k, v in mcp_server.ROSTER.items() if k in ("worker", "preflight")]

    assert not any("deepseek" in m.lower() for m in per_openrouter)


def test_contenuto_vuoto_da_un_modello_di_ragionamento_fallisce(monkeypatch):
    """deepseek-v4-flash spende max_tokens in reasoning_content prima di content:
    con un budget stretto content resta vuoto, e una diagnosi vuota non va restituita."""

    def finta_post(url, headers=None, json=None, timeout=None):
        return FakeResponse(
            {"choices": [{"message": {"content": "", "reasoning_content": "sto pensando"}}]}
        )

    monkeypatch.setattr(mcp_server, "DEEPSEEK_API_KEY", "sk-finta")
    monkeypatch.setattr(mcp_server.requests, "post", finta_post)

    with pytest.raises(RuntimeError, match="non ha prodotto contenuto"):
        mcp_server.call_deepseek("deepseek-v4-flash", "crash", "sys", max_tokens=10)
