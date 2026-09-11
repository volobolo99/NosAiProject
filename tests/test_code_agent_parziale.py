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
