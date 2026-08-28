"""Resumable in-process agent sessions and deterministic lifecycle state."""
from dataclasses import dataclass, field
from datetime import datetime, timezone
from typing import Any
from uuid import uuid4


TERMINAL_STATUSES = {"STOPPED", "FAILED", "COMPLETED"}


@dataclass
class AgentSession:
    session_id: str = field(default_factory=lambda: str(uuid4()))
    goal: str = ""
    state: dict[str, Any] = field(default_factory=dict)
    events: list[dict[str, Any]] = field(default_factory=list)
    status: str = "CREATED"
    updated_at: str = field(default_factory=lambda: datetime.now(timezone.utc).isoformat())

    def checkpoint(self, **state: Any) -> None:
        self.state.update(state)
        self.updated_at = datetime.now(timezone.utc).isoformat()
        self.events.append({"type": "CHECKPOINT", "state": dict(state), "at": self.updated_at})

    def transition(self, status: str) -> None:
        self.status = status
        self.updated_at = datetime.now(timezone.utc).isoformat()
        self.events.append({"type": "STATUS", "status": status, "at": self.updated_at})

    def start(self) -> None:
        self.transition("RUNNING")

    def pause(self) -> None:
        self.transition("PAUSED")

    def complete(self) -> None:
        self.transition("COMPLETED")

    def fail(self, reason: str) -> None:
        self.checkpoint(failure_reason=reason)
        self.transition("FAILED")


class SessionManager:
    def __init__(self) -> None:
        self._sessions: dict[str, AgentSession] = {}

    def create(self, goal: str = "") -> AgentSession:
        session = AgentSession(goal=goal)
        self._sessions[session.session_id] = session
        return session

    def get(self, session_id: str) -> AgentSession:
        return self._sessions[session_id]

    def stop(self, session_id: str) -> AgentSession:
        session = self.get(session_id)
        session.transition("STOPPED")
        return session

    def resume(self, session_id: str) -> AgentSession:
        session = self.get(session_id)
        if session.status == "COMPLETED":
            return session
        session.transition("RESUMED")
        return session
