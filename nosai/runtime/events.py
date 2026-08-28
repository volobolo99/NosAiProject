"""Piano eventi runtime con coda limitata e perdita controllata dei log non critici."""
from __future__ import annotations
from dataclasses import dataclass, field
from datetime import datetime, timezone
from typing import Any, Callable
from uuid import uuid4
from collections import deque

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
    critical: bool = False

class EventBus:
    """Bus deterministico bounded; gli iscritti sono osservatori, mai esecutori."""
    def __init__(self, max_events: int = 4096) -> None:
        if max_events < 1:
            raise ValueError("max_events deve essere positivo")
        self._events: deque[RuntimeEvent] = deque(maxlen=max_events)
        self._max_events = max_events
        self._subscribers: list[Callable[[RuntimeEvent], None]] = []
        self._dropped_noncritical = 0

    def subscribe(self, handler: Callable[[RuntimeEvent], None]) -> None:
        self._subscribers.append(handler)

    def publish(self, event: RuntimeEvent) -> None:
        if len(self._events) >= self._max_events:
            if event.critical:
                self._events.popleft()
            else:
                self._dropped_noncritical += 1
                return
        self._events.append(event)
        for handler in tuple(self._subscribers):
            handler(event)

    def history(self) -> tuple[RuntimeEvent, ...]:
        return tuple(self._events)

    def by_run(self, run_id: str) -> tuple[RuntimeEvent, ...]:
        return tuple(e for e in self._events if e.run_id == run_id)

    @property
    def dropped_noncritical(self) -> int:
        return self._dropped_noncritical

    def clear(self) -> None:
        self._events.clear()
