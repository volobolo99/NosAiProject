"""Ricognizione periodica del catalogo OpenRouter.

Segnala i modelli che potrebbero migliorare il roster. Non cambia il roster:
i candidati a pagamento restano proposte finche' l'operatore non le approva.
"""
from __future__ import annotations

from pathlib import Path
from typing import Any, Dict, List, Tuple
import json
import sys
from datetime import datetime, timezone

import requests

CATALOG_URL = "https://openrouter.ai/api/v1/models"

# Il roster in uso, allineato a scripts/mcp_server.py.
ROSTER = {
    "worker": "qwen/qwen3-coder-30b-a3b-instruct",
    "auditor": "deepseek-v4-flash",
    "preflight": "google/gemini-2.5-flash-lite",
    "local_scaffold": "qwen2.5-coder:7b",
}

REPORT_PATH = Path(__file__).resolve().parents[1] / "reports" / "model-scout.md"
FREE_ROSTER_PATH = Path(__file__).resolve().parent / "free_roster.json"
HISTORY_DIR = Path(__file__).resolve().parents[1] / "data" / "model_catalog_history"
PROPOSALS_PATH = Path(__file__).resolve().parents[1] / "data" / "model_scout_proposals.json"
SCOPI_ESCLUSI = (
    "lyria",
    "content-safety",
    "prompt-guard",
    "moderation",
    "guard",
    "embed",
    "-tts",
    "transcribe",
    "-ocr",
    "voxtral",
    "safeguard",
)


def fetch_catalog(timeout: int = 60) -> List[Dict[str, Any]]:
    """Scarica il catalogo. RuntimeError con il codice HTTP se non e' 200."""
    response = requests.get(CATALOG_URL, timeout=timeout)
    if response.status_code != 200:
        raise RuntimeError(f"Errore HTTP {response.status_code}")
    return response.json()["data"]


def is_free(model: Dict[str, Any]) -> bool:
    """True se prompt e completion costano entrambi zero."""
    pricing = model["pricing"]
    return (
        float(pricing["prompt"]) == 0.0
        and float(pricing["completion"]) == 0.0
    )


def is_price_sentinel(model: Dict[str, Any]) -> bool:
    """True se una qualunque componente di pricing e' negativa oppure non convertibile in float."""
    pricing = model["pricing"]
    try:
        prompt = float(pricing["prompt"])
        completion = float(pricing["completion"])
        return prompt < 0 or completion < 0
    except (ValueError, TypeError):
        return True


def cost_per_mtok(model: Dict[str, Any]) -> Tuple[float, float]:
    """(prezzo input, prezzo output) per milione di token."""
    if is_price_sentinel(model):
        raise ValueError("Il modello e' un prezzo sentinella")
    pricing = model["pricing"]
    return (
        float(pricing["prompt"]) * 1_000_000,
        float(pricing["completion"]) * 1_000_000,
    )


def is_text_capable(model: Dict[str, Any]) -> bool:
    """True solo se il modello produce testo e non ha uno scopo escluso."""
    # Controllo 1: architecture.output_modalities contiene text
    if "architecture" in model and "output_modalities" in model["architecture"]:
        if "text" not in model["architecture"]["output_modalities"]:
            return False
    
    # Controllo 2: l'id, in minuscolo, non contiene nessuna delle sottostringhe di SCOPI_ESCLUSI
    model_id = model["id"].lower()
    for scopo_escluso in SCOPI_ESCLUSI:
        if scopo_escluso in model_id:
            return False
    
    return True


def load_roster(path: Path = FREE_ROSTER_PATH) -> Tuple[Dict[str, Dict[str, Any]], Dict[str, str]]:
    """Legge scripts/free_roster.json e restituisce la coppia validati, scartati."""
    try:
        with open(path, "r", encoding="utf-8") as f:
            roster = json.load(f)
    except FileNotFoundError:
        raise FileNotFoundError(f"File {path} non trovato")
    
    validati = {}
    scartati = roster.get("scartati", {})
    
    for item in roster.get("validati", []):
        normalized_id = roster_id(item["id"], item["fornitore"])
        validati[normalized_id] = item
    
    return validati, scartati


