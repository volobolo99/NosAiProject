from __future__ import annotations

import json
import os
import urllib.error
import urllib.request
from dataclasses import dataclass

from .contracts import RouteDecision
from .enforcement import require_capability
from .router import ModelRouter
from .secrets import SecretStore


@dataclass
class InferenceGateway:
    router: ModelRouter
    secrets: SecretStore | None

    def infer(self, prompt: str, capability: str | None = None, timeout: float = 30.0, role_id: str | None = None) -> dict:
        if not prompt.strip():
            raise ValueError("prompt is required")
        require_capability(role_id, capability)
        decision: RouteDecision = self.router.choose(capability, role_id=role_id)
        if decision.provider_id == "ollama-local":
            endpoint = os.getenv("NOSAI_OLLAMA_URL", "http://127.0.0.1:11434/api/generate")
            payload = {"model": decision.model_id, "prompt": prompt, "stream": False}
            return self._post_json(endpoint, payload, timeout, {})

        if self.secrets is None:
            raise RuntimeError("encrypted secret store is not configured")
        key = self.secrets.resolve_for_internal_call(decision.provider_id)
        if not key:
            raise RuntimeError(f"credential missing for provider {decision.provider_id}")
        endpoint = {
            "groq-free": os.getenv("NOSAI_GROQ_URL", "https://api.groq.com/openai/v1/chat/completions"),
            "openrouter-free": os.getenv("NOSAI_OPENROUTER_URL", "https://openrouter.ai/api/v1/chat/completions"),
        }.get(decision.provider_id)
        if not endpoint:
            raise RuntimeError(f"provider endpoint not configured: {decision.provider_id}")
        payload = {"model": decision.model_id, "messages": [{"role": "user", "content": prompt}], "stream": False}
        headers = {"Authorization": f"Bearer {key}"}
        return self._post_json(endpoint, payload, timeout, headers)

    @staticmethod
    def _post_json(endpoint: str, payload: dict, timeout: float, headers: dict[str, str]) -> dict:
        request = urllib.request.Request(
            endpoint,
            data=json.dumps(payload).encode("utf-8"),
            headers={"Content-Type": "application/json", **headers},
            method="POST",
        )
        try:
            with urllib.request.urlopen(request, timeout=timeout) as response:
                value = json.loads(response.read().decode("utf-8"))
        except (urllib.error.URLError, TimeoutError, json.JSONDecodeError) as exc:
            raise RuntimeError(f"provider request failed: {exc}") from exc
        if not isinstance(value, dict):
            raise RuntimeError("provider returned a non-object response")
        return value

