"""Controllore adattivo di recupero del runtime."""
from __future__ import annotations
from dataclasses import dataclass, field
from typing import Any, Callable, Mapping, Sequence
import time

from .context_slimming import VRAMContextSlimmer

class CriticalDeadlock(RuntimeError):
    """Indica tre fallimenti consecutivi sulla stessa macro-azione."""

@dataclass(frozen=True)
class RecoveryEvent:
    reason: str
    step_index: int
    attempt: int
    strategy: str = "retry"
    requested_mode: str | None = None
    context: Mapping[str, Any] = field(default_factory=dict)
    backoff_seconds: float = 0.0

class RecoveryController:
    """Recupero adattivo con circuit breaker e backoff esponenziale."""
    def __init__(self, on_recover: Callable[[RecoveryEvent], None] | None = None,
                 context_slimmer: VRAMContextSlimmer | None = None,
                 strategy_selector: Callable[[str, int, int], str] | None = None,
                 max_attempts: int = 3, base_backoff_seconds: float = 1.0,
                 sleep: Callable[[float], None] = time.sleep) -> None:
        if max_attempts < 1 or base_backoff_seconds < 0:
            raise ValueError("parametri circuit breaker non validi")
        self.on_recover = on_recover
        self.context_slimmer = context_slimmer or VRAMContextSlimmer()
        self.strategy_selector = strategy_selector
        self.max_attempts = max_attempts
        self.base_backoff_seconds = base_backoff_seconds
        self._sleep = sleep
        self.events: list[RecoveryEvent] = []
        self.error_history: list[dict[str, Any]] = []
        self._failures: dict[str, int] = {}

    def recover(self, reason: str, step_index: int, attempt: int,
                error_history: Sequence[Mapping[str, Any] | str] | None = None,
                macro_action: str = "default") -> RecoveryEvent:
        failures = self._failures.get(macro_action, 0) + 1
        self._failures[macro_action] = failures
        if failures > self.max_attempts:
            raise CriticalDeadlock(f"macro-azione '{macro_action}' fallita {failures} volte consecutive")
        history = list(error_history) if error_history is not None else [reason]
        self.error_history.extend(self.context_slimmer.compress(history))
        strategy = self.strategy_selector(reason, step_index, attempt) if self.strategy_selector else self._default_strategy(reason, attempt)
        backoff = self.base_backoff_seconds * (2 ** (failures - 1)) if failures > 1 else 0.0
        if backoff:
            self._sleep(backoff)
        event = RecoveryEvent(reason, step_index, attempt, strategy, self._requested_mode(strategy), {"errors": tuple(self.error_history[-3:])}, backoff)
        self.events.append(event)
        if self.on_recover is not None:
            self.on_recover(event)
        return event

    def mark_success(self, macro_action: str = "default") -> None:
        self._failures.pop(macro_action, None)

    def reset(self) -> None:
        self._failures.clear()

    @staticmethod
    def _default_strategy(reason: str, attempt: int) -> str:
        if "thermal" in reason or "io_rate" in reason:
            return "cooling"
        if "timeout" in reason:
            return "degraded_replan"
        return "retry" if attempt <= 1 else "replan"

    @staticmethod
    def _requested_mode(strategy: str) -> str | None:
        return {"cooling": "COOLING", "degraded_replan": "DEGRADED", "replan": "RECOVERY"}.get(strategy)