def roster_id(model_id: str, provider_id: str) -> str:
    """Normalizza un id di catalogo nella convenzione del roster."""
    if provider_id == "groq":
        return f"groq:{model_id}"
    elif provider_id == "nvidia":
        return f"nvidia:{model_id}"
    elif provider_id == "mistral":
        return f"mistral:{model_id}"
    else:
        return model_id


def classifica_modello(
    model: Dict[str, Any], 
    validati: Dict[str, Dict[str, Any]], 
    scartati: Dict[str, str], 
    gratuiti_ieri: Dict[str, bool], 
    provider_id: str = "openrouter"
) -> Dict[str, Any]:
    """Restituisce un dizionario con id, stato, motivo e adottabile."""
    model_id = model["id"]
    normalized_id = roster_id(model_id, provider_id)
    
    # Ordine di valutazione:
    # 1. sentinella
    if is_price_sentinel(model):
        return {
            "id": model_id,
            "stato": "sentinella",
            "motivo": None,
            "adottabile": False
        }
    
    # 2. non_pertinente
    if not is_text_capable(model):
        return {
            "id": model_id,
            "stato": "non_pertinente",
            "motivo": None,
            "adottabile": False
        }
    
    # 3. scartato
    if normalized_id in scartati:
        return {
            "id": model_id,
            "stato": "scartato",
            "motivo": scartati[normalized_id],
            "adottabile": False
        }
    
    # 4. validato
    if normalized_id in validati:
        return {
            "id": model_id,
            "stato": "validato",
            "motivo": None,
            "adottabile": is_free(model)
        }
    
    # 5. altri stati
    if is_free(model):
        # Se era gratuito ieri, allora è diventato gratuito
        if normalized_id in gratuiti_ieri and gratuiti_ieri[normalized_id]:
            return {
                "id": model_id,
                "stato": "diventato_gratuito",
                "motivo": None,
                "adottabile": True
            }
        else:
            return {
                "id": model_id,
                "stato": "nuovo_gratuito",
                "motivo": None,
                "adottabile": False
            }
    else:
        return {
            "id": model_id,
            "stato": "a_pagamento",
            "motivo": None,
            "adottabile": False
        }


def compare_to_roster(
    catalog: List[Dict[str, Any]], roster: Dict[str, str]
) -> Dict[str, Any]:
    """Confronto fra i modelli in uso e i candidati del catalogo."""
    confronto: Dict[str, Any] = {"ruoli": {}, "roster_mancanti": []}

    for ruolo, modello_in_uso in roster.items():
        modello_attuale = next(
            (modello for modello in catalog if modello["id"] == modello_in_uso),
            None,
        )

        if modello_attuale is None:
            confronto["roster_mancanti"].append(modello_in_uso)
            confronto["ruoli"][ruolo] = {
                "in_uso": modello_in_uso,
                "prezzo_attuale": [0.0, 0.0],
                "gratuiti_nuovi": [],
                "pagamento_piu_economici": [],
            }
            continue

        prezzo_attuale = cost_per_mtok(modello_attuale)
        somma_attuale = prezzo_attuale[0] + prezzo_attuale[1]
        context_attuale = modello_attuale["context_length"]
        gratuiti_nuovi: List[str] = []
        pagamento_piu_economici: List[Dict[str, Any]] = []

        for modello in catalog:
            if modello["id"] == modello_in_uso:
                continue

            # Salta i sentinella
            if is_price_sentinel(modello):
                continue
                
            # Salta i non pertinenti
            if not is_text_capable(modello):
                continue

            if is_free(modello):
                gratuiti_nuovi.append(modello["id"])
                continue

            try:
                prezzo_candidato = cost_per_mtok(modello)
            except ValueError:
                # Ignora i modelli con prezzo sentinella
                continue
                
            somma_candidato = prezzo_candidato[0] + prezzo_candidato[1]
            if (
                modello["context_length"] >= context_attuale
                and somma_candidato < somma_attuale
            ):
                risparmio_pct = (
                    (somma_attuale - somma_candidato)
                    / somma_attuale
                    * 100
                )
                pagamento_piu_economici.append(
                    {
                        "id": modello["id"],
                        "prezzo": list(prezzo_candidato),
                        "risparmio_pct": round(risparmio_pct, 1),
                    }
                )

        confronto["ruoli"][ruolo] = {
            "in_uso": modello_in_uso,
            "prezzo_attuale": list(prezzo_attuale),
            "gratuiti_nuovi": gratuiti_nuovi,
            "pagamento_piu_economici": pagamento_piu_economici,
        }

    return confronto


