"""
Catena di riserva gratuita: elenca i modelli a costo zero già validati dal banco e ritenta la chiamata sui successivi quando quello scelto fallisce.
"""

from typing import Any, Callable, Dict, List, Optional, Sequence, Tuple
import json
from pathlib import Path

FREE_ROSTER_PATH = Path(__file__).resolve().parent / 'free_roster.json'
LENTO_SECONDI = 200
GRUPPO_MAX = 3
PROVIDER_GUASTI = ['Novita']
# Il roster elenca piu' fornitori: questo modulo parla solo con OpenRouter.
FORNITORE_PREDEFINITO = 'openrouter'


def free_models(escludi_lenti: bool = True, fornitore: Optional[str] = FORNITORE_PREDEFINITO) -> List[str]:
    """
    Legge il file JSON indicato da FREE_ROSTER_PATH e restituisce gli identificativi nella chiave validati, ordinati per sec_medi crescente.
    Se escludi_lenti è True, scarta i modelli con sec_medi maggiore o uguale a LENTO_SECONDI.
    Il roster è multi-fornitore: fornitore seleziona una sola provenienza, None le restituisce tutte.
    Le voci storiche senza il campo contano come OpenRouter, che era l'unico fornitore.
    """
    try:
        with FREE_ROSTER_PATH.open('r', encoding='utf-8') as f:
            data = json.load(f)
    except (FileNotFoundError, json.JSONDecodeError):
        return []

    entries = data.get('validati', [])
    if not isinstance(entries, list):
        return []

    def get_sec_medi(entry):
        sec = entry.get('sec_medi')
        if isinstance(sec, (int, float)):
            return sec
        return float('inf')

    sorted_entries = sorted(entries, key=get_sec_medi)

    if escludi_lenti:
        sorted_entries = [e for e in sorted_entries if get_sec_medi(e) < LENTO_SECONDI]

    if fornitore is not None:
        sorted_entries = [
            e for e in sorted_entries
            if e.get('fornitore', FORNITORE_PREDEFINITO) == fornitore
        ]

    ids = [e.get('id') for e in sorted_entries if 'id' in e]
    return ids


def call_with_fallback(chiamata: Callable[[str], str], modelli: Sequence[str]) -> Tuple[str, str]:
    """
    Prova i modelli in ordine e restituisce la coppia testo prodotto e identificativo del modello che lo ha prodotto.
    Se tutti i modelli falliscono, solleva RuntimeError.
    """
    if not modelli:
        raise RuntimeError("Nessun modello fornito")

    failures = []
    for modello in modelli:
        try:
            testo = chiamata(modello)
            if not testo or testo.isspace():
                failures.append((modello, "risposta vuota o composta solo da spazi"))
                continue
            return (testo, modello)
        except Exception as e:
            failures.append((modello, f"eccezione: {e}"))
            continue

    error_lines = [f"Modello '{modello}' fallito: {reason}" for modello, reason in failures]
    error_msg = "\n".join(error_lines)
    raise RuntimeError(error_msg)


def gruppi(modelli: Sequence[str], dimensione: int = GRUPPO_MAX) -> List[List[str]]:
    """
    Suddivide i modelli in blocchi consecutivi lunghi al massimo la dimensione richiesta, mantenendo l'ordine originale.
    """
    if dimensione < 1:
        raise ValueError("La dimensione dei gruppi deve essere almeno 1")

    return [list(modelli[inizio:inizio + dimensione]) for inizio in range(0, len(modelli), dimensione)]


def corpo_richiesta(messaggi: List[Dict[str, Any]], gruppo: Sequence[str], max_tokens: int = 8192, temperatura: float = 0.0, solo_gratuiti: bool = True) -> Dict[str, Any]:
    """
    Costruisce il corpo della richiesta OpenRouter per un gruppo di modelli.
    """
    if not gruppo:
        raise ValueError("Il gruppo di modelli non può essere vuoto")
    if len(gruppo) > GRUPPO_MAX:
        raise ValueError(f"Il gruppo contiene più di {GRUPPO_MAX} modelli")

    provider: Dict[str, Any] = {"ignore": list(PROVIDER_GUASTI)}
    if solo_gratuiti:
        provider["max_price"] = {"prompt": 0, "completion": 0}

    return {
        "model": gruppo[0],
        "models": list(gruppo),
        "messages": messaggi,
        "max_tokens": max_tokens,
        "temperature": temperatura,
        "provider": provider,
    }


def testo_utile(payload: Dict[str, Any]) -> Tuple[str, str]:
    """
    Estrae dalla risposta il testo prodotto e l'identificativo del modello che lo ha prodotto.
    """
    try:
        contenuto = payload["choices"][0]["message"]["content"]
    except (KeyError, IndexError, TypeError) as errore:
        raise RuntimeError("La risposta non contiene un contenuto testuale valido") from errore

    if contenuto is None or not isinstance(contenuto, str) or not contenuto or contenuto.isspace():
        raise RuntimeError("La risposta non contiene un contenuto testuale utile")

    try:
        modello = payload["model"]
    except KeyError as errore:
        raise RuntimeError("La risposta non indica il modello che l'ha prodotta") from errore

    if not isinstance(modello, str):
        raise RuntimeError("La risposta non indica un modello valido")

    return contenuto, modello


def chiama_a_gruppi(post: Callable[[Dict[str, Any]], Dict[str, Any]], messaggi: List[Dict[str, Any]], modelli: Optional[Sequence[str]] = None, max_tokens: int = 8192, temperatura: float = 0.0, solo_gratuiti: bool = True) -> Tuple[str, str]:
    """
    Prova i gruppi di modelli in ordine, passando al successivo quando una risposta è mancante, vuota o causa un'eccezione.
    """
    modelli_da_provare = free_models() if modelli is None else modelli
    motivi_di_fallimento = []

    for indice, gruppo in enumerate(gruppi(modelli_da_provare), start=1):
        try:
            corpo = corpo_richiesta(messaggi, gruppo, max_tokens=max_tokens, temperatura=temperatura, solo_gratuiti=solo_gratuiti)
            payload = post(corpo)
            return testo_utile(payload)
        except RuntimeError as errore:
            motivi_di_fallimento.append(f"Gruppo {indice}: {errore}")
        except Exception as errore:
            motivi_di_fallimento.append(f"Gruppo {indice}: eccezione: {errore}")

    if not motivi_di_fallimento:
        raise RuntimeError("Nessun modello da provare")

    raise RuntimeError("\n".join(motivi_di_fallimento))
