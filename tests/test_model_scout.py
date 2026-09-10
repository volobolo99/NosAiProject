"""Criterio di accettazione di scripts/model_scout.py, contratto model-scout-002.

Lo scout propone, non dispone: nessun test qui gli concede di modificare il roster
o di sostituire un modello a pagamento.

I casi da test_scartato_non_e_adottabile in poi sono regressioni sui difetti misurati
il 2026-09-10, quando il rapporto dichiarava adottabili senza consenso sei modelli che
il banco aveva gia' bocciato, incluso uno che aveva totalizzato 0 su 16.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path
from unittest import mock

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "scripts"))

import model_scout  # noqa: E402


def _modello(mid, prompt, completion, ctx=100000, modalities=None):
    m = {"id": mid, "name": mid, "context_length": ctx,
         "pricing": {"prompt": str(prompt), "completion": str(completion)}}
    if modalities is not None:
        m["architecture"] = {"output_modalities": modalities}
    return m


def _scrivi_roster(cartella: Path, validati, scartati) -> Path:
    p = cartella / "free_roster.json"
    p.write_text(json.dumps({
        "schema_version": "1.0",
        "scartati": scartati,
        "validati": validati,
    }), encoding="utf-8")
    return p


# --- invarianti conservati da model-scout-001 -------------------------------

def test_fetch_catalog_restituisce_data():
    risposta = mock.Mock(status_code=200)
    risposta.json.return_value = {"data": [_modello("a/b", 0, 0)]}
    with mock.patch.object(model_scout.requests, "get", return_value=risposta):
        assert model_scout.fetch_catalog() == [_modello("a/b", 0, 0)]


def test_fetch_catalog_errore_http():
    risposta = mock.Mock(status_code=503)
    with mock.patch.object(model_scout.requests, "get", return_value=risposta):
        with pytest.raises(RuntimeError) as exc:
            model_scout.fetch_catalog()
    assert "503" in str(exc.value)


def test_is_free():
    assert model_scout.is_free(_modello("x/y", 0, 0)) is True
    assert model_scout.is_free(_modello("x/y", "0.0000007", "0")) is False
    assert model_scout.is_free(_modello("x/y", "0", "0.0000007")) is False


def test_cost_per_mtok():
    ingresso, uscita = model_scout.cost_per_mtok(_modello("x/y", "0.0000007", "0.0000028"))
    assert round(ingresso, 4) == 0.7
    assert round(uscita, 4) == 2.8


# --- prezzi sentinella ------------------------------------------------------

def test_prezzo_negativo_e_sentinella():
    """openrouter/auto-beta espone prompt -1000000/M: non e' il modello piu' conveniente."""
    sentinella = _modello("openrouter/auto-beta", "-1000000", "-1000000")
    assert model_scout.is_price_sentinel(sentinella) is True
    assert model_scout.is_price_sentinel(_modello("a/b", "0.00007", "0.00028")) is False
    assert model_scout.is_price_sentinel(_modello("a/b", 0, 0)) is False


def test_cost_per_mtok_rifiuta_la_sentinella():
    with pytest.raises(ValueError):
        model_scout.cost_per_mtok(_modello("openrouter/fusion", "-1000000", "-1000000"))


def test_sentinella_fuori_dai_candidati_a_pagamento():
    catalogo = [
        _modello("openrouter/auto-beta", "-1000000", "-1000000"),
        _modello("buono/economico", "0.00000001", "0.00000002"),
    ]
    cand = model_scout.candidati_a_pagamento(catalogo, "in/uso", (0.07, 0.28))
    ids = [c["id"] for c in cand]
    assert "openrouter/auto-beta" not in ids
    assert "buono/economico" in ids


def test_ogni_candidato_a_pagamento_richiede_consenso():
    catalogo = [_modello("buono/economico", "0.00000001", "0.00000002")]
    cand = model_scout.candidati_a_pagamento(catalogo, "in/uso", (0.07, 0.28))
    assert cand, "il candidato piu' economico deve comparire"
    assert all(c["richiede_consenso"] is True for c in cand)


def test_il_modello_in_uso_non_e_candidato_di_se_stesso():
    catalogo = [_modello("in/uso", "0.00000001", "0.00000002")]
    assert model_scout.candidati_a_pagamento(catalogo, "in/uso", (0.07, 0.28)) == []


# --- filtro di scopo --------------------------------------------------------

@pytest.mark.parametrize("mid", [
    "google/lyria-3-pro-preview",
    "google/lyria-3-clip-preview",
    "nvidia/nemotron-3.5-content-safety:free",
    "meta/llama-prompt-guard-2-86m",
    "openai/gpt-oss-safeguard-20b",
    "mistralai/codestral-embed",
    "mistralai/voxtral-mini-tts-latest",
])
def test_scopo_escluso_non_e_text_capable(mid):
    """Musica, classificatori, embedding e audio non scrivono codice."""
    assert model_scout.is_text_capable(_modello(mid, 0, 0)) is False


def test_modello_di_testo_e_capable():
    assert model_scout.is_text_capable(_modello("nex-agi/nex-n2.5-mini:free", 0, 0)) is True


def test_modalita_di_uscita_senza_testo_esclude():
    audio = _modello("qualcuno/modello-audio", 0, 0, modalities=["audio"])
    assert model_scout.is_text_capable(audio) is False


def test_campo_modalita_assente_non_esclude():
    assert model_scout.is_text_capable(_modello("qualcuno/normale", 0, 0)) is True


# --- lettura del roster -----------------------------------------------------

def test_load_roster_legge_validati_e_scartati(tmp_path):
    p = _scrivi_roster(
        tmp_path,
        [{"id": "buono/modello:free", "fornitore": "openrouter", "sec_medi": 8}],
        {"cattivo/modello:free": "0/16 e 3/14: non regge il contratto"},
    )
    validati, scartati = model_scout.load_roster(p)
    assert "buono/modello:free" in validati
    assert validati["buono/modello:free"]["sec_medi"] == 8
    assert scartati["cattivo/modello:free"].startswith("0/16")


def test_load_roster_senza_file_solleva(tmp_path):
    """Senza roster il guardiano non puo' giudicare: non deve indovinare."""
    with pytest.raises(FileNotFoundError):
        model_scout.load_roster(tmp_path / "non_esiste.json")