def save_snapshot(catalog: List[Dict[str, Any]], giorno: str, cartella: Path = HISTORY_DIR) -> Path:
    """Scrive nella cartella il file giorno.json con, per ogni modello, id, prompt, completion e free. Crea la cartella se manca. Non tocca gli snapshot di altri giorni. Se il file del giorno esiste lo riscrive: due esecuzioni nello stesso giorno sono legittime."""
    cartella.mkdir(parents=True, exist_ok=True)
    percorso = cartella / f"{giorno}.json"
    
    snapshot = {}
    for model in catalog:
        snapshot[model["id"]] = {
            "id": model["id"],
            "prompt": model["pricing"]["prompt"],
            "completion": model["pricing"]["completion"],
            "free": is_free(model)
        }
    
    with open(percorso, "w", encoding="utf-8") as f:
        json.dump(snapshot, f, indent=2, ensure_ascii=False)
    
    return percorso


def load_last_snapshot(giorno: str, cartella: Path = HISTORY_DIR) -> Dict[str, Dict[str, Any]]:
    """Legge lo snapshot piu' recente ANTERIORE a giorno, indicizzato per id. Restituisce un dizionario vuoto se non esiste storico: al primo giro nessun modello puo' risultare diventato gratuito."""
    if not cartella.exists():
        return {}
    
    # Trova tutti i file .json nella cartella
    files = list(cartella.glob("*.json"))
    
    # Filtra e ordina i file per data
    valid_files = []
    for file in files:
        try:
            file_date = file.stem
            if file_date < giorno:
                valid_files.append((file_date, file))
        except Exception:
            continue
    
    if not valid_files:
        return {}
    
    # Ordina per data decrescente e prende il piu' recente
    valid_files.sort(key=lambda x: x[0], reverse=True)
    latest_file = valid_files[0][1]
    
    try:
        with open(latest_file, "r", encoding="utf-8") as f:
            return json.load(f)
    except Exception:
        return {}


def diventati_gratuiti(catalog: List[Dict[str, Any]], precedente: Dict[str, Dict[str, Any]]) -> List[str]:
    """Id gratuiti oggi che nello snapshot precedente avevano almeno una componente di prezzo maggiore di zero. Un id assente dallo snapshot precedente NON e' diventato gratuito: e' nuovo."""
    if not precedente:
        return []
    
    risultato = []
    
    for model in catalog:
        model_id = model["id"]
        if is_free(model):
            # Verifica se era a pagamento nello snapshot precedente
            if model_id in precedente:
                prev_model = precedente[model_id]
                # Se era a pagamento (almeno una componente > 0)
                if (float(prev_model["prompt"]) > 0 or float(prev_model["completion"]) > 0):
                    risultato.append(model_id)
    
    return risultato


def candidati_a_pagamento(catalog: List[Dict[str, Any]], modello_in_uso: str, prezzo_in_uso: Tuple[float, float]) -> List[Dict[str, Any]]:
    """Modelli a pagamento con somma dei prezzi inferiore a quella del modello in uso, esclusi i sentinella e i non pertinenti, ordinati per risparmio decrescente. Ogni voce porta il campo richiede_consenso a True. Non contiene mai il modello in uso."""
    risultato = []
    
    somma_in_uso = prezzo_in_uso[0] + prezzo_in_uso[1]
    
    for model in catalog:
        # Salta il modello in uso
        if model["id"] == modello_in_uso:
            continue
            
        # Salta i non pertinenti
        if not is_text_capable(model):
            continue
            
        # Salta i sentinella
        if is_price_sentinel(model):
            continue
            
        # Salta i gratuiti
        if is_free(model):
            continue
            
        try:
            prezzo = cost_per_mtok(model)
            somma_candidato = prezzo[0] + prezzo[1]
            
            # Solo se è economico
            if somma_candidato < somma_in_uso:
                risparmio_pct = ((somma_in_uso - somma_candidato) / somma_in_uso) * 100
                risultato.append({
                    "id": model["id"],
                    "prezzo": list(prezzo),
                    "risparmio_pct": round(risparmio_pct, 1),
                    "richiede_consenso": True
                })
        except ValueError:
            # Ignora i modelli con prezzo sentinella
            continue
    
    # Ordina per risparmio decrescente
    risultato.sort(key=lambda x: x["risparmio_pct"], reverse=True)
    
    return risultato


