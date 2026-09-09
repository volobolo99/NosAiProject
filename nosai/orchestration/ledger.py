from __future__ import annotations
from dataclasses import dataclass, field
from pathlib import Path
import json

@dataclass(frozen=True)
class TaskRecord:
    task_id: str
    model: str
    calls: int
    estimated_cost_usd: float
    status: str
    file: str
    words: int

class CostLedger:
    def __init__(self, path: Path) -> None:
        self.path = path

    def append(self, record: TaskRecord) -> None:
        """Aggiunge una riga JSON Lines al registro dei consumi.

        Precondizione: calls >= 1 e estimated_cost_usd >= 0
        Postcondizione: aggiunge esattamente una riga e non riscrive le righe esistenti
        """
        raise NotImplementedError

    def read_all(self) -> list[TaskRecord]:
        """Legge tutte le righe del registro dei consumi.

        Postcondizione: su file assente restituisce lista vuota e non solleva
        Postcondizione: una riga malformata viene saltata senza interrompere la lettura
        Postcondizione: il file viene letto riga per riga, mai caricato interamente in memoria
        """
        raise NotImplementedError

    def summary_by_model(self) -> dict[str, dict[str, float]]:
        """Riporta una sommario dei consumi per ogni modello.

        Postcondizione: per ogni modello riporta calls, tasks ed estimated_cost_usd
        """
        raise NotImplementedError

    def total_estimated_cost(self) -> float:
        """Calcola il costo totale stimato dei consumi.

        Postcondizione: restituisce il costo totale stimato come float
        """
        raise NotImplementedError