def test_roster_id_normalizza_il_prefisso_del_fornitore():
    assert model_scout.roster_id("openai/gpt-oss-120b", "groq") == "groq:openai/gpt-oss-120b"
    assert model_scout.roster_id("openai/gpt-oss-20b", "nvidia") == "nvidia:openai/gpt-oss-20b"
    assert model_scout.roster_id("codestral-latest", "mistral") == "mistral:codestral-latest"
    assert model_scout.roster_id("nex-agi/nex-n2.5-mini:free", "openrouter") == "nex-agi/nex-n2.5-mini:free"


# --- classificazione: il cuore del difetto del 2026-09-10 -------------------

def test_scartato_non_e_adottabile():
    """liquid/lfm-2.5-2.6b:free ha fatto 0/16 al banco A e il catalogo lo offre ancora."""
    scartati = {"liquid/lfm-2.5-2.6b:free": "0/16 e 3/14: non regge il contratto"}
    esito = model_scout.classifica_modello(
        _modello("liquid/lfm-2.5-2.6b:free", 0, 0), {}, scartati, {})
    assert esito["stato"] == "scartato"
    assert esito["adottabile"] is False
    assert "0/16" in esito["motivo"]


def test_validato_non_e_nuovo():
    validati = {"nex-agi/nex-n2.5-mini:free": {"id": "nex-agi/nex-n2.5-mini:free", "sec_medi": 8}}
    esito = model_scout.classifica_modello(
        _modello("nex-agi/nex-n2.5-mini:free", 0, 0), validati, {}, {})
    assert esito["stato"] == "validato"
    assert esito["adottabile"] is True


