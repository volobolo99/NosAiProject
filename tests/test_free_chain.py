"""Criterio di accettazione della catena di riserva gratuita.

La catena non decide quale modello sia il migliore: usa l'ordine gia' misurato
dal banco e si limita a non fermarsi quando uno dei modelli tace.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))

import free_chain  # noqa: E402


def test_percorso_ancorato_al_file():
    assert isinstance(free_chain.FREE_ROSTER_PATH, Path)
    assert free_chain.FREE_ROSTER_PATH.is_absolute()
    assert free_chain.FREE_ROSTER_PATH.name == "free_roster.json"


def test_free_models_ordina_per_latenza(tmp_path, monkeypatch):
    roster = {"validati": [
        {"id": "lento/uno", "sec_medi": 60},
        {"id": "svelto/due", "sec_medi": 8},
        {"id": "medio/tre", "sec_medi": 22},
    ]}
    f = tmp_path / "free_roster.json"
    f.write_text(json.dumps(roster), encoding="utf-8")
    monkeypatch.setattr(free_chain, "FREE_ROSTER_PATH", f)
    assert free_chain.free_models() == ["svelto/due", "medio/tre", "lento/uno"]


def test_free_models_scarta_i_lenti(tmp_path, monkeypatch):
    roster = {"validati": [
        {"id": "svelto/uno", "sec_medi": 8},
        {"id": "lumaca/due", "sec_medi": 422},
        {"id": "confine/tre", "sec_medi": 200},
    ]}
    f = tmp_path / "free_roster.json"
    f.write_text(json.dumps(roster), encoding="utf-8")
    monkeypatch.setattr(free_chain, "FREE_ROSTER_PATH", f)
    assert free_chain.free_models() == ["svelto/uno"]
    assert free_chain.free_models(escludi_lenti=False) == ["svelto/uno", "confine/tre", "lumaca/due"]


def test_free_models_file_mancante(tmp_path, monkeypatch):
    monkeypatch.setattr(free_chain, "FREE_ROSTER_PATH", tmp_path / "non_esiste.json")
    assert free_chain.free_models() == []


def test_roster_reale_del_progetto():
    modelli = free_chain.free_models()
    assert modelli, "il roster del progetto non deve essere vuoto"
    assert all(m.endswith(":free") or m == "openrouter/free" for m in modelli)


def test_primo_modello_riuscito_ferma_la_catena():
    provati = []

    def chiamata(modello):
        provati.append(modello)
        return "risposta buona"

    testo, usato = free_chain.call_with_fallback(chiamata, ["a/uno", "b/due"])
    assert (testo, usato) == ("risposta buona", "a/uno")
    assert provati == ["a/uno"]


def test_eccezione_passa_al_successivo():
    def chiamata(modello):
        if modello == "a/uno":
            raise RuntimeError("429 dal provider")
        return "seconda risposta"

    testo, usato = free_chain.call_with_fallback(chiamata, ["a/uno", "b/due"])
    assert testo == "seconda risposta"
    assert usato == "b/due"


def test_risposta_vuota_conta_come_fallimento():
    def chiamata(modello):
        return "   " if modello == "a/uno" else "contenuto vero"

    testo, usato = free_chain.call_with_fallback(chiamata, ["a/uno", "b/due"])
    assert testo == "contenuto vero"
    assert usato == "b/due"


def test_tutti_falliti_solleva_ed_elenca():
    def chiamata(modello):
        raise RuntimeError("guasto su " + modello)

    with pytest.raises(RuntimeError) as exc:
        free_chain.call_with_fallback(chiamata, ["a/uno", "b/due"])
    messaggio = str(exc.value)
    assert "a/uno" in messaggio and "b/due" in messaggio


def test_lista_vuota_solleva():
    with pytest.raises(RuntimeError):
        free_chain.call_with_fallback(lambda m: "x", [])


# --- strategia: fallback lato piattaforma e lucchetto sul costo -------------


def test_gruppi_rispetta_il_limite_di_piattaforma():
    modelli = ["a", "b", "c", "d", "e", "f", "g"]
    blocchi = free_chain.gruppi(modelli)
    assert blocchi == [["a", "b", "c"], ["d", "e", "f"], ["g"]]
    assert all(len(b) <= free_chain.GRUPPO_MAX for b in blocchi)


def test_gruppi_casi_limite():
    assert free_chain.gruppi([]) == []
    assert free_chain.gruppi(["solo"]) == [["solo"]]
    with pytest.raises(ValueError):
        free_chain.gruppi(["a"], dimensione=0)


def test_il_lucchetto_e_attivo_per_difetto():
    corpo = free_chain.corpo_richiesta([{"role": "user", "content": "ciao"}], ["x/uno", "y/due"])
    assert corpo["model"] == "x/uno"
    assert corpo["models"] == ["x/uno", "y/due"]
    assert corpo["provider"]["max_price"] == {"prompt": 0, "completion": 0}
    assert "Novita" in corpo["provider"]["ignore"]


def test_senza_lucchetto_niente_max_price():
    corpo = free_chain.corpo_richiesta(
        [{"role": "user", "content": "ciao"}], ["x/uno"], solo_gratuiti=False
    )
    assert "max_price" not in corpo["provider"]
    assert "Novita" in corpo["provider"]["ignore"]


def test_corpo_rifiuta_gruppi_impossibili():
    with pytest.raises(ValueError):
        free_chain.corpo_richiesta([{"role": "user", "content": "c"}], [])
    with pytest.raises(ValueError):
        free_chain.corpo_richiesta([{"role": "user", "content": "c"}], ["a", "b", "c", "d"])


def test_testo_utile_legge_modello_e_contenuto():
    payload = {"model": "chi/harisposto", "choices": [{"message": {"content": "ecco"}}]}
    assert free_chain.testo_utile(payload) == ("ecco", "chi/harisposto")


def test_testo_utile_rifiuta_il_duecento_vuoto():
    for contenuto in (None, "", "   "):
        payload = {"model": "muto/modello", "choices": [{"message": {"content": contenuto}}]}
        with pytest.raises(RuntimeError):
            free_chain.testo_utile(payload)


def test_testo_utile_rifiuta_risposta_malformata():
    with pytest.raises(RuntimeError):
        free_chain.testo_utile({"model": "x", "choices": []})


def test_il_corpo_non_espone_la_costante_globale():
    """Chi riceve il corpo non deve poter mutare PROVIDER_GUASTI modificando la
    lista che ci trova dentro."""
    originale = list(free_chain.PROVIDER_GUASTI)
    corpo = free_chain.corpo_richiesta([{"role": "user", "content": "c"}], ["x/uno"])
    corpo["provider"]["ignore"].append("Intruso")
    assert free_chain.PROVIDER_GUASTI == originale


def test_cascata_si_ferma_al_primo_gruppo_utile():
    chiamate = []

    def post(corpo):
        chiamate.append(corpo["models"])
        return {"model": corpo["models"][0], "choices": [{"message": {"content": "buona"}}]}

    testo, modello = free_chain.chiama_a_gruppi(
        post, [{"role": "user", "content": "c"}], modelli=["a", "b", "c", "d", "e"]
    )
    assert testo == "buona"
    assert modello == "a"
    assert chiamate == [["a", "b", "c"]]


def test_cascata_supera_il_gruppo_che_risponde_vuoto():
    def post(corpo):
        if "a" in corpo["models"]:
            return {"model": "a", "choices": [{"message": {"content": "  "}}]}
        return {"model": "d", "choices": [{"message": {"content": "seconda"}}]}

    testo, modello = free_chain.chiama_a_gruppi(
        post, [{"role": "user", "content": "c"}], modelli=["a", "b", "c", "d"]
    )
    assert (testo, modello) == ("seconda", "d")


def test_cascata_supera_il_gruppo_che_solleva():
    def post(corpo):
        if "a" in corpo["models"]:
            raise RuntimeError("429 dal provider")
        return {"model": "d", "choices": [{"message": {"content": "seconda"}}]}

    testo, modello = free_chain.chiama_a_gruppi(
        post, [{"role": "user", "content": "c"}], modelli=["a", "b", "c", "d"]
    )
    assert modello == "d"


def test_cascata_esaurita_solleva_ed_elenca():
    def post(corpo):
        raise RuntimeError("il provider e' fuori servizio")

    with pytest.raises(RuntimeError) as exc:
        free_chain.chiama_a_gruppi(
            post, [{"role": "user", "content": "c"}],
            modelli=["primo/gruppo", "secondo/gruppo", "terzo/gruppo", "quarto/gruppo"],
        )
    messaggio = str(exc.value)
    # Due gruppi provati, due motivi riportati: il messaggio deve dire cosa e'
    # andato storto, non limitarsi a dire che e' andato storto.
    assert messaggio.count("fuori servizio") == 2
    assert "Gruppo 1" in messaggio and "Gruppo 2" in messaggio


def test_cascata_senza_modelli_solleva():
    with pytest.raises(RuntimeError):
        free_chain.chiama_a_gruppi(lambda c: {}, [{"role": "user", "content": "c"}], modelli=[])


def test_free_models_filtra_per_fornitore(tmp_path, monkeypatch):
    """Il roster e' multi-fornitore: free_chain parla solo con OpenRouter."""
    f = tmp_path / "roster.json"
    f.write_text(json.dumps({"validati": [
        {"id": "groq:qualcosa", "fornitore": "groq", "sec_medi": 1},
        {"id": "alfa:free", "fornitore": "openrouter", "sec_medi": 5},
        {"id": "nvidia:altro", "fornitore": "nvidia", "sec_medi": 2},
        {"id": "beta:free", "fornitore": "openrouter", "sec_medi": 9},
    ]}), encoding="utf-8")
    monkeypatch.setattr(free_chain, "FREE_ROSTER_PATH", f)
    assert free_chain.free_models() == ["alfa:free", "beta:free"]
    assert free_chain.free_models(fornitore="groq") == ["groq:qualcosa"]
    assert free_chain.free_models(fornitore=None) == [
        "groq:qualcosa", "nvidia:altro", "alfa:free", "beta:free"
    ]


def test_voce_senza_fornitore_conta_come_openrouter(tmp_path, monkeypatch):
    """Le voci storiche non portano il campo: erano tutte di OpenRouter."""
    f = tmp_path / "roster.json"
    f.write_text(json.dumps({"validati": [{"id": "storico:free", "sec_medi": 3}]}), encoding="utf-8")
    monkeypatch.setattr(free_chain, "FREE_ROSTER_PATH", f)
    assert free_chain.free_models() == ["storico:free"]
