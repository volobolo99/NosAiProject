"""Test per local_generate_skeleton: il linguaggio del file bersaglio deve essere rilevato
dal contratto, non assunto. Prima di questa correzione un contratto .cs produceva
silenziosamente uno scheletro Python (task_a2736b3a).

Nessuna chiamata a Ollama: si testano solo le funzioni pure di rilevazione e costruzione
del prompt, separate dalla chiamata di rete.
"""

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "scripts"))

import mcp_server


def test_rileva_csharp_dalla_chiave_file():
    contratto = json.dumps({
        "contract_id": "esempio-001",
        "file": "src/NosAi.Runtime/Observability/DecisionTelemetryWriter.cs",
        "scopo": "prova",
    })
    assert mcp_server._rileva_linguaggio_bersaglio(contratto) == "csharp"


def test_rileva_cpp_dalla_chiave_target_file():
    contratto = json.dumps({"target_file": "scripts/run_asan_pipeline.hpp"})
    assert mcp_server._rileva_linguaggio_bersaglio(contratto) == "cpp"


def test_rileva_python_dalla_chiave_file_bersaglio():
    contratto = json.dumps({"file_bersaglio": "nosai/storage/volume.py"})
    assert mcp_server._rileva_linguaggio_bersaglio(contratto) == "python"


def test_rileva_dal_testo_grezzo_quando_il_json_non_e_valido():
    testo = 'Contratto per "src/NosAi.Runtime/Gate3/Gate3DecisionLoop.cs", canale 1'
    assert mcp_server._rileva_linguaggio_bersaglio(testo) == "csharp"


def test_nessuna_estensione_riconoscibile_restituisce_none():
    contratto = json.dumps({"file": "docs/senza_estensione"})
    assert mcp_server._rileva_linguaggio_bersaglio(contratto) is None


def test_nessuna_chiave_nota_e_nessun_percorso_nel_testo_restituisce_none():
    contratto = json.dumps({"scopo": "solo prosa, nessun percorso di file qui dentro"})
    assert mcp_server._rileva_linguaggio_bersaglio(contratto) is None


def test_prompt_csharp_menziona_notimplementedexception_non_lo_scheletro_python():
    contratto = json.dumps({"file": "src/NosAi.Runtime/Foo.cs"})
    prompt = mcp_server._prompt_scheletro(contratto)
    assert "NotImplementedException" in prompt
    assert "raise NotImplementedError" not in prompt


def test_prompt_python_menziona_raise_notimplementederror():
    contratto = json.dumps({"file": "nosai/foo.py"})
    prompt = mcp_server._prompt_scheletro(contratto)
    assert "raise NotImplementedError" in prompt
    assert "NotImplementedException" not in prompt


def test_prompt_senza_linguaggio_determinabile_chiede_di_fermarsi_invece_di_indovinare():
    contratto = json.dumps({"scopo": "nessun file citato"})
    prompt = mcp_server._prompt_scheletro(contratto)
    assert "fermati e dichiaralo" in prompt
