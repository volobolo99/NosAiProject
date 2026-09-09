"""Criterio di accettazione della cascata gratuita dentro il server MCP.

Tre vincoli, in ordine di durezza. La qualita' non cala: un testo gratuito
entra in produzione solo se supera lo stesso giudizio che si applicherebbe al
pagato. Il servizio non si interrompe: qualunque cosa combinino i gratuiti, il
tool risponde. Il tempo si spende solo dove rende: chi e' piu' veloce va provato
per primo, e il budget chiude la cascata quando non conviene piu'.

Nessun test tocca la rete.
"""
from __future__ import annotations

import sys
from pathlib import Path

import pytest

RADICE = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(RADICE / "scripts"))

import free_first  # noqa: E402
import mcp_server  # noqa: E402


SCHELETRO = '''from typing import Optional


def somma(a: int, b: int) -> int:
    raise NotImplementedError


def meta(valore: int) -> Optional[int]:
    raise NotImplementedError
'''


# ------------------------------------------------- l'ordine dei fornitori


def test_i_gratuiti_piu_veloci_vengono_provati_per_primi():
    modelli = mcp_server.modelli_gratuiti()
    assert modelli, "senza gratuiti la cascata non ha senso"
    groq = [m for m in modelli if m.startswith("groq:")]
    assert groq, "il roster non offre alcun modello Groq"
    # I Groq sono stati misurati sotto il secondo: devono precedere gli altri.
    primo_non_groq = next(
        (i for i, m in enumerate(modelli) if not m.startswith("groq:")), len(modelli)
    )
    assert all(modelli.index(g) < primo_non_groq for g in groq)


def test_il_roster_gratuito_non_contiene_modelli_a_pagamento():
    a_pagamento = set(mcp_server.ROSTER.values())
    for modello in mcp_server.modelli_gratuiti():
        assert modello not in a_pagamento


# ------------------------------------------- il giudizio sull'implementazione


def test_il_giudizio_boccia_i_corpi_non_implementati():
    giudizio = mcp_server.giudizio_infill(SCHELETRO)
    codice = SCHELETRO  # nessun corpo riempito
    assert giudizio(codice), "uno scheletro restituito tale e quale deve essere bocciato"


def test_il_giudizio_boccia_i_segnaposto():
    giudizio = mcp_server.giudizio_infill(SCHELETRO)
    codice = '''from typing import Optional


def somma(a: int, b: int) -> int:
    return a + b  # TODO controllare l'overflow


def meta(valore: int) -> Optional[int]:
    return valore // 2
'''
    assert any("egnaposto" in e or "TODO" in e for e in giudizio(codice))


def test_il_giudizio_boccia_una_firma_alterata():
    giudizio = mcp_server.giudizio_infill(SCHELETRO)
    codice = '''from typing import Optional


def somma(a: int, b: int, c: int = 0) -> int:
    return a + b + c


def meta(valore: int) -> Optional[int]:
    return valore // 2
'''
    assert giudizio(codice), "una firma allargata viola il contratto"


def test_il_giudizio_boccia_la_sintassi_rotta():
    giudizio = mcp_server.giudizio_infill(SCHELETRO)
    assert giudizio("def somma(a, b) -> int\n    return a + b")


def test_il_giudizio_promuove_l_implementazione_conforme():
    giudizio = mcp_server.giudizio_infill(SCHELETRO)
    codice = '''from typing import Optional


def somma(a: int, b: int) -> int:
    return a + b


def meta(valore: int) -> Optional[int]:
    if valore < 0:
        return None
    return valore // 2
'''
    assert giudizio(codice) == []


def test_il_giudizio_regge_un_ingresso_che_non_e_python():
    """Il tool riceve scheletro e contratto in una stringa sola: se le firme
    non sono estraibili il giudizio resta valido sugli altri controlli."""
    giudizio = mcp_server.giudizio_infill("{\"contratto\": \"non e' codice\"}")
    assert giudizio("def f():\n    return 1") == []
    assert giudizio("def f():\n    raise NotImplementedError")


# ------------------------------------------------ il giudizio sul pre-flight


def test_il_preflight_rifiuta_una_risposta_vuota():
    assert mcp_server.giudizio_preflight("   ")


def test_il_preflight_rifiuta_chi_non_decide():
    """Una risposta che non approva e non elenca difetti non e' un verdetto."""
    assert mcp_server.giudizio_preflight("Non sono in grado di stabilirlo.")


def test_il_preflight_accetta_l_approvazione():
    assert mcp_server.giudizio_preflight("APPROVED") == []


def test_il_preflight_accetta_un_elenco_di_difetti():
    verdetto = "- firma alterata: compare un parametro non previsto\n- manca il controllo sul buffer"
    assert mcp_server.giudizio_preflight(verdetto) == []


# --------------------------------------------------- i due protocolli


