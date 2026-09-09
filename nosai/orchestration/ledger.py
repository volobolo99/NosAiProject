"""Registro dei consumi: una riga JSON Lines per ogni task eseguito da un modello.

Contratto: docs/contracts/orchestration_v1.json (ORCH-001).
Il registro di riferimento del progetto e' data/ai_task_ledger.jsonl e ogni riga
rispetta schemas/task_record.schema.json.
"""
from __future__ import annotations

import json
from dataclasses import asdict, dataclass, fields
from pathlib import Path


@dataclass(frozen=True)
class TaskRecord:
    """Il consumo di un singolo task: chi lo ha eseguito, quanto e' costato."""

    task_id: str
    model: str
    calls: int
    estimated_cost_usd: float
    status: str
    file: str
    words: int


class CostLedger:
    """Accesso in aggiunta e in lettura al registro dei consumi."""

    def __init__(self, path: Path) -> None:
        self.path = Path(path)

    def append(self, record: TaskRecord) -> None:
        """Aggiunge una riga al registro.

        Precondizione: calls maggiore o uguale a 1 e estimated_cost_usd maggiore
        o uguale a 0, altrimenti solleva ValueError.
        Postcondizione: aggiunge esattamente una riga e non riscrive le righe
        gia' presenti. La directory padre viene creata se assente.
        """
        if record.calls < 1:
            raise ValueError("calls deve essere maggiore o uguale a 1: {}".format(record.calls))
        if record.estimated_cost_usd < 0:
            raise ValueError(
                "estimated_cost_usd non puo' essere negativo: {}".format(record.estimated_cost_usd)
            )

        self.path.parent.mkdir(parents=True, exist_ok=True)
        with self.path.open("a", encoding="utf-8") as handle:
            handle.write(json.dumps(asdict(record), ensure_ascii=False) + "\n")

    def read_all(self) -> list[TaskRecord]:
        """Legge il registro riga per riga.

        Postcondizione: su file assente restituisce lista vuota e non solleva.
        Postcondizione: una riga malformata viene saltata senza interrompere la
        lettura, sia che non sia JSON, sia che sia JSON con campi non previsti.
        Postcondizione: il file non viene mai caricato interamente in memoria.
        """
        if not self.path.exists():
            return []

        attesi = {campo.name for campo in fields(TaskRecord)}
        letti: list[TaskRecord] = []
        with self.path.open("r", encoding="utf-8") as handle:
            for riga in handle:
                riga = riga.strip()
                if not riga:
                    continue
                try:
                    dati = json.loads(riga)
                except json.JSONDecodeError:
                    continue
                if not isinstance(dati, dict) or set(dati) != attesi:
                    continue
                try:
                    letti.append(TaskRecord(**dati))
                except TypeError:
                    continue
        return letti

    def summary_by_model(self) -> dict[str, dict[str, float]]:
        """Riepiloga i consumi per modello.

        Postcondizione: per ogni modello riporta calls, tasks ed
        estimated_cost_usd.
        """
        riepilogo: dict[str, dict[str, float]] = {}
        for record in self.read_all():
            voce = riepilogo.setdefault(
                record.model, {"calls": 0, "tasks": 0, "estimated_cost_usd": 0.0}
            )
            voce["calls"] += record.calls
            voce["tasks"] += 1
            voce["estimated_cost_usd"] += record.estimated_cost_usd
        return riepilogo

    def total_estimated_cost(self) -> float:
        """Restituisce il costo stimato complessivo del registro."""
        return sum(record.estimated_cost_usd for record in self.read_all())
