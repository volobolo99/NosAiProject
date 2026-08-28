"""Typed runtime event plane for audit, telemetry, memory and replay.

Events are execution facts only: publishing an event never grants execution authority.
"""
from __future__ import annotations
from dataclasses import dataclass, field
from datetime import datetime, timezone
from typing import Any, Callable
from uuid import uuid4

@dataclass(frozen=True)
class RuntimeEvent:
    event_type: str
    source: str
    session_id: str
    run_id: str
    task_id: str
    payload: dict[str, Any] = field(default_factory=dict)
    event_id: str = field(default_factory=lambda: str(uuid4()))
    parent_event_id: str | None = None
    timestamp: str = field(default_factory=lambda: datetime.now(timezone.utc).isoformat())
    schema_version: int = 1

class EventBus:
    """Synchronous deterministic bus; subscribers are observers, never executors."""
    def __init__(self) -> None:
        self._events: list[RuntimeEvent] = []
        self._subscribers: list[Callable[[RuntimeEvent], None]] = []

    def subscribe(self, handler: Callable[[RuntimeEvent], None]) -> None:
        self._subscribers.append(handler)

    def publish(self, event: RuntimeEvent) -> None:
        self._events.append(event)
        for handler in tuple(self._subscribers):
            handler(event)

    def history(self) -> tuple[RuntimeEvent, ...]:
        return tuple(self._events)

    def by_run(self, run_id: str) -> tuple[RuntimeEvent, ...]:
        return tuple(e for e in self._events if e.run_id == run_id)

    def clear(self) -> None:
        self._events.clear()
