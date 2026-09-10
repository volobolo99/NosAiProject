from __future__ import annotations

from dataclasses import dataclass
from typing import Iterable

from .contracts import ProviderConfig, RouteDecision
from .policy import McpPolicy


@dataclass
class ModelRouter:
    policy: McpPolicy
    providers: list[ProviderConfig]

    @classmethod
    def from_config(cls, config: dict, policy: McpPolicy) -> "ModelRouter":
        providers = [ProviderConfig(**item) for item in config.get("providers", [])]
        return cls(policy=policy, providers=providers)

    def catalog(self) -> list[dict]:
        return [
            {
                "provider_id": item.provider_id,
                "model_id": item.model_id,
                "tier": item.tier,
                "enabled": item.enabled,
                "network_required": item.network_required,
                "capabilities": list(item.capabilities),
            }
            for item in sorted(self.providers, key=lambda provider: provider.tier)
        ]

    def choose(self, capability: str | None = None) -> RouteDecision:
        candidates: Iterable[ProviderConfig] = (
            item for item in sorted(self.providers, key=lambda provider: provider.tier)
            if item.enabled
            and (capability is None or not item.capabilities or capability in item.capabilities)
            and (not item.network_required or self.policy.network_enabled)
        )
        selected = next(candidates, None)
        if selected is None:
            raise RuntimeError("no qualified provider is available under the current MCP policy")
        return RouteDecision(
            provider_id=selected.provider_id,
            model_id=selected.model_id,
            reason="lowest qualified tier under current policy",
            network_used=selected.network_required,
        )


