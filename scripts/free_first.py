from __future__ import annotations
import time
from dataclasses import dataclass, field
from typing import Callable, Dict, List, Optional, Sequence, Tuple
try:  # importabile sia da scripts/ nel path sia come scripts.free_first
    import free_chain
except ModuleNotFoundError:  # pragma: no cover
    from scripts import free_chain

BUDGET_PREDEFINITO: float = 120.0
PAUSA_PREDEFINITA: float = 60.0

class LimiteRaggiunto(RuntimeError):
    """Sollevata dal chiamante quando il provider risponde 429. Porta i secondi di Retry-After."""
    def __init__(self, messaggio: str, attendi: float = PAUSA_PREDEFINITA) -> None:
        super().__init__(messaggio)
        self.attendi = attendi

@dataclass
class Tentativo:
    modello: str
    gratuito: bool
    secondi: float
    accettato: bool
    motivo: str

@dataclass
class Esito:
    testo: str
    modello: str
    gratuito: bool
    accettato: bool
    secondi: float
    tentativi: List[Tentativo]

class Pausa:
    """Registro dei modelli temporaneamente esclusi perche hanno esaurito la quota."""
    def __init__(self) -> None:
        raise NotImplementedError

    def attiva(self, modello: str, secondi: float, adesso: float) -> None:
        raise NotImplementedError

    def attivo(self, modello: str, adesso: float) -> bool:
        raise NotImplementedError

    def filtra(self, modelli: Sequence[str], adesso: float) -> List[str]:
        raise NotImplementedError

def budget_residuo(iniziato: float, budget: float, adesso: float) -> float:
    """Secondi ancora disponibili per la cascata gratuita. Mai negativo."""
    raise NotImplementedError

def esegui(chiama_gruppo: Callable[[Sequence[str]], Tuple[str, str]], chiama_pagato: Callable[[], Tuple[str, str]], giudizio: Callable[[str], List[str]], modelli: Optional[Sequence[str]] = None, budget_secondi: float = BUDGET_PREDEFINITO, orologio: Callable[[], float] = time.monotonic, pausa: Optional[Pausa] = None) -> Esito:
    """Prova i gruppi gratuiti in ordine di velocita, accetta il primo testo che supera il giudizio, e ricade sul pagato quando nessuno passa o il budget e esaurito."""
    raise NotImplementedError
