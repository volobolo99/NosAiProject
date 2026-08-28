from __future__ import annotations

import json
import urllib.error
import urllib.request
from dataclasses import dataclass

from app.core.contracts import ActionType, Decision, Goal, WorldState


class LLMConnectionError(RuntimeError):
    pass


@dataclass(frozen=True, slots=True)
class LlamaCppConfig:
    host: str = "127.0.0.1"
    port: int = 8080
    model: str = "Qwen2.5-7B-Instruct-Q4_K_M.gguf"
    timeout: float = 2.0
    temperature: float = 0.1
    max_tokens: int = 256

    @property
    def endpoint(self) -> str:
        return f"http://{self.host}:{self.port}/v1/chat/completions"


class LlamaCppDecisionProvider:
    def __init__(self, config: LlamaCppConfig | None = None) -> None:
        self.config = config or LlamaCppConfig()

    def decide(self, world_state: WorldState, goal: Goal) -> Decision:
        payload = {
            "model": self.config.model,
            "messages": [
                {"role": "system", "content": "Return only JSON with action, target_id, confidence, reasoning."},
                {"role": "user", "content": json.dumps({"world_state": world_state.__dict__, "goal": goal.__dict__})},
            ],
            "temperature": self.config.temperature,
            "max_tokens": self.config.max_tokens,
            "response_format": {"type": "json_object"},
        }
        request = urllib.request.Request(
            self.config.endpoint,
            data=json.dumps(payload).encode("utf-8"),
            headers={"Content-Type": "application/json"},
            method="POST",
        )
        try:
            with urllib.request.urlopen(request, timeout=self.config.timeout) as response:
                body = json.loads(response.read().decode("utf-8"))
        except (urllib.error.URLError, TimeoutError, json.JSONDecodeError) as exc:
            raise LLMConnectionError(str(exc)) from exc

        try:
            content = body["choices"][0]["message"]["content"]
            data = json.loads(content) if isinstance(content, str) else content
            return Decision(
                action=ActionType(data["action"]),
                target_id=data.get("target_id"),
                confidence=float(data["confidence"]),
                reasoning=str(data.get("reasoning", "")),
            )
        except (KeyError, TypeError, ValueError, json.JSONDecodeError) as exc:
            raise LLMConnectionError(f"invalid llama.cpp response: {exc}") from exc
