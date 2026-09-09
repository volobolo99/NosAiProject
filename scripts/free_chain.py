"""
Catena di riserva gratuita: elenca i modelli a costo zero già validati dal banco e ritenta la chiamata sui successivi quando quello scelto fallisce.
"""

from typing import List, Sequence, Tuple, Callable
import json
from pathlib import Path

FREE_ROSTER_PATH = Path(__file__).resolve().parent / 'free_roster.json'
LENTO_SECONDI = 200

def free_models(escludi_lenti: bool = True) -> List[str]:
    """
    Legge il file JSON indicato da FREE_ROSTER_PATH e restituisce gli identificativi nella chiave validati, ordinati per sec_medi crescente.
    Se escludi_lenti è True, scarta i modelli con sec_medi maggiore o uguale a LENTO_SECONDI.
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
