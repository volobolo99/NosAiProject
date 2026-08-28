"""Adaptive recovery controller for the autonomous runtime."""
from __future__ import annotations
from dataclasses import dataclass, field
from typing import Any, Callable, Mapping, Sequence

from .context_slimming import VRAMContextSlimmer


@dataclass(frozen=True)
class RecoveryEvent:
    reason: str
    step_index: int
    attempt: int
    strategy: str = "retry"
    requested_mode: str | None = None
    context: Mapping[str, Any] = field(default_factory=dict)


class RecoveryController:
    """Select and record adaptive recovery strategies and runtime modes."""
    def __init__(self, on_recover: Callable[[RecoveryEvent], None] | None = None,
                 context_slimmer: VRAMContextSlimmer | None = None,
                 strategy_selector: Callable[[str, int, int], str] | None = None) -> None:
        self.on_recover = on_recover
        self.context_slimmer = context_slimmer or VRAMContextSlimmer()
        self.strategy_selector = strategy_selector
        self.events: list[RecoveryEvent] = []
        self.error_history: list[dict[str, Any]] = []

    def recover(self, reason: str, step_index: int, attempt: int,
                error_history: Sequence[Mapping[str, Any] | str] | None = None) -> RecoveryEvent:
        history = list(error_history) if error_history is not None else [reason]
        self.error_history.extend(self.context_slimmer.compress(history))
        strategy = self.strategy_selector(reason, step_index, attempt) if self.strategy_selector else self._default_strategy(reason, attempt)
        event = RecoveryEvent(reason, step_index, attempt, strategy, self._requested_mode(strategy), {"errors": tuple(self.error_history[-3:])})
        self.events.append(event)
        if self.on_recover is not None:
            self.on_recover(event)
        return event

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