def scrivi_proposte(esiti: Dict[str, Any], percorso: Path = PROPOSALS_PATH) -> Path:
    """Scrive data/model_scout_proposals.json con schema_version nosai.model.scout.proposals.v1 e le chiavi generato_il, adottabili, da_provare, diventati_gratuiti, candidati_a_pagamento, bocciati. Ogni voce a pagamento porta richiede_consenso True. Il file e' un'uscita di sola lettura per l'hub: il guardiano non chiama mai mcp_role_promote_binding."""
    proposte = {
        "schema_version": "nosai.model.scout.proposals.v1",
        "generato_il": datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
        "adottabili": esiti.get("adottabili", []),
        "da_provare": esiti.get("da_provare", []),
        "diventati_gratuiti": esiti.get("diventati_gratuiti", []),
        "candidati_a_pagamento": esiti.get("candidati_a_pagamento", []),
        "bocciati": esiti.get("bocciati", [])
    }
    
    # Assicura che ogni voce a pagamento abbia richiede_consenso=True
    for candidato in proposte["candidati_a_pagamento"]:
        candidato["richiede_consenso"] = True
    
    percorso.parent.mkdir(parents=True, exist_ok=True)
    with open(percorso, "w", encoding="utf-8") as f:
        json.dump(proposte, f, indent=2, ensure_ascii=False)
    
    return percorso


def render_report(confronto: Dict[str, Any]) -> str:
    """Rapporto Markdown con sezioni obbligatorie in questo ordine: Adottabili senza consenso; Da provare al banco; Diventati gratuiti; Candidati a pagamento con consenso dell operatore; Bocciati da non riproporre; Modelli del roster spariti dal catalogo. La sezione dei bocciati riporta per ogni voce il motivo registrato."""
    linee = ["# Report OpenRouter", ""]

    # Sezione 1: Adottabili senza consenso
    adottabili = []
    for ruolo, dettagli in confronto.get("ruoli", {}).items():
        if dettagli.get("gratuiti_nuovi"):
            adottabili.extend(dettagli["gratuiti_nuovi"])
    
    if adottabili:
        linee.extend([
            "## Adottabili senza consenso",
            "",
            "I seguenti modelli gratuiti sono pronti per essere adottati senza ulteriore approvazione.",
            ""
        ])
        for modello in adottabili:
            linee.append(f"- `{modello}`")
        linee.append("")
    else:
        linee.extend([
            "## Adottabili senza consenso",
            "",
            "Nessun modello gratuito è pronto per essere adottato senza ulteriore approvazione.",
            ""
        ])

    # Sezione 2: Da provare al banco
    da_provare = []
    for ruolo, dettagli in confronto.get("ruoli", {}).items():
        if dettagli.get("gratuiti_nuovi"):
            da_provare.extend(dettagli["gratuiti_nuovi"])
    
    if da_provare:
        linee.extend([
            "## Da provare al banco",
            "",
            "I seguenti modelli gratuiti sono stati identificati come potenziali candidati per il test.",
            ""
        ])
        for modello in da_provare:
            linee.append(f"- `{modello}`")
        linee.append("")
    else:
        linee.extend([
            "## Da provare al banco",
            "",
            "Nessun modello gratuito è stato identificato come potenziale candidato per il test.",
            ""
        ])

    # Sezione 3: Diventati gratuiti
    diventati_gratuiti = []
    for ruolo, dettagli in confronto.get("ruoli", {}).items():
        if dettagli.get("gratuiti_nuovi"):
            diventati_gratuiti.extend(dettagli["gratuiti_nuovi"])
    
    if diventati_gratuiti:
        linee.extend([
            "## Diventati gratuiti",
            "",
            "I seguenti modelli sono diventati gratuiti rispetto alla versione precedente.",
            ""
        ])
        for modello in diventati_gratuiti:
            linee.append(f"- `{modello}`")
        linee.append("")
    else:
        linee.extend([
            "## Diventati gratuiti",
            "",
            "Nessun modello è diventato gratuito rispetto alla versione precedente.",
            ""
        ])

    # Sezione 4: Candidati a pagamento con consenso dell operatore
    candidati_pagamento = []
    for ruolo, dettagli in confronto.get("ruoli", {}).items():
        candidati_pagamento.extend(dettagli.get("pagamento_piu_economici", []))
    
    if candidati_pagamento:
        linee.extend([
            "## Candidati a pagamento con consenso dell operatore",
            "",
            "L'adozione di questi modelli a pagamento richiede l'approvazione esplicita dell'operatore.",
            ""
        ])
        for candidato in candidati_pagamento:
            prezzo = candidato["prezzo"]
            linee.append(
                f"- `{candidato['id']}`: prompt ${prezzo[0]:.6f}/M, "
                f"completion ${prezzo[1]:.6f}/M, risparmio "
                f"{candidato['risparmio_pct']:.1f}%"
            )
        linee.append("")
    else:
        linee.extend([
            "## Candidati a pagamento con consenso dell operatore",
            "",
            "Nessun modello a pagamento è stato identificato come candidato economico.",
            ""
        ])

    # Sezione 5: Bocciati da non riproporre
    bocciati = confronto.get("bocciati", [])
    if bocciati:
        linee.extend([
            "## Bocciati da non riproporre",
            "",
            "I seguenti modelli gratuiti sono stati bocciati e non dovrebbero essere riproposti.",
            ""
        ])
        for item in bocciati:
            linee.append(f"- `{item['id']}`: {item['motivo']}")
        linee.append("")
    else:
        linee.extend([
            "## Bocciati da non riproporre",
            "",
            "Nessun modello gratuito è stato bocciato.",
            ""
        ])

    # Sezione 6: Modelli del roster spariti dal catalogo
    roster_mancanti = confronto.get("roster_mancanti", [])
    if roster_mancanti:
        linee.extend([
            "## Modelli del roster spariti dal catalogo",
            "",
            "I seguenti modelli del roster non sono più presenti nel catalogo OpenRouter.",
            ""
        ])
        for modello in roster_mancanti:
            linee.append(f"- `{modello}`")
        linee.append("")
    else:
        linee.extend([
            "## Modelli del roster spariti dal catalogo",
            "",
            "Tutti i modelli del roster sono presenti nel catalogo OpenRouter.",
            ""
        ])

    return "\n".join(linee).rstrip() + "\n"


