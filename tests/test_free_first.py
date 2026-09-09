"""Criterio di accettazione della cascata gratuita con giudizio di qualita'.

Il modulo non decide se un testo sia buono: riceve il giudizio dal chiamante e
si limita a non accettare mai un gratuito bocciato, a non interrompersi mai per
colpa di un gratuito, e a spendere solo quando il gratuito non conviene piu'.
Nessun test tocca la rete: chiamate e orologio sono iniettati.
"""
from __future__ import annotations

import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))

import free_first  # noqa: E402


SEI = ["m1", "m2", "m3", "m4", "m5", "m6"]


class Orologio:
    """Orologio finto: avanza solo quando glielo si chiede."""

    def __init__(self, adesso: float = 0.0) -> None:
        self.adesso = adesso

    def __call__(self) -> float:
        return self.adesso

    def avanza(self, secondi: float) -> None:
        self.adesso += secondi


def promosso(_testo: str) -> list:
    return []


def bocciato(_testo: str) -> list:
    return ["il giudizio boccia sempre"]


def pagato_ok():
    return ("testo del pagato", "modello/pagato")


# ------------------------------------------------------------ budget_residuo


def test_budget_residuo_scala_col_tempo():
    assert free_first.budget_residuo(10.0, 30.0, 10.0) == 30.0
    assert free_first.budget_residuo(10.0, 30.0, 25.0) == 15.0


def test_budget_residuo_non_va_sotto_zero():
    assert free_first.budget_residuo(10.0, 30.0, 100.0) == 0.0


# -------------------------------------------------------------------- Pausa


def test_pausa_esclude_finche_dura_e_poi_riammette():
    p = free_first.Pausa()
    assert p.attivo("m1", 0.0) is False
    p.attiva("m1", 60.0, adesso=0.0)
    assert p.attivo("m1", 30.0) is True
    assert p.attivo("m1", 60.0) is False
    assert p.attivo("m2", 30.0) is False


def test_pausa_filtra_conserva_ordine():
    p = free_first.Pausa()
    p.attiva("m2", 60.0, adesso=0.0)
    assert p.filtra(["m1", "m2", "m3"], adesso=10.0) == ["m1", "m3"]
    assert p.filtra(["m1", "m2", "m3"], adesso=99.0) == ["m1", "m2", "m3"]


# -------------------------------------------------- la cascata accetta e ferma


def test_il_primo_gratuito_promosso_vince_e_non_si_spende():
    speso = []

    def chiama_gruppo(gruppo):
        return ("va bene", gruppo[0])

    def pagato():
        speso.append(1)
        return pagato_ok()

    e = free_first.esegui(chiama_gruppo, pagato, promosso, modelli=SEI, orologio=Orologio())
    assert e.testo == "va bene"
    assert e.gratuito is True
    assert e.accettato is True
    assert e.modello == "m1"
    assert speso == []
    assert len(e.tentativi) == 1


def test_il_giudizio_negativo_fa_proseguire_al_gruppo_successivo():
    visti = []

    def chiama_gruppo(gruppo):
        visti.append(list(gruppo))
        return ("scadente" if gruppo[0] == "m1" else "buono", gruppo[0])

    def giudizio(testo):
        return [] if testo == "buono" else ["scadente"]

    e = free_first.esegui(chiama_gruppo, pagato_ok, giudizio, modelli=SEI, orologio=Orologio())
    assert visti == [["m1", "m2", "m3"], ["m4", "m5", "m6"]]
    assert e.gratuito is True
    assert e.modello == "m4"
    assert e.tentativi[0].accettato is False
    assert e.tentativi[1].accettato is True


def test_un_gratuito_bocciato_non_viene_mai_restituito():
    """La qualita' non cala: se tutti i gratuiti sono bocciati si spende."""

    def chiama_gruppo(gruppo):
        return ("robaccia", gruppo[0])

    e = free_first.esegui(chiama_gruppo, pagato_ok, bocciato, modelli=SEI, orologio=Orologio())
    assert e.testo == "testo del pagato"
    assert e.gratuito is False
    assert "robaccia" not in e.testo


# --------------------------------------------- nessuna interruzione di servizio


def test_l_eccezione_di_un_gratuito_non_arriva_al_chiamante():
    def chiama_gruppo(gruppo):
        if gruppo[0] == "m1":
            raise ConnectionError("il provider non risponde")
        return ("buono", gruppo[0])

    e = free_first.esegui(chiama_gruppo, pagato_ok, promosso, modelli=SEI, orologio=Orologio())
    assert e.gratuito is True
    assert e.modello == "m4"
    assert e.tentativi[0].accettato is False
    assert "il provider non risponde" in e.tentativi[0].motivo


def test_tutti_i_gratuiti_rotti_si_ricade_sul_pagato():
    def chiama_gruppo(gruppo):
        raise ConnectionError("non risponde")

    e = free_first.esegui(chiama_gruppo, pagato_ok, promosso, modelli=SEI, orologio=Orologio())
    assert e.gratuito is False
    assert e.modello == "modello/pagato"
    assert e.accettato is True


def test_elenco_di_gratuiti_vuoto_va_diritto_al_pagato():
    def chiama_gruppo(gruppo):
        raise AssertionError("non doveva essere interpellato")

    e = free_first.esegui(chiama_gruppo, pagato_ok, promosso, modelli=[], orologio=Orologio())
    assert e.gratuito is False
    assert e.modello == "modello/pagato"