def test_gratuito_ignoto_e_da_provare_non_adottabile():
    esito = model_scout.classifica_modello(
        _modello("inclusionai/ling-3.0-flash-vl:free", 0, 0), {}, {}, {})
    assert esito["stato"] == "nuovo_gratuito"
    assert esito["adottabile"] is False, "un gratuito mai passato dal banco non e' adottabile"


def test_lo_scarto_vince_sulla_validazione():
    """Ordine di valutazione vincolante: uno scarto registrato batte tutto."""
    mid = "ambiguo/modello:free"
    esito = model_scout.classifica_modello(
        _modello(mid, 0, 0), {mid: {"id": mid}}, {mid: "bocciato al banco B"}, {})
    assert esito["stato"] == "scartato"
    assert esito["adottabile"] is False


def test_sentinella_classificata_come_tale():
    esito = model_scout.classifica_modello(
        _modello("openrouter/auto", "-1000000", "-1000000"), {}, {}, {})
    assert esito["stato"] == "sentinella"
    assert esito["adottabile"] is False


def test_non_pertinente_classificato_come_tale():
    esito = model_scout.classifica_modello(
        _modello("google/lyria-3-pro-preview", 0, 0), {}, {}, {})
    assert esito["stato"] == "non_pertinente"
    assert esito["adottabile"] is False


# --- storico dei prezzi -----------------------------------------------------

def test_snapshot_scritto_e_riletto(tmp_path):
    catalogo = [_modello("a/b", 0, 0), _modello("c/d", "0.00001", "0.00002")]
    p = model_scout.save_snapshot(catalogo, "2026-09-09", tmp_path)
    assert p.exists()
    dati = json.loads(p.read_text(encoding="utf-8"))
    assert any(v["id"] == "a/b" and v["free"] for v in dati.values() if isinstance(v, dict)) \
        or "a/b" in json.dumps(dati)


def test_snapshot_di_oggi_non_altera_quello_di_ieri(tmp_path):
    model_scout.save_snapshot([_modello("a/b", "0.00001", "0.00002")], "2026-09-09", tmp_path)
    ieri = (tmp_path / "2026-09-09.json").read_text(encoding="utf-8")
    model_scout.save_snapshot([_modello("a/b", 0, 0)], "2026-09-10", tmp_path)
    assert (tmp_path / "2026-09-09.json").read_text(encoding="utf-8") == ieri
    assert (tmp_path / "2026-09-10.json").exists()


def test_load_last_snapshot_prende_il_precedente(tmp_path):
    model_scout.save_snapshot([_modello("a/b", "0.00001", "0.00002")], "2026-09-08", tmp_path)
    model_scout.save_snapshot([_modello("a/b", "0.00002", "0.00003")], "2026-09-09", tmp_path)
    prec = model_scout.load_last_snapshot("2026-09-10", tmp_path)
    assert prec["a/b"]["prompt"] in ("0.00002", 0.00002), "deve leggere il 09, non il 08"


def test_load_last_snapshot_vuoto_al_primo_giro(tmp_path):
    assert model_scout.load_last_snapshot("2026-09-10", tmp_path) == {}


def test_diventato_gratuito_riconosciuto():
    """Il caso che l'operatore ha chiesto: un modello vecchio messo gratuito."""
    precedente = {"c/d": {"id": "c/d", "prompt": "0.00001", "completion": "0.00002", "free": False}}
    assert model_scout.diventati_gratuiti([_modello("c/d", 0, 0)], precedente) == ["c/d"]


def test_nuovo_non_e_diventato_gratuito():
    assert model_scout.diventati_gratuiti([_modello("mai/visto:free", 0, 0)], {}) == []


