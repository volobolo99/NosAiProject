"""Deterministic recovery actions for the autonomous runtime."""
from dataclasses import dataclass
from typing import Callable


@dataclass(frozen=True)
class RecoveryEvent:
    reason: str
    step_index: int
    attempt: int


class RecoveryController:
    """Small policy adapter; it never authorizes execution by itself."""
    def __init__(self, on_recover: Callable[[RecoveryEvent], None] | None = None) -> None:
        self.on_recover = on_recover
        self.events: list[RecoveryEvent] = []

    def recover(self, reason: str, step_index: int, attempt: int) -> RecoveryEvent:
        event = RecoveryEvent(reason, step_index, attempt)
        self.events.append(event)
        if self.on_recover is not None:
            self.on_recover(event)
        return event