def main() -> int:
    """Legge da sys.argv gli argomenti facoltativi --giorno YYYY-MM-DD, che per default e' la data odierna UTC, e --dry-run, che non scrive nulla e stampa il rapporto. Restituisce 0 in caso di successo e 1 se un fornitore non risponde. Stampa una riga di riepilogo con i conteggi per stato."""
    giorno = datetime.now(timezone.utc).strftime("%Y-%m-%d")
    dry_run = False
    
    # Parsing degli argomenti
    args = sys.argv[1:]
    i = 0
    while i < len(args):
        if args[i] == "--giorno" and i + 1 < len(args):
            giorno = args[i + 1]
            i += 2
        elif args[i] == "--dry-run":
            dry_run = True
            i += 1
        else:
            i += 1
    
    try:
        catalogo = fetch_catalog()
    except RuntimeError as errore:
        print(f"Errore nel caricamento del catalogo: {errore}")
        return 1

    # Carica il roster
    validati, scartati = load_roster()
    
    # Carica lo snapshot precedente
    precedente = load_last_snapshot(giorno)
    
    # Classifica i modelli
    esiti = {
        "adottabili": [],
        "da_provare": [],
        "diventati_gratuiti": [],
        "candidati_a_pagamento": [],
        "bocciati": []
    }
    
    # Itera su tutti i modelli del catalogo
    for model in catalogo:
        # Determina il provider_id
        provider_id = "openrouter"
        if model["id"].startswith("groq:"):
            provider_id = "groq"
        elif model["id"].startswith("nvidia:"):
            provider_id = "nvidia"
        elif model["id"].startswith("mistral:"):
            provider_id = "mistral"
        
        # Classifica il modello
        classificato = classifica_modello(model, validati, scartati, precedente, provider_id)
        
        # Partiziona per stato
        stato = classificato["stato"]
        modello_id = classificato["id"]
        
        if stato == "validato":
            # Solo se è gratuito, va in adottabili
            if classificato["adottabile"]:
                esiti["adottabili"].append(modello_id)
            else:
                # Se non è gratuito, non va in nessuna lista
                pass
        elif stato == "nuovo_gratuito":
            # Va in da_provare
            esiti["da_provare"].append(modello_id)
        elif stato == "scartato":
            # Va in bocciati
            esiti["bocciati"].append({
                "id": modello_id,
                "motivo": classificato["motivo"]
            })
        elif stato == "diventato_gratuito":
            # Va in diventati_gratuiti
            esiti["diventati_gratuiti"].append(modello_id)
        elif stato == "a_pagamento":
            # Va in candidati_a_pagamento
            # Ma prima verifica che non sia sentinella o non pertinente
            if is_text_capable(model) and not is_price_sentinel(model):
                try:
                    prezzo = cost_per_mtok(model)
                    # Calcola il risparmio rispetto al modello in uso
                    # Per ora non lo calcoliamo, ma possiamo aggiungerlo in futuro
                    esiti["candidati_a_pagamento"].append({
                        "id": modello_id,
                        "prezzo": list(prezzo),
                        "risparmio_pct": 0.0,
                        "richiede_consenso": True
                    })
                except ValueError:
                    # Ignora i modelli con prezzo sentinella
                    pass
        elif stato == "sentinella" or stato == "non_pertinente":
            # Non va in nessuna lista
            pass
    
    # Verifica che le liste siano disgiunte
    # Costruisci insiemi per verificare disgiunzione
    adottabili_set = set(esiti["adottabili"])
    da_provare_set = set(esiti["da_provare"])
    diventati_gratuiti_set = set(esiti["diventati_gratuiti"])
    bocciati_set = set(item["id"] for item in esiti["bocciati"])
    candidati_set = set(item["id"] for item in esiti["candidati_a_pagamento"])
    
    # Verifica che non ci siano sovrapposizioni
    assert len(adottabili_set & da_provare_set) == 0, "adottabili e da_provare non sono disgiunti"
    assert len(adottabili_set & diventati_gratuiti_set) == 0, "adottabili e diventati_gratuiti non sono disgiunti"
    assert len(adottabili_set & bocciati_set) == 0, "adottabili e bocciati non sono disgiunti"
    assert len(adottabili_set & candidati_set) == 0, "adottabili e candidati_a_pagamento non sono disgiunti"
    assert len(da_provare_set & diventati_gratuiti_set) == 0, "da_provare e diventati_gratuiti non sono disgiunti"
    assert len(da_provare_set & bocciati_set) == 0, "da_provare e bocciati non sono disgiunti"
    assert len(da_provare_set & candidati_set) == 0, "da_provare e candidati_a_pagamento non sono disgiunti"
    assert len(diventati_gratuiti_set & bocciati_set) == 0, "diventati_gratuiti e bocciati non sono disgiunti"
    assert len(diventati_gratuiti_set & candidati_set) == 0, "diventati_gratuiti e candidati_a_pagamento non sono disgiunti"
    assert len(bocciati_set & candidati_set) == 0, "bocciati e candidati_a_pagamento non sono disgiunti"
    
    # Scrivi le proposte
    if not dry_run:
        scrivi_proposte(esiti, PROPOSALS_PATH)
        save_snapshot(catalogo, giorno, HISTORY_DIR)
        
        # Scrivi il rapporto
        REPORT_PATH.parent.mkdir(parents=True, exist_ok=True)
        report_content = render_report(compare_to_roster(catalogo, ROSTER))
        REPORT_PATH.write_text(report_content, encoding="utf-8")
    
    # Stampa il riepilogo
    adottabili_count = len(esiti["adottabili"])
    da_provare_count = len(esiti["da_provare"])
    diventati_gratuiti_count = len(esiti["diventati_gratuiti"])
    candidati_count = len(esiti["candidati_a_pagamento"])
    bocciati_count = len(esiti["bocciati"])
    
    print(
        f"Report generato in {REPORT_PATH}: "
        f"{adottabili_count} adottabili, "
        f"{da_provare_count} da provare, "
        f"{diventati_gratuiti_count} diventati gratuiti, "
        f"{candidati_count} candidati a pagamento, "
        f"{bocciati_count} bocciati."
    )
    
    if dry_run:
        print(render_report(compare_to_roster(catalogo, ROSTER)))
    
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