def test_gratuito_ieri_e_gratuito_oggi_non_e_una_novita():
    precedente = {"a/b": {"id": "a/b", "prompt": "0", "completion": "0", "free": True}}
    assert model_scout.diventati_gratuiti([_modello("a/b", 0, 0)], precedente) == []


# --- uscite -----------------------------------------------------------------

def test_render_report_ha_tutte_le_sezioni():
    esiti = {"ruoli": {}, "roster_mancanti": [], "adottabili": [], "da_provare": [],
             "diventati_gratuiti": [], "candidati_a_pagamento": [], "bocciati": []}
    testo = model_scout.render_report(esiti).lower()
    for sezione in ("adottabili", "da provare", "diventati gratuiti",
                    "consenso", "bocciati", "spariti"):
        assert sezione in testo, f"sezione mancante: {sezione}"


def test_report_non_dichiara_adottabile_uno_scartato():
    """Regressione diretta sul difetto del 2026-09-10."""
    esiti = {"ruoli": {}, "roster_mancanti": [], "da_provare": [], "diventati_gratuiti": [],
             "candidati_a_pagamento": [], "adottabili": [],
             "bocciati": [{"id": "liquid/lfm-2.5-2.6b:free", "motivo": "0/16 e 3/14"}]}
    testo = model_scout.render_report(esiti)
    posizione_bocciati = testo.lower().find("bocciati")
    posizione_modello = testo.find("liquid/lfm-2.5-2.6b:free")
    assert posizione_modello > posizione_bocciati >= 0, \
        "un modello bocciato deve comparire solo nella sezione dei bocciati"


def test_scrivi_proposte_marca_il_consenso(tmp_path):
    esiti = {"adottabili": [], "da_provare": [], "diventati_gratuiti": [], "bocciati": [],
             "candidati_a_pagamento": [{"id": "x/y", "risparmio": 10.0, "richiede_consenso": True}]}
    p = model_scout.scrivi_proposte(esiti, tmp_path / "proposte.json")
    dati = json.loads(p.read_text(encoding="utf-8"))
    assert dati["schema_version"] == "nosai.model.scout.proposals.v1"
    assert all(c["richiede_consenso"] is True for c in dati["candidati_a_pagamento"])


# --- il percorso conservato deve reggere le regole nuove ---------------------

def test_compare_to_roster_non_si_schianta_sui_sentinella():
    """La funzione conservata da model-scout-001 chiamava cost_per_mtok senza
    difese. Da quando cost_per_mtok solleva ValueError sui prezzi sentinella,
    l'esecuzione reale del 2026-09-11 e' morta con
    'ValueError: Il modello e' un prezzo sentinella' sul catalogo vero, che
    contiene cinque voci openrouter con prompt -1000000/M.
    """
    catalogo = [
        _modello("openrouter/auto-beta", "-1000000", "-1000000"),
        _modello("openrouter/fusion", "-1000000", "-1000000"),
        _modello("buono/economico", "0.00000001", "0.00000002"),
        _modello("gratis/modello:free", 0, 0),
    ]
    esito = model_scout.compare_to_roster(catalogo, {"worker": "in/uso"})
    testo = json.dumps(esito)
    assert "openrouter/auto-beta" not in testo, "una sentinella non e' un candidato"
    assert "openrouter/fusion" not in testo


def test_main_regge_il_catalogo_reale(tmp_path, monkeypatch):
    """Il guardiano gira ogni notte senza sorveglianza: non deve sollevare."""
    catalogo = [
        _modello("openrouter/auto", "-1000000", "-1000000"),
        _modello("google/lyria-3-pro-preview", 0, 0),
        _modello("liquid/lfm-2.5-2.6b:free", 0, 0),
        _modello("nex-agi/nex-n2.5-mini:free", 0, 0),
        _modello("qwen/qwen3-coder-30b-a3b-instruct", "0.00000007", "0.00000028"),
    ]
    monkeypatch.setattr(model_scout, "fetch_catalog", lambda timeout=60: catalogo)
    monkeypatch.setattr(model_scout, "HISTORY_DIR", tmp_path / "storia")
    monkeypatch.setattr(model_scout, "PROPOSALS_PATH", tmp_path / "proposte.json")
    monkeypatch.setattr(model_scout, "REPORT_PATH", tmp_path / "rapporto.md")
    assert model_scout.main() == 0


