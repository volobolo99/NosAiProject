"""Test per _skeleton_system_prompt e local_generate_skeleton in scripts/mcp_server.py.

Verificato empiricamente il 2026-09-11: un contratto con "file": "qualcosa.cs" produceva
comunque uno scheletro Python, perche' il system prompt dello Skeleton Architect era
hardcoded su C++/Python e non guardava mai l'estensione dichiarata nel contratto. Il
progetto ha 564 sorgenti C# contro 78 Python (ADR-0030): e' il caso comune, non l'eccezione.
"""
import json
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "scripts"))

import mcp_server  # noqa: E402


class FakeResponse:
    def __init__(self, payload):
        self._payload = payload

    def raise_for_status(self):
        return None

    def json(self):
        return self._payload


@pytest.fixture(autouse=True)
def niente_ledger_reale(monkeypatch):
    """local_generate_skeleton chiama record(), che scrive su data/ai_task_ledger.jsonl:
    i test non devono toccare il ledger reale del progetto."""
    monkeypatch.setattr(mcp_server, "record", lambda entry: None)


def test_prompt_csharp_per_estensione_cs():
    prompt = mcp_server._skeleton_system_prompt(
        json.dumps({"file": "src/NosAi.Runtime/Configuration/RoleBindingConfiguration.cs"})
    )
    for marcatore in ("C#", "namespace", "using", "class", "NotImplementedException"):
        assert marcatore in prompt, marcatore
    assert "def " not in prompt
    assert "import " not in prompt
    assert "raise NotImplementedError" not in prompt


def test_prompt_generico_per_estensione_py():
    prompt = mcp_server._skeleton_system_prompt(json.dumps({"file": "scripts/qualcosa.py"}))
    assert "raise NotImplementedError" in prompt
    assert "C#/.NET" not in prompt


def test_prompt_generico_per_estensione_cpp():
    prompt = mcp_server._skeleton_system_prompt(json.dumps({"file": "third_party/foo.hpp"}))
    assert "header C++" in prompt
    assert "C#/.NET" not in prompt


def test_prompt_generico_se_manca_il_campo_file():
    prompt = mcp_server._skeleton_system_prompt(json.dumps({"altro": "valore"}))
    assert "C#/.NET" not in prompt


def test_prompt_generico_se_il_json_e_malformato():
    prompt = mcp_server._skeleton_system_prompt("{non e' json valido")
    assert "C#/.NET" not in prompt


def test_local_generate_skeleton_invia_il_prompt_csharp_a_ollama(monkeypatch):
    visto = {}

    def finta_post(url, json=None, timeout=None):
        visto["prompt"] = json["prompt"]
        return FakeResponse({"response": "namespace NosAi.Runtime.Configuration { public class Foo { } }"})

    monkeypatch.setattr(mcp_server.requests, "post", finta_post)

    contratto = json.dumps({"file": "src/NosAi.Runtime/Configuration/RoleBindingConfiguration.cs"})
    mcp_server.local_generate_skeleton(contratto)

    assert "namespace" in visto["prompt"]
    assert "using" in visto["prompt"]
    assert "class" in visto["prompt"]
    assert "def " not in visto["prompt"]
    assert "import " not in visto["prompt"]


def test_local_generate_skeleton_resta_invariato_per_py(monkeypatch):
    visto = {}

    def finta_post(url, json=None, timeout=None):
        visto["prompt"] = json["prompt"]
        return FakeResponse({"response": "class Foo:\n    pass"})

    monkeypatch.setattr(mcp_server.requests, "post", finta_post)

    contratto = json.dumps({"file": "scripts/qualcosa.py"})
    mcp_server.local_generate_skeleton(contratto)

    assert "raise NotImplementedError" in visto["prompt"]
    assert "C#/.NET" not in visto["prompt"]
