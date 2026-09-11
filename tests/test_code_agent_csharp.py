"""Criterio di accettazione del percorso C# di scripts/code_agent.py.

Misurato il 2026-09-11: validate_implementation partiva da ast.parse(), il parser
di Python, quindi su codice C# sollevava SyntaxError. Conseguenza: la catena a
cinque fasi non poteva toccare una sola riga delle 188.762 righe di C# del
prodotto, e infatti nessun contratto ha mai avuto come bersaglio un file .cs.
Il protocollo lavorava sull'11% del progetto.

La grammatica c'e' gia': scripts/build_function_index.py dichiara .cs -> c_sharp
e usa tree_sitter. Mancava il collegamento in code_agent.py.
"""
from __future__ import annotations

import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))

import code_agent  # noqa: E402


SCHELETRO_CS = """using System;
using System.IO;

namespace NosAi.Runtime.Testing;

public static class Esempio
{
    public static string? TrovaRadice(string? start = null)
    {
        throw new NotImplementedException();
    }

    private static int Conta(int a, string b)
    {
        throw new NotImplementedException();
    }
}
"""

IMPLEMENTATO_CS = """using System;
using System.IO;

namespace NosAi.Runtime.Testing;

public static class Esempio
{
    public static string? TrovaRadice(string? start = null)
    {
        var dir = new DirectoryInfo(start ?? AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "NosAi.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    private static int Conta(int a, string b)
    {
        return a + b.Length;
    }
}
"""


def _task(nome: str = "src/Esempio.cs") -> dict:
    return {"task_id": "prova", "file": nome, "allow_stub": []}


# --- riconoscimento del linguaggio ------------------------------------------

def test_linguaggio_dal_suffisso():
    assert code_agent.linguaggio_del_file(Path("a/b.py")) == "python"
    assert code_agent.linguaggio_del_file(Path("a/b.cs")) == "c_sharp"


def test_suffisso_ignoto_solleva():
    """Meglio fermarsi che validare codice con il parser sbagliato."""
    with pytest.raises(ValueError):
        code_agent.linguaggio_del_file(Path("a/b.rs"))


# --- il percorso Python non cambia ------------------------------------------

def test_il_percorso_python_resta_intatto():
    scheletro = "def f(a: int) -> str:\n    raise NotImplementedError\n"
    buono = "def f(a: int) -> str:\n    return str(a)\n"
    assert code_agent.validate_implementation(buono, scheletro, _task("scripts/x.py")) == []


def test_firma_python_alterata_ancora_bocciata():
    scheletro = "def f(a: int) -> str:\n    raise NotImplementedError\n"
    cattivo = "def f(a: str) -> str:\n    return a\n"
    errori = code_agent.validate_implementation(cattivo, scheletro, _task("scripts/x.py"))
    assert errori, "una firma alterata in Python deve restare un errore"


# --- il percorso C# ---------------------------------------------------------

def test_csharp_conforme_e_accettato():
    assert code_agent.validate_implementation(IMPLEMENTATO_CS, SCHELETRO_CS, _task()) == []


def test_csharp_sintassi_rotta_e_bocciata():
    rotto = IMPLEMENTATO_CS.replace("return null;\n    }", "return null;")
    errori = code_agent.validate_implementation(rotto, SCHELETRO_CS, _task())
    assert errori, "una parentesi non chiusa in C# deve essere un errore"


def test_csharp_metodo_scomparso_e_bocciato():
    """Stessa regola del Python: l'infilling aggiunge e riempie, non toglie."""
    senza = IMPLEMENTATO_CS.split("    private static int Conta")[0] + "}\n"
    errori = code_agent.validate_implementation(senza, SCHELETRO_CS, _task())
    assert errori
    assert any("Conta" in e for e in errori), errori


def test_csharp_firma_alterata_e_bocciata():
    alterato = IMPLEMENTATO_CS.replace(
        "private static int Conta(int a, string b)",
        "private static int Conta(string a, int b)")
    errori = code_agent.validate_implementation(alterato, SCHELETRO_CS, _task())
    assert errori
    assert any("Conta" in e for e in errori), errori


def test_csharp_tipo_di_ritorno_alterato_e_bocciato():
    alterato = IMPLEMENTATO_CS.replace(
        "private static int Conta(int a, string b)",
        "private static long Conta(int a, string b)")
    errori = code_agent.validate_implementation(alterato, SCHELETRO_CS, _task())
    assert errori
    assert any("Conta" in e for e in errori), errori


def test_csharp_classe_scomparsa_e_bocciata():
    errori = code_agent.validate_implementation(
        "using System;\n", SCHELETRO_CS, _task())
    assert errori
    assert any("Esempio" in e for e in errori), errori


def test_csharp_corpo_ancora_non_implementato_e_bocciato():
    """NotImplementedException e' il segnaposto del C#: non e' un'implementazione."""
    errori = code_agent.validate_implementation(SCHELETRO_CS, SCHELETRO_CS, _task())
    assert errori, "uno scheletro restituito identico non e' un'implementazione"


def test_csharp_un_metodo_nuovo_e_permesso():
    """Aggiungere un helper privato non altera il contratto."""
    piu_uno = IMPLEMENTATO_CS.replace(
        "    private static int Conta(int a, string b)",
        "    private static bool Aiuto() => true;\n\n    private static int Conta(int a, string b)")
    assert code_agent.validate_implementation(piu_uno, SCHELETRO_CS, _task()) == []


# --- controllo di compilazione ----------------------------------------------

def test_build_check_smista_per_linguaggio():
    """Un .cs non si verifica con 'import': si verifica compilando."""
    assert hasattr(code_agent, "build_check")


def test_import_check_resta_disponibile():
    """Il percorso Python preesistente non viene rimosso."""
    assert hasattr(code_agent, "import_check")
