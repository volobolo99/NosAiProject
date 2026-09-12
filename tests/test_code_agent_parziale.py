"""Criterio di accettazione della modifica parziale di scripts/code_agent.py.

Misurato il 2026-09-11: la catena chiede al modello di riemettere il file intero.
Su scripts/model_scout.py, 26.693 byte, servono circa 7.418 token di uscita contro
un budget di 8.192: il modello ha emesso la sola funzione cambiata e il validatore
l'ha respinta con "Funzioni scomparse rispetto allo scheletro" elencandone quindici.

Il limite non e' del modello: e' del protocollo. Un file grande non e' delegabile
finche' l'unica forma ammessa e' la riemissione completa, e il prodotto ha file
molto piu' grandi di 26 KB.

Con solo_funzioni nell'incarico il modello emette soltanto le funzioni dichiarate,
code_agent le innesta nello scheletro e valida il file risultante per intero: le
garanzie non cambiano, cambia quanto deve scrivere il modello.
"""
from __future__ import annotations

import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))

import code_agent  # noqa: E402


SCHELETRO = '''"""Modulo di prova."""
from __future__ import annotations

COSTANTE = 3


def prima(a: int) -> int:
    """Resta come sta."""
    return a * COSTANTE


def bersaglio(valore: str) -> str:
    """Da implementare."""
    raise NotImplementedError


def dopo(b: int) -> int:
    """Resta come sta."""
    return b + 1
'''

SOLO_BERSAGLIO = '''def bersaglio(valore: str) -> str:
    """Da implementare."""
    return valore.upper()
'''


def test_innesta_una_funzione_e_conserva_le_altre():
    unito = code_agent.innesta_funzioni(SCHELETRO, SOLO_BERSAGLIO, ["bersaglio"])
    assert "def prima(a: int) -> int:" in unito
    assert "def dopo(b: int) -> int:" in unito
    assert "return valore.upper()" in unito
    assert "raise NotImplementedError" not in unito
    assert "COSTANTE = 3" in unito
    assert '"""Modulo di prova."""' in unito


def test_il_file_innestato_e_sintatticamente_valido():
    import ast
    ast.parse(code_agent.innesta_funzioni(SCHELETRO, SOLO_BERSAGLIO, ["bersaglio"]))


def test_l_ordine_delle_funzioni_non_cambia():
    unito = code_agent.innesta_funzioni(SCHELETRO, SOLO_BERSAGLIO, ["bersaglio"])
    assert unito.index("def prima") < unito.index("def bersaglio") < unito.index("def dopo")


def test_una_funzione_non_dichiarata_non_viene_innestata():
    """Il perimetro e' l'incarico: cio' che non e' dichiarato non entra."""
    intruso = SOLO_BERSAGLIO + '\n\ndef prima(a: int) -> int:\n    return 999\n'
    unito = code_agent.innesta_funzioni(SCHELETRO, intruso, ["bersaglio"])
    assert "return 999" not in unito, "prima non era nel perimetro dichiarato"
    assert "return a * COSTANTE" in unito


def test_una_funzione_dichiarata_e_assente_dalla_risposta_solleva():
    with pytest.raises(ValueError) as exc:
        code_agent.innesta_funzioni(SCHELETRO, SOLO_BERSAGLIO, ["bersaglio", "mancante"])
    assert "mancante" in str(exc.value)


def test_una_funzione_dichiarata_e_assente_dallo_scheletro_solleva():
    nuova = 'def aggiunta(x: int) -> int:\n    return x\n'
    with pytest.raises(ValueError) as exc:
        code_agent.innesta_funzioni(SCHELETRO, nuova, ["aggiunta"])
    assert "aggiunta" in str(exc.value)


def test_la_risposta_con_sintassi_rotta_solleva():
    with pytest.raises(SyntaxError):
        code_agent.innesta_funzioni(SCHELETRO, "def bersaglio(v)\n    return v", ["bersaglio"])


def test_innesta_piu_funzioni_in_un_colpo():
    due = (
        'def bersaglio(valore: str) -> str:\n    return valore.lower()\n\n\n'
        'def dopo(b: int) -> int:\n    return b + 2\n'
    )
    unito = code_agent.innesta_funzioni(SCHELETRO, due, ["bersaglio", "dopo"])
    assert "return valore.lower()" in unito
    assert "return b + 2" in unito
    assert "return b + 1" not in unito
    assert "return a * COSTANTE" in unito


