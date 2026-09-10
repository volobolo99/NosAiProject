"""Criterio di accettazione del contratto model-catalog-002.

Il caso centrale e' una regressione: il 2026-09-10 il catalogo fornitori conteneva
tre voci mentre i binding dei dipendenti citavano quattro nomi logici assenti, con
il risultato che 12 dipendenti su 15 sollevavano RuntimeError su choose(role_id=...)
e employee.perception riceveva qwen2.5-coder:7b, un modello di solo testo, per il
lavoro di leggere lo schermo di gioco.

L'ultimo test del file e' il lucchetto sul consenso: qualunque cambio di un modello
a pagamento come primario di un dipendente lo fa fallire, e va aggiornato solo dopo
un consenso esplicito dell'operatore.
"""
from __future__ import annotations

import json
from pathlib import Path

import pytest

from nosai.mcp.config import DEFAULT_CONFIG, load_config
from nosai.mcp.policy import McpMode, McpPolicy
from nosai.mcp.roles import DEFAULT_EMPLOYEE_ROLES
from nosai.mcp.router import ModelRouter

RADICE = Path(__file__).resolve().parents[1]
CONFIG_VIVA = RADICE / "config" / "mcp.default.json"

# I modelli a pagamento del progetto. Ogni aggiunta qui e' una decisione economica.
MODELLI_A_PAGAMENTO = {
    "qwen3-coder-30b",
    "qwen/qwen3-coder-30b-a3b-instruct",
    "deepseek-v4-flash",
    "gemini-2.5-flash-lite",
    "google/gemini-2.5-flash-lite",
}

# Fotografia dei primari al 2026-09-10, prima del catalogo pieno.
PRIMARI_ATTESI = {
    "employee.orchestrator_cto": "claude",
    "employee.mcp_chief": "claude",
    "employee.product_manager": "qwen2.5-coder:7b",
    "employee.game_ai_architect": "claude",
    "employee.perception": "gemini-2.5-flash-lite",
    "employee.world_model": "qwen3-coder-30b",
    "employee.planning": "qwen3-coder-30b",
    "employee.decision": "qwen3-coder-30b",
    "employee.action": "deepseek-v4-flash",
    "employee.memory": "deepseek-v4-flash",
    "employee.coding": "deepseek-v4-flash",
    "employee.testing": "deepseek-v4-flash",
    "employee.security": "claude",
    "employee.reviewer": "claude",
    "employee.documentation": "qwen2.5-coder:7b",
}


def test_provider_config_accetta_i_campi_della_misura():
    """router.from_config costruisce ProviderConfig(**item): un campo non
    dichiarato nella dataclass fa sollevare TypeError alla lettura della
    configurazione, quindi i campi nuovi vengono prima del catalogo.
    """
    from nosai.mcp.contracts import ProviderConfig
    voce = ProviderConfig(
        provider_id="groq-free", model_id="qwen/qwen3.8-27b", tier=1,
        enabled=True, network_required=True, capabilities=("coding",),
        aliases=("qwen3.8-27b",), cost_class="free", sec_medi=0.7,
        bench="a+b", misurato_il="2026-09-10", routable=True,
        quota="tier gratuito Groq",
    )
    assert voce.aliases == ("qwen3.8-27b",)
    assert voce.cost_class == "free"
    assert voce.routable is True


def test_provider_config_conserva_la_costruzione_minima():
    """I campi nuovi hanno un valore predefinito: le costruzioni esistenti
    nei test dell'hub non devono rompersi."""
    from nosai.mcp.contracts import ProviderConfig
    voce = ProviderConfig(provider_id="x", model_id="y", tier=0)
    assert voce.aliases == ()
    assert voce.routable is True
    assert voce.sec_medi is None


@pytest.fixture
def router() -> ModelRouter:
    cfg = load_config(CONFIG_VIVA)
    return ModelRouter.from_config(cfg, McpPolicy(mode=McpMode.OFFLINE, network_enabled=True))


def _voci(router: ModelRouter) -> list:
    return list(router.providers)


