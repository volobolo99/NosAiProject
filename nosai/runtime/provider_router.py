"""Model-agnostic provider registry and local-first routing."""
from .contracts import DecisionProvider, ProviderCandidate, ResourceSnapshot, RuntimeContext
from .policy import RoutingPolicy


class ProviderRegistry:
    def __init__(self) -> None:
        self._providers: dict[str, DecisionProvider] = {}

    def register(self, provider: DecisionProvider) -> None:
        provider_id = provider.capabilities.provider_id
        if provider_id in self._providers:
            raise ValueError(f"provider_already_registered:{provider_id}")
        self._providers[provider_id] = provider

    def get(self, provider_id: str) -> DecisionProvider:
        return self._providers[provider_id]

    def all(self) -> tuple[DecisionProvider, ...]:
        return tuple(self._providers.values())


class ProviderRouter:
    def __init__(self, registry: ProviderRegistry, policy: RoutingPolicy | None = None) -> None:
        self.registry = registry
        self.policy = policy or RoutingPolicy()

    def select(self, context: RuntimeContext, resources: ResourceSnapshot) -> ProviderCandidate:
        candidates: list[ProviderCandidate] = []
        for provider in self.registry.all():
            caps = provider.capabilities
            if context.requires_local and not caps.local:
                continue
            score = self.policy.score(context, resources, caps.local)
            if score != float("-inf"):
                candidates.append(ProviderCandidate(provider, score, "policy_score"))
        if not candidates:
            raise RuntimeError("no_provider_satisfies_policy")
        return max(candidates, key=lambda candidate: candidate.score)