def test_il_prompt_parziale_chiede_solo_le_funzioni_dichiarate():
    prompt = code_agent.prompt_parziale("CONTRATTO", "x.py", SCHELETRO, ["bersaglio"])
    assert "bersaglio" in prompt
    assert "SOLO" in prompt.upper(), "il modello deve capire che non serve il file intero"


def test_senza_solo_funzioni_il_comportamento_non_cambia():
    """Chi non dichiara il perimetro continua a ricevere il file intero."""
    assert code_agent.innesta_funzioni(SCHELETRO, SCHELETRO, []) == SCHELETRO


# --- innesto parziale in C# (C-311): innesta_funzioni usava ast.parse, Python
# puro, quindi solleva sempre SyntaxError su un file .cs. I test seguenti
# coprono _intervalli_funzioni_csharp e il dispatch per linguaggio in
# innesta_funzioni. Le classi qui sotto sono dentro un namespace annidato di
# proposito: un tentativo precedente cercava solo fra i figli diretti della
# radice dell'albero e non avrebbe mai trovato questi metodi.

SCHELETRO_CS = '''namespace NosAi.Runtime.Tactical
{
    public class AutoplayCommand
    {
        private static int Prima(int a)
        {
            return a * 2;
        }

        private static int Bersaglio(string valore)
        {
            throw new NotImplementedException();
        }

        private static int Dopo(int b)
        {
            return b + 1;
        }
    }
}
'''

SOLO_BERSAGLIO_CS = '''private static int Bersaglio(string valore)
{
    return valore.Length;
}
'''

SCHELETRO_CS_AMBIGUO = '''namespace NosAi.Runtime.Tactical
{
    public class Uno
    {
        private static int Bersaglio(int a) { return a; }
    }

    public class Due
    {
        private static int Bersaglio(int a) { return a + 1; }
    }
}
'''


def test_intervalli_funzioni_csharp_trova_metodi_dentro_un_namespace():
    intervalli = code_agent._intervalli_funzioni_csharp(SCHELETRO_CS)
    assert set(intervalli) == {"Prima", "Bersaglio", "Dopo"}
    inizio, fine = intervalli["Prima"]
    righe = SCHELETRO_CS.splitlines()
    assert "Prima" in righe[inizio - 1]
    assert righe[fine - 1].strip() == "}"


# Trovato su AutoplayCommand.cs reale (C-310): un metodo il cui tipo di ritorno
# non e' primitivo (qui "Risultato") ha DUE identifier diretti nei children del
# suo method_declaration -- il tipo di ritorno e il vero nome. Un rilevatore che
# prende "il primo identifier" cattura il tipo di ritorno, non il nome del
# metodo, e nel caso reale quel tipo di ritorno era anche il nome di un record
# esistente altrove nello stesso file: "metodo ambiguo, presente in piu' di un
# contenitore" per un file che non ha alcuna vera ambiguita'.
SCHELETRO_CS_TIPO_DI_RITORNO_NON_PRIMITIVO = '''namespace NosAi.Runtime.Tactical
{
    public sealed record Risultato(int Valore);

    public class AutoplayCommand
    {
        public static Risultato Bersaglio(int a)
        {
            return new Risultato(a);
        }
    }
}
'''


def test_intervalli_funzioni_csharp_non_confonde_il_tipo_di_ritorno_col_nome():
    intervalli = code_agent._intervalli_funzioni_csharp(SCHELETRO_CS_TIPO_DI_RITORNO_NON_PRIMITIVO)
    assert "Bersaglio" in intervalli
    assert "Risultato" not in intervalli


def test_innesta_funzioni_csharp_sostituisce_un_metodo_e_conserva_gli_altri():
    unito = code_agent.innesta_funzioni(SCHELETRO_CS, SOLO_BERSAGLIO_CS, ["Bersaglio"], "c_sharp")
    assert "return a * 2;" in unito
    assert "return b + 1;" in unito
    assert "return valore.Length;" in unito
    assert "NotImplementedException" not in unito


def test_innesta_funzioni_csharp_nome_assente_dalla_risposta_solleva():
    with pytest.raises(ValueError) as exc:
        code_agent.innesta_funzioni(SCHELETRO_CS, SOLO_BERSAGLIO_CS, ["Bersaglio", "Mancante"], "c_sharp")
    assert "Mancante" in str(exc.value)