def _per_modello(router: ModelRouter, model_id: str):
    return next((v for v in router.providers if v.model_id == model_id), None)


# --- risoluzione dei binding: la regressione ------------------------------------

def test_ogni_dipendente_risolve_senza_eccezione(router):
    """Il 2026-09-10 dodici dipendenti su quindici sollevavano RuntimeError."""
    falliti = []
    for e in DEFAULT_EMPLOYEE_ROLES:
        try:
            router.choose(role_id=e.employee_id)
        except Exception as exc:
            falliti.append((e.employee_id, type(exc).__name__, str(exc)))
    assert falliti == [], f"dipendenti senza modello: {falliti}"


def test_primario_instradabile_vince_sul_ripiego(router):
    for e in DEFAULT_EMPLOYEE_ROLES:
        voce = next((v for v in router.providers
                     if e.primary_model == v.model_id or e.primary_model in tuple(v.aliases)), None)
        if voce is None or not voce.routable:
            continue
        deciso = router.choose(role_id=e.employee_id)
        assert deciso.model_id == voce.model_id, (
            f"{e.employee_id} ha primario {e.primary_model} instradabile "
            f"ma ha ricevuto {deciso.model_id}")


def test_perception_riceve_un_modello_che_vede(router):
    """Un modello di solo testo non puo' leggere lo schermo di gioco."""
    deciso = router.choose(role_id="employee.perception", capability="vision")
    voce = _per_modello(router, deciso.model_id)
    assert voce is not None
    assert "vision" in voce.capabilities, (
        f"employee.perception ha ricevuto {deciso.model_id}, che non dichiara vision")


def test_nessun_dipendente_risolve_su_una_voce_non_instradabile(router):
    for e in DEFAULT_EMPLOYEE_ROLES:
        deciso = router.choose(role_id=e.employee_id)
        voce = _per_modello(router, deciso.model_id)
        assert voce is not None and voce.routable, (
            f"{e.employee_id} risolve su {deciso.model_id}, marcato non instradabile")


def test_primario_non_instradabile_passa_al_ripiego(router):
    """claude agisce nella sessione: il gateway non lo chiama, passa al fallback."""
    deciso = router.choose(role_id="employee.orchestrator_cto")
    assert deciso.model_id != "claude"
    voce = _per_modello(router, deciso.model_id)
    assert voce is not None and voce.routable


def test_un_alias_risolve_sulla_sua_voce(router):
    con_alias = [v for v in router.providers if tuple(v.aliases)]
    assert con_alias, "il catalogo pieno deve dichiarare almeno un alias"
    for voce in con_alias:
        for alias in voce.aliases:
            trovata = next((v for v in router.providers
                            if v.model_id == alias or alias in tuple(v.aliases)), None)
            assert trovata is not None, f"alias orfano: {alias}"


# --- integrita' del catalogo ----------------------------------------------------

def test_ogni_voce_porta_l_evidenza_della_misura(router):
    for voce in _voci(router):
        if voce.cost_class == "session":
            continue
        assert voce.bench, f"{voce.model_id} entra in catalogo senza campo bench"
        assert voce.misurato_il, f"{voce.model_id} entra in catalogo senza misurato_il"


def test_ogni_voce_dichiara_le_proprie_capacita(router):
    """capabilities vuoto aggira il filtro di capacita' del router."""
    for voce in _voci(router):
        assert voce.capabilities, f"{voce.model_id} ha capabilities vuoto"


def test_i_pagati_stanno_sopra_i_gratuiti(router):
    tier_free = [v.tier for v in _voci(router) if v.cost_class in ("free", "local")]
    tier_paid = [v.tier for v in _voci(router) if v.cost_class == "paid"]
    assert tier_free and tier_paid, "il catalogo deve contenere sia gratuiti sia pagati"
    assert min(tier_paid) > max(tier_free), (
        "la cascata deve esaurire i gratuiti prima di spendere")