# --- partizione delle proposte: il difetto ricomparso il 2026-09-11 ----------

def _ids(voci):
    return {v if isinstance(v, str) else v.get("id") for v in voci}


@pytest.fixture
def proposte_reali(tmp_path, monkeypatch):
    """Esegue main su un catalogo che contiene un caso per ogni stato."""
    catalogo = [
        _modello("nex-agi/nex-n2.5-mini:free", 0, 0),          # validato nel roster
        _modello("inclusionai/ling-3.0-flash-vl:free", 0, 0),  # gratuito mai provato
        _modello("liquid/lfm-2.5-2.6b:free", 0, 0),            # bocciato: 0/16
        _modello("google/lyria-3-pro-preview", 0, 0),          # non pertinente
        _modello("openrouter/auto", "-1000000", "-1000000"),   # sentinella
        _modello("qualcuno/pagato", "0.00000001", "0.00000002"),
    ]
    monkeypatch.setattr(model_scout, "fetch_catalog", lambda timeout=60: catalogo)
    monkeypatch.setattr(model_scout, "HISTORY_DIR", tmp_path / "storia")
    monkeypatch.setattr(model_scout, "PROPOSALS_PATH", tmp_path / "proposte.json")
    monkeypatch.setattr(model_scout, "REPORT_PATH", tmp_path / "rapporto.md")
    assert model_scout.main() == 0
    return json.loads((tmp_path / "proposte.json").read_text(encoding="utf-8"))


def test_adottabili_e_da_provare_sono_disgiunti(proposte_reali):
    """Il 2026-09-11 le due liste contenevano gli stessi 38 elementi."""
    a = _ids(proposte_reali["adottabili"])
    p = _ids(proposte_reali["da_provare"])
    assert not (a & p), f"un modello non puo' essere insieme adottabile e da provare: {a & p}"


def test_adottabile_solo_se_validato_dal_banco(proposte_reali):
    """Il vincolo che l'operatore ha chiesto: niente adozioni non misurate."""
    validati, _ = model_scout.load_roster()
    for mid in _ids(proposte_reali["adottabili"]):
        assert mid in validati, (
            f"{mid} e' dichiarato adottabile senza essere passato dal banco")


def test_un_bocciato_non_e_mai_adottabile(proposte_reali):
    _, scartati = model_scout.load_roster()
    a = _ids(proposte_reali["adottabili"])
    assert not (a & set(scartati)), f"bocciati dichiarati adottabili: {a & set(scartati)}"


def test_il_gratuito_ignoto_finisce_fra_i_da_provare(proposte_reali):
    assert "inclusionai/ling-3.0-flash-vl:free" in _ids(proposte_reali["da_provare"])
    assert "inclusionai/ling-3.0-flash-vl:free" not in _ids(proposte_reali["adottabili"])


def test_il_validato_finisce_fra_gli_adottabili(proposte_reali):
    assert "nex-agi/nex-n2.5-mini:free" in _ids(proposte_reali["adottabili"])


def test_sentinella_e_non_pertinenti_fuori_da_ogni_proposta(proposte_reali):
    for chiave in ("adottabili", "da_provare", "candidati_a_pagamento"):
        ids = _ids(proposte_reali[chiave])
        assert "openrouter/auto" not in ids, f"sentinella in {chiave}"
        assert "google/lyria-3-pro-preview" not in ids, f"non pertinente in {chiave}"