def test_se_anche_il_pagato_fallisce_l_errore_porta_il_registro():
    def chiama_gruppo(gruppo):
        raise ConnectionError("non risponde")

    def pagato():
        raise ConnectionError("anche il pagato non risponde")

    with pytest.raises(RuntimeError) as errore:
        free_first.esegui(chiama_gruppo, pagato, promosso, modelli=SEI, orologio=Orologio())
    messaggio = str(errore.value)
    assert "m1" in messaggio
    assert "anche il pagato non risponde" in messaggio


# --------------------------------------------------------------- il budget


def test_il_budget_esaurito_ferma_la_cascata_e_si_spende():
    orologio = Orologio()

    def chiama_gruppo(gruppo):
        orologio.avanza(20.0)
        return ("scadente", gruppo[0])

    e = free_first.esegui(
        chiama_gruppo, pagato_ok, bocciato, modelli=SEI, budget_secondi=25.0, orologio=orologio
    )
    assert e.gratuito is False
    motivi = [t.motivo for t in e.tentativi if not t.accettato]
    assert any("budget" in m.lower() for m in motivi)


def test_budget_a_zero_salta_del_tutto_i_gratuiti():
    def chiama_gruppo(gruppo):
        raise AssertionError("con budget nullo non si interpella nessun gratuito")

    e = free_first.esegui(
        chiama_gruppo, pagato_ok, promosso, modelli=SEI, budget_secondi=0.0, orologio=Orologio()
    )
    assert e.gratuito is False


def test_il_budget_non_si_applica_al_pagato():
    """Il pagato e' la rete di sicurezza: si tenta anche a budget finito."""
    orologio = Orologio()

    def chiama_gruppo(gruppo):
        orologio.avanza(500.0)
        raise ConnectionError("non risponde")

    e = free_first.esegui(
        chiama_gruppo, pagato_ok, promosso, modelli=SEI, budget_secondi=1.0, orologio=orologio
    )
    assert e.modello == "modello/pagato"
    assert e.accettato is True


# ------------------------------------------------------------ quota e pause


def test_il_429_mette_in_pausa_il_gruppo_e_la_cascata_prosegue():
    registro = free_first.Pausa()

    def chiama_gruppo(gruppo):
        if gruppo[0] == "m1":
            raise free_first.LimiteRaggiunto("429", attendi=45.0)
        return ("buono", gruppo[0])

    e = free_first.esegui(
        chiama_gruppo, pagato_ok, promosso, modelli=SEI, orologio=Orologio(), pausa=registro
    )
    assert e.gratuito is True
    assert e.modello == "m4"
    for m in ("m1", "m2", "m3"):
        assert registro.attivo(m, 44.0) is True
        assert registro.attivo(m, 46.0) is False


def test_i_modelli_in_pausa_sono_esclusi_prima_di_comporre_i_gruppi():
    registro = free_first.Pausa()
    registro.attiva("m1", 60.0, adesso=0.0)
    registro.attiva("m2", 60.0, adesso=0.0)
    visti = []

    def chiama_gruppo(gruppo):
        visti.append(list(gruppo))
        return ("buono", gruppo[0])

    e = free_first.esegui(
        chiama_gruppo, pagato_ok, promosso, modelli=SEI, orologio=Orologio(), pausa=registro
    )
    assert visti == [["m3", "m4", "m5"]]
    assert e.modello == "m3"


def test_tutti_in_pausa_si_va_diritto_al_pagato():
    registro = free_first.Pausa()
    for m in SEI:
        registro.attiva(m, 60.0, adesso=0.0)

    def chiama_gruppo(gruppo):
        raise AssertionError("nessun gratuito era disponibile")

    e = free_first.esegui(
        chiama_gruppo, pagato_ok, promosso, modelli=SEI, orologio=Orologio(), pausa=registro
    )
    assert e.gratuito is False


def test_la_pausa_sopravvive_fra_due_invocazioni():
    registro = free_first.Pausa()
    visti = []

    def chiama_gruppo(gruppo):
        visti.append(list(gruppo))
        if gruppo[0] == "m1":
            raise free_first.LimiteRaggiunto("429", attendi=300.0)
        return ("buono", gruppo[0])

    free_first.esegui(chiama_gruppo, pagato_ok, promosso, modelli=SEI,
                      orologio=Orologio(), pausa=registro)
    visti.clear()
    free_first.esegui(chiama_gruppo, pagato_ok, promosso, modelli=SEI,
                      orologio=Orologio(), pausa=registro)
    assert visti == [["m4", "m5", "m6"]]


# ----------------------------------------------------------- osservabilita'


def test_il_registro_elenca_ogni_modello_in_ordine():
    def chiama_gruppo(gruppo):
        return ("robaccia", gruppo[0])

    e = free_first.esegui(chiama_gruppo, pagato_ok, bocciato, modelli=SEI, orologio=Orologio())
    assert [t.modello for t in e.tentativi] == ["m1", "m4", "modello/pagato"]
    assert [t.gratuito for t in e.tentativi] == [True, True, False]


def test_accettato_riflette_il_giudizio_anche_sul_pagato():
    def chiama_gruppo(gruppo):
        raise ConnectionError("non risponde")

    e = free_first.esegui(chiama_gruppo, pagato_ok, bocciato, modelli=SEI, orologio=Orologio())
    assert e.gratuito is False
    assert e.accettato is False
    assert e.testo == "testo del pagato"


def test_i_secondi_misurano_il_tempo_totale():
    orologio = Orologio()

    def chiama_gruppo(gruppo):
        orologio.avanza(7.0)
        return ("buono", gruppo[0])

    e = free_first.esegui(chiama_gruppo, pagato_ok, promosso, modelli=SEI, orologio=orologio)
    assert e.secondi == 7.0
    assert e.tentativi[0].secondi == 7.0