def test_una_sola_fonte_per_il_catalogo():
    """Due copie del catalogo divergono: il comportamento cambierebbe a seconda
    che il file di configurazione esista. Il catalogo effettivo, cioe' quello che
    load_config restituisce davvero, deve essere quello di DEFAULT_CONFIG.

    Un operatore puo' ancora ridefinire providers nel file, ma allora deve
    scriverlo identico, e questo test lo obbliga ad accorgersene.
    """
    effettivo = load_config(CONFIG_VIVA)["providers"]
    assert effettivo == DEFAULT_CONFIG["providers"], (
        "il catalogo effettivo non e' quello di DEFAULT_CONFIG: "
        "config/mcp.default.json ne ridefinisce una versione divergente")


def test_il_catalogo_non_e_piu_un_set_minimo():
    """La richiesta dell'operatore: un catalogo pieno, non cinque modelli."""
    assert len(DEFAULT_CONFIG["providers"]) >= 15, (
        f"il catalogo ha {len(DEFAULT_CONFIG['providers'])} voci: non e' un catalogo pieno")
    gratuiti = [v for v in DEFAULT_CONFIG["providers"] if v.get("cost_class") == "free"]
    assert len(gratuiti) >= 10, "il catalogo deve portare i gratuiti misurati, non solo i pagati"


def test_il_catalogo_contiene_ogni_modello_citato_dai_binding(router):
    noti = set()
    for v in _voci(router):
        noti.add(v.model_id)
        noti.update(v.aliases)
    citati = set()
    for e in DEFAULT_EMPLOYEE_ROLES:
        citati.add(e.primary_model)
        citati.update(e.fallback_models)
    assert citati <= noti, f"modelli citati dai binding e assenti dal catalogo: {sorted(citati - noti)}"


# --- il dipendente guardiano ----------------------------------------------------

def test_esiste_il_dipendente_guardiano():
    scout = next((e for e in DEFAULT_EMPLOYEE_ROLES if e.employee_id == "employee.model_scout"), None)
    assert scout is not None, "nessuno sorveglia il catalogo"
    assert "catalog_watch" in scout.capabilities


def test_il_guardiano_non_puo_promuovere():
    """Propone e non dispone: il consenso resta un gesto dell'operatore."""
    scout = next(e for e in DEFAULT_EMPLOYEE_ROLES if e.employee_id == "employee.model_scout")
    for vietato in ("binding_promotion", "paid_model_adoption"):
        assert vietato in scout.forbidden, f"il guardiano puo' ancora {vietato}"


def test_il_guardiano_non_costa(router):
    scout = next(e for e in DEFAULT_EMPLOYEE_ROLES if e.employee_id == "employee.model_scout")
    assert scout.primary_model not in MODELLI_A_PAGAMENTO, (
        "la sorveglianza quotidiana del catalogo non deve costare")


# --- lucchetto sul consenso -----------------------------------------------------

def test_nessun_primario_a_pagamento_cambia_senza_consenso():
    """Questo test fallisce a ogni sostituzione di un modello a pagamento.

    Va aggiornato SOLO dopo un consenso esplicito dell'operatore, mai per farlo
    passare. Se fallisce senza che tu abbia autorizzato un cambio, il cambio e'
    arrivato da un automatismo e va annullato.
    """
    attuali = {e.employee_id: e.primary_model for e in DEFAULT_EMPLOYEE_ROLES}
    for employee_id, atteso in PRIMARI_ATTESI.items():
        assert attuali.get(employee_id) == atteso, (
            f"{employee_id} aveva primario {atteso} e ora ha {attuali.get(employee_id)}")
    a_pagamento_prima = {k for k, v in PRIMARI_ATTESI.items() if v in MODELLI_A_PAGAMENTO}
    a_pagamento_ora = {k for k, v in attuali.items() if v in MODELLI_A_PAGAMENTO}
    assert a_pagamento_ora == a_pagamento_prima, (
        f"l'insieme dei dipendenti su modello a pagamento e' cambiato: "
        f"aggiunti {sorted(a_pagamento_ora - a_pagamento_prima)}, "
        f"rimossi {sorted(a_pagamento_prima - a_pagamento_ora)}")
