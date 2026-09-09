"""Controlli sullo scheletro Python prodotto dal modello locale.

Un incarico che chiede una costante deve poter essere soddisfatto: se il
validatore non riconosce le assegnazioni di modulo, ogni tentativo finisce
in blocked e la catena spreca i suoi tre giri senza mai poter riuscire.
"""
from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))

import doc_agent  # noqa: E402


SCHELETRO = '''"""Modulo di prova."""
from pathlib import Path
from typing import List

FREE_ROSTER_PATH = Path("scripts/free_roster.json")
LENTO_SECONDI: int = 200


class Catena:
    """Una classe."""


def free_models(escludi_lenti: bool = True) -> List[str]:
    """Elenca i modelli."""
    raise NotImplementedError
'''


def test_le_costanti_di_modulo_contano_come_simboli():
    ok, errori = doc_agent.validate_python_skeleton(
        SCHELETRO, {"required_symbols": ["FREE_ROSTER_PATH", "LENTO_SECONDI"]}
    )
    assert ok is True, errori


def test_riconosce_anche_funzioni_e_classi():
    ok, errori = doc_agent.validate_python_skeleton(
        SCHELETRO, {"required_symbols": ["free_models", "Catena"]}
    )
    assert ok is True, errori


def test_un_simbolo_davvero_assente_viene_segnalato():
    ok, errori = doc_agent.validate_python_skeleton(
        SCHELETRO, {"required_symbols": ["NON_ESISTE"]}
    )
    assert ok is False
    assert any("NON_ESISTE" in e for e in errori)


def test_lo_scheletro_con_logica_resta_rifiutato():
    con_logica = SCHELETRO.replace(
        '    """Elenca i modelli."""\n    raise NotImplementedError',
        '    """Elenca i modelli."""\n    dati = [1, 2, 3]\n    return [str(d) for d in dati]',
    )
    ok, errori = doc_agent.validate_python_skeleton(con_logica, {"required_symbols": []})
    assert ok is False
    assert any("logica implementata" in e for e in errori)


def test_i_segnaposto_restano_vietati():
    """Un modello che deve far passare il proprio codice puo' essere tentato di
    disarmare il controllo invece di correggersi: qui si verifica che le parole
    vietate siano ancora intercettate."""
    for parola in ("TODO", "TBD", "FIXME", "XXX", "PLACEHOLDER", "da definire", "inserire qui"):
        assert doc_agent.FORBIDDEN.search("testo con {} dentro".format(parola)), parola


def test_un_testo_pulito_non_viene_segnalato():
    assert doc_agent.FORBIDDEN.search("Questo documento e' completo e verificato.") is None
