"""Criterio di accettazione del contratto C-315, contracts/python-partial-graft-methods-021.json.

Il rilevatore Python guardava solo `albero.body`, quindi ogni metodo dentro una
classe risultava assente dallo scheletro e ogni incarico con `solo_funzioni` su
un metodo veniva rifiutato. Il rilevatore C# cammina tutto l'albero dal
contratto C-311: qui il Python lo raggiunge, con la stessa regola
sull'ambiguita'.
"""
from __future__ import annotations

import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))

import code_agent  # noqa: E402


SCHELETRO_METODI = '''"""Modulo di prova con metodi."""
from __future__ import annotations


def libera(valore: int) -> int:
    """Funzione di modulo."""
    return valore + 1


class Contenitore:
    """Classe di prova."""

    def prima(self, valore: int) -> int:
        """Metodo che precede il bersaglio."""
        return valore * 2

    @staticmethod
    def bersaglio(valore: int) -> int:
        """Metodo da sostituire."""
        raise NotImplementedError("da implementare")

    def dopo(self, valore: str) -> int:
        """Metodo che segue il bersaglio."""
        return len(valore)
'''

SOLO_BERSAGLIO = '''    @staticmethod
    def bersaglio(valore: int) -> int:
        """Metodo da sostituire."""
        return valore * 3
'''

SCHELETRO_CHIUSURA = '''from __future__ import annotations


def esterna(valore: int) -> int:
    def interna(x: int) -> int:
        return x + 1

    return interna(valore)


class Altra:
    def metodo(self) -> int:
        def interna(x: int) -> int:
            return x - 1

        return interna(3)
'''

SCHELETRO_OMBRA = '''from __future__ import annotations


def bersaglio(valore: int) -> int:
    return valore


class Contenitore:
    def bersaglio(self, valore: int) -> int:
        return valore * 2
'''

SCHELETRO_AMBIGUO = '''from __future__ import annotations


class Prima:
    def bersaglio(self) -> int:
        return 1


class Seconda:
    def bersaglio(self) -> int:
        return 2
'''


def test_un_metodo_dentro_una_classe_compare_fra_gli_intervalli():
    intervalli = code_agent._intervalli_funzioni(SCHELETRO_METODI)

    assert {"libera", "prima", "bersaglio", "dopo"} <= set(intervalli)
    inizio, fine = intervalli["prima"]
    righe = SCHELETRO_METODI.splitlines()
    assert righe[inizio - 1].strip().startswith("def prima")
    assert righe[fine - 1].strip() == "return valore * 2"


def test_un_metodo_decorato_parte_dalla_riga_del_decoratore():
    inizio, _ = code_agent._intervalli_funzioni(SCHELETRO_METODI)["bersaglio"]

    assert SCHELETRO_METODI.splitlines()[inizio - 1].strip() == "@staticmethod"


def test_le_funzioni_di_primo_livello_restano_visibili():
    intervalli = code_agent._intervalli_funzioni(SCHELETRO_METODI)

    inizio, fine = intervalli["libera"]
    righe = SCHELETRO_METODI.splitlines()
    assert righe[inizio - 1].strip().startswith("def libera")
    assert righe[fine - 1].strip() == "return valore + 1"


def test_la_funzione_di_modulo_vince_sul_metodo_omonimo():
    intervalli = code_agent._intervalli_funzioni(SCHELETRO_OMBRA)

    inizio, _ = intervalli["bersaglio"]
    assert SCHELETRO_OMBRA.splitlines()[inizio - 1].strip() == "def bersaglio(valore: int) -> int:"


def test_lo_stesso_metodo_in_due_classi_solleva():
    with pytest.raises(ValueError) as exc:
        code_agent._intervalli_funzioni(SCHELETRO_AMBIGUO)

    assert "bersaglio" in str(exc.value)


def test_una_chiusura_dentro_una_funzione_non_compare():
    intervalli = code_agent._intervalli_funzioni(SCHELETRO_CHIUSURA)

    assert "interna" not in intervalli
    assert {"esterna", "metodo"} <= set(intervalli)


def test_innesta_il_corpo_di_un_metodo_e_conserva_il_resto():
    unito = code_agent.innesta_funzioni(SCHELETRO_METODI, SOLO_BERSAGLIO, ["bersaglio"])

    assert "return valore * 3" in unito
    assert "return valore * 2" in unito
    assert "return len(valore)" in unito
    assert "return valore + 1" in unito
    assert "NotImplementedError" not in unito
    compilato = compile(unito, "<innesto>", "exec")
    assert compilato is not None


def test_una_sorgente_rotta_continua_a_sollevare_syntaxerror():
    with pytest.raises(SyntaxError):
        code_agent._intervalli_funzioni("class Rotta:\n    def x(self) ->\n")
