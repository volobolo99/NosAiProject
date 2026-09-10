from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Iterable

from .contracts import ProviderConfig, RouteDecision
from .bindings import RoleBindingRegistry
from .policy import McpPolicy
from .state import McpStateStore


@dataclass
class ModelRouter:
    policy: McpPolicy
    providers: list[ProviderConfig]
    role_bindings: RoleBindingRegistry | None = None

    @classmethod
    def from_config(cls, config: dict, policy: McpPolicy) -> "ModelRouter":
        providers = [ProviderConfig(**item) for item in config.get("providers", [])]
        binding_path = config.get("role_bindings_path", "data/mcp/role_bindings.json")
        state_path = config.get("state_path") or str(Path(binding_path).with_suffix(".sqlite3"))
        bindings = RoleBindingRegistry(binding_path, state_store=McpStateStore(state_path))
        return cls(policy=policy, providers=providers, role_bindings=bindings)

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

    def choose(self, capability: str | None = None, role_id: str | None = None) -> RouteDecision:
        if role_id and self.role_bindings is not None:
            binding = self.role_bindings.get(role_id)
            preferred = [binding["primary_model"], *binding["fallback_models"]]
            for model_id in preferred:
                selected = next((item for item in self.providers
                    if item.model_id == model_id
                    and item.enabled
                    and (capability is None or not item.capabilities or capability in item.capabilities)
                    and (not item.network_required or self.policy.network_enabled)), None)
                if selected is not None:
                    return RouteDecision(
                        provider_id=selected.provider_id,
                        model_id=selected.model_id,
                        reason="role binding " + role_id + " (" + binding["state"] + ")",
                        network_used=selected.network_required,
                    )
            raise RuntimeError("no qualified provider is available for role binding " + role_id)

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



