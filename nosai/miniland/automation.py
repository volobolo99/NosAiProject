from __future__ import annotations

from dataclasses import dataclass, field
from enum import Enum
from random import Random
from time import monotonic
from typing import Callable, Protocol


class MinilandAction(str, Enum):
    FISH = "fish"
    COLLECT = "collect"
    WAIT = "wait"
    STOP = "stop"


@dataclass(frozen=True)
class MinilandCommand:
    action: MinilandAction
    duration_ms: int = 0
    metadata: dict[str, str] = field(default_factory=dict)


@dataclass(frozen=True)
class FishingResult:
    success: bool
    catches: int = 0
    message: str = ""


class MinilandAdapter(Protocol):
    def send_command(self, command: MinilandCommand) -> bool: ...
    def read_result(self) -> FishingResult: ...


class MinilandAutomation:
    """Controller Miniland indipendente dal client di gioco reale.

    L'adapter è il solo confine I/O. Il controller non contiene codice di
    cattura input, invio pacchetti o modifica del client.
    """

    def __init__(self, adapter: MinilandAdapter, rng: Random | None = None) -> None:
        self.adapter = adapter
        self.rng = rng or Random()
        self._running = False

    @property
    def running(self) -> bool:
        return self._running

    def start(self) -> None:
        self._running = True

    def stop(self) -> bool:
        self._running = False
        return self.adapter.send_command(MinilandCommand(MinilandAction.STOP))

    def execute(self, command: MinilandCommand) -> FishingResult:
        if not self._running and command.action is not MinilandAction.STOP:
            raise RuntimeError("automazione Miniland non avviata")
        if command.duration_ms < 0:
            raise ValueError("duration_ms non può essere negativo")
        if not self.adapter.send_command(command):
            return FishingResult(False, message="comando rifiutato dall'adapter")
        return self.adapter.read_result()


class FishingAutomation(MinilandAutomation):
    """Automazione della pesca tramite adapter astratto."""

    def fish_once(self, duration_ms: int = 1000) -> FishingResult:
        return self.execute(MinilandCommand(MinilandAction.FISH, duration_ms))

    def collect(self) -> FishingResult:
        return self.execute(MinilandCommand(MinilandAction.COLLECT))

    def run_cycle(self, cycles: int = 1, duration_ms: int = 1000) -> list[FishingResult]:
        if cycles < 1:
            raise ValueError("cycles deve essere almeno 1")
        results: list[FishingResult] = []
        self.start()
        started = monotonic()
        try:
            for _ in range(cycles):
                result = self.fish_once(duration_ms)
                results.append(result)
                if not result.success:
                    break
        finally:
            self.stop()
        _ = started
        return results
