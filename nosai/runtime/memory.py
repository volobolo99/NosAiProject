"""In-process memory bus for runtime state and evidence.

Persistent SQLite storage remains a separate implementation gate; this bus
provides the contract needed by the runtime without coupling decision code to
storage.
"""
from collections import defaultdict, deque
from dataclasses import dataclass
from typing import Any


@dataclass(frozen=True)
class MemoryEvent:
    session_id: str
    kind: str
    payload: dict[str, Any]


class MemoryBus:
    def __init__(self, max_events_per_session: int = 512) -> None:
        self._events: dict[str, deque[MemoryEvent]] = defaultdict(lambda: deque(maxlen=max_events_per_session))

    def publish(self, event: MemoryEvent) -> None:
        self._events[event.session_id].append(event)

    def recent(self, session_id: str, limit: int = 50) -> tuple[MemoryEvent, ...]:
        events = self._events.get(session_id, ())
        return tuple(events)[-limit:]