def test_innesta_funzioni_csharp_nome_assente_dallo_scheletro_solleva():
    nuovo = "private static int Aggiunta(int x)\n{\n    return x;\n}\n"
    with pytest.raises(ValueError) as exc:
        code_agent.innesta_funzioni(SCHELETRO_CS, nuovo, ["Aggiunta"], "c_sharp")
    assert "Aggiunta" in str(exc.value)


def test_innesta_funzioni_csharp_sintassi_rotta_solleva():
    with pytest.raises(SyntaxError):
        code_agent.innesta_funzioni(SCHELETRO_CS, "private static int Bersaglio(string v) {", ["Bersaglio"], "c_sharp")


def test_intervalli_funzioni_csharp_metodo_ambiguo_fra_due_classi_solleva():
    with pytest.raises(ValueError) as exc:
        code_agent._intervalli_funzioni_csharp(SCHELETRO_CS_AMBIGUO)
    assert "Bersaglio" in str(exc.value)


def test_run_con_solo_funzioni_su_file_csharp_non_fallisce_piu_per_innesto(monkeypatch, tmp_path):
    """Criterio di accettazione di C-311: prima di questa correzione, qualunque
    incarico C# con solo_funzioni falliva con 'Innesto parziale rifiutato' per
    un SyntaxError che non era un vero errore di sintassi C#, solo il fatto che
    ast.parse non sa leggere C#."""
    target = tmp_path / "Esempio.cs"
    target.write_text(SCHELETRO_CS, encoding="utf-8")
    contract_file = tmp_path / "contratto.json"
    contract_file.write_text("{}", encoding="utf-8")

    monkeypatch.setattr(code_agent, "call_model", lambda model, prompt, sistema, max_tokens: (SOLO_BERSAGLIO_CS, {}))
    monkeypatch.setattr(code_agent, "build_check", lambda path: [])
    monkeypatch.setattr(code_agent, "record", lambda entry: None)

    task = {
        "task_id": "prova-csharp-solo-funzioni",
        "file": str(target.relative_to(code_agent.ROOT)) if target.is_relative_to(code_agent.ROOT) else str(target),
        "contract_file": str(contract_file.relative_to(code_agent.ROOT)) if contract_file.is_relative_to(code_agent.ROOT) else str(contract_file),
        "tier": "local",
        "solo_funzioni": ["Bersaglio"],
    }

    risultato = code_agent.run(task, do_preflight=False)

    assert not any("Innesto parziale rifiutato" in e for e in risultato["missing_items"])
    assert risultato["status"] == "completed"


def test_run_non_eredita_l_errore_di_un_tentativo_precedente(monkeypatch, tmp_path):
    """Trovato su C-310 reale: al tentativo 2 il modello rispondeva con prosa
    invece di codice (innesto rifiutato per sintassi), al tentativo 3 con
    codice valido -- ma errors non veniva mai azzerato a inizio tentativo,
    quindi un innesto riuscito al tentativo N ereditava l'errore, ancora non
    svuotato, del tentativo N-1, saltava validate_implementation (la guardia
    'if not (solo and errors)') e il risultato finale restava quello vecchio."""
    target = tmp_path / "Esempio.cs"
    target.write_text(SCHELETRO_CS, encoding="utf-8")
    contract_file = tmp_path / "contratto.json"
    contract_file.write_text("{}", encoding="utf-8")

    risposte = iter(["questo non e' codice, e' prosa che descrive cosa fa il metodo", SOLO_BERSAGLIO_CS])
    monkeypatch.setattr(code_agent, "call_model", lambda model, prompt, sistema, max_tokens: (next(risposte), {}))
    monkeypatch.setattr(code_agent, "build_check", lambda path: [])
    monkeypatch.setattr(code_agent, "record", lambda entry: None)

    task = {
        "task_id": "prova-csharp-due-tentativi",
        "file": str(target.relative_to(code_agent.ROOT)) if target.is_relative_to(code_agent.ROOT) else str(target),
        "contract_file": str(contract_file.relative_to(code_agent.ROOT)) if contract_file.is_relative_to(code_agent.ROOT) else str(contract_file),
        "tier": "local",
        "solo_funzioni": ["Bersaglio"],
    }

    risultato = code_agent.run(task, do_preflight=False)

    assert risultato["status"] == "completed", risultato["missing_items"]
    assert risultato["missing_items"] == []
