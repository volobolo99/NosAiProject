from __future__ import annotations
from dataclasses import dataclass, field

@dataclass
class AgentMessage:
    task_id: str
    objective: str
    input: str
    expected_output: str
    files: list[str]
    dependencies: list[str]
    risks: list[str]
    required_tests: list[str]
    status: str
    model: str
    summary: str
    confidence: float

    def to_dict(self) -> dict:
        raise NotImplementedError

    @classmethod
    def from_dict(cls, payload: dict) -> AgentMessage:
        raise NotImplementedError

def validate_message(payload: dict) -> list[str]:
    raise NotImplementedError