def test_i_groq_si_provano_uno_per_uno_e_gli_altri_in_blocco(monkeypatch):
    groq_visti = []
    openrouter_visti = []

    def finto_groq(modello, messaggi, max_tokens, temperatura):
        groq_visti.append(modello)
        raise RuntimeError("non risponde")

    def finto_openrouter(gruppo, messaggi, max_tokens, temperatura):
        openrouter_visti.append(list(gruppo))
        return ("testo", gruppo[0])

    monkeypatch.setattr(mcp_server, "chiama_groq", finto_groq)
    monkeypatch.setattr(mcp_server, "chiama_openrouter", finto_openrouter)

    gruppo = ["groq:uno", "groq:due", "altro/tre:free"]
    testo, modello = mcp_server.chiama_gruppo(gruppo, [], 100, 0.0)
    assert groq_visti == ["groq:uno", "groq:due"], "i Groq vanno provati singolarmente, in ordine"
    assert openrouter_visti == [["altro/tre:free"]], "i restanti vanno a OpenRouter in un colpo solo"
    assert modello == "altro/tre:free"


def test_il_primo_groq_che_risponde_ferma_il_gruppo(monkeypatch):
    chiamati = []

    def finto_groq(modello, messaggi, max_tokens, temperatura):
        chiamati.append(modello)
        return ("va bene", modello)

    def finto_openrouter(gruppo, messaggi, max_tokens, temperatura):
        raise AssertionError("OpenRouter non doveva essere interpellato")

    monkeypatch.setattr(mcp_server, "chiama_groq", finto_groq)
    monkeypatch.setattr(mcp_server, "chiama_openrouter", finto_openrouter)

    testo, modello = mcp_server.chiama_gruppo(["groq:uno", "groq:due", "x:free"], [], 100, 0.0)
    assert chiamati == ["groq:uno"]
    assert testo == "va bene"


def test_un_gruppo_tutto_muto_solleva(monkeypatch):
    monkeypatch.setattr(mcp_server, "chiama_groq",
                        lambda *a, **k: (_ for _ in ()).throw(RuntimeError("muto")))
    monkeypatch.setattr(mcp_server, "chiama_openrouter",
                        lambda *a, **k: (_ for _ in ()).throw(RuntimeError("muto")))
    with pytest.raises(Exception):
        mcp_server.chiama_gruppo(["groq:uno", "x:free"], [], 100, 0.0)


# --------------------------------------------------------- la quota


def test_il_429_diventa_limite_raggiunto_con_i_secondi_indicati():
    class Risposta:
        status_code = 429
        headers = {"retry-after": "42"}
        text = "Too Many Requests"

        def json(self):
            return {}

    with pytest.raises(free_first.LimiteRaggiunto) as errore:
        mcp_server.controlla_quota(Risposta())
    assert errore.value.attendi == 42.0


def test_senza_retry_after_si_usa_la_pausa_predefinita():
    class Risposta:
        status_code = 429
        headers = {}
        text = "Too Many Requests"

        def json(self):
            return {}

    with pytest.raises(free_first.LimiteRaggiunto) as errore:
        mcp_server.controlla_quota(Risposta())
    assert errore.value.attendi == free_first.PAUSA_PREDEFINITA


def test_una_risposta_buona_non_solleva():
    class Risposta:
        status_code = 200
        headers = {}
        text = ""

        def json(self):
            return {}

    assert mcp_server.controlla_quota(Risposta()) is None


def test_il_registro_delle_pause_e_unico_per_processo():
    assert isinstance(mcp_server.PAUSA, free_first.Pausa)
    assert mcp_server.PAUSA is mcp_server.PAUSA


# ------------------------------------------------- le politiche per tool


def test_l_infilling_e_il_preflight_passano_dalla_cascata():
    for nome in ("cloud_infill_implementation", "preflight_contract_check"):
        assert mcp_server.POLITICHE[nome]["cascata"] is True


def test_il_ragionatore_profondo_resta_a_pagamento():
    """Una diagnosi sbagliata ma plausibile supererebbe ogni giudizio
    automatico: senza un banco che la sappia misurare non si risparmia qui."""
    assert mcp_server.POLITICHE["deep_reasoner_solve_crash"]["cascata"] is False


def test_ogni_politica_con_cascata_dichiara_budget_e_pagato():
    for nome, politica in mcp_server.POLITICHE.items():
        if politica["cascata"]:
            assert politica["budget_secondi"] > 0, nome
            assert politica["pagato"], nome


def test_il_preflight_ha_un_budget_piu_stretto_dell_infilling():
    """Il pagato del pre-flight risponde in poco piu' di un secondo: la cascata
    non puo' permettersi di farlo aspettare quanto un infilling."""
    assert (mcp_server.POLITICHE["preflight_contract_check"]["budget_secondi"]
            < mcp_server.POLITICHE["cloud_infill_implementation"]["budget_secondi"])


# ----------------------------------------- il lucchetto che non deve cadere


def test_deepseek_resta_fuori_da_openrouter():
    with pytest.raises(ValueError):
        mcp_server.call_openrouter("qualcuno/deepseek-cosa", "prompt", "sistema")


def test_nessun_modello_groq_finisce_su_openrouter(monkeypatch):
    visti = []
    monkeypatch.setattr(mcp_server, "chiama_openrouter",
                        lambda gruppo, *a, **k: visti.append(list(gruppo)) or ("t", gruppo[0]))
    monkeypatch.setattr(mcp_server, "chiama_groq",
                        lambda *a, **k: (_ for _ in ()).throw(RuntimeError("muto")))
    try:
        mcp_server.chiama_gruppo(["groq:uno", "groq:due"], [], 100, 0.0)
    except Exception:
        pass
    for gruppo in visti:
        assert not any(m.startswith("groq:") for m in gruppo)
