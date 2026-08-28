"""Explicit tool registry; tools are capability declarations, not LLM privileges."""
from dataclasses import dataclass
from typing import Callable, Mapping
from .trust import TrustTier

@dataclass(frozen=True)
class ToolSpec:
    name: str
    handler: Callable[[Mapping[str, object]], object]
    required_tier: TrustTier = TrustTier.SIMULATE
    reversible: bool = True
    local_only: bool = True

class ToolRegistry:
    def __init__(self):
        self._tools: dict[str, ToolSpec] = {}

    def register(self, spec: ToolSpec) -> None:
        if spec.name in self._tools:
            raise ValueError(f"tool already registered: {spec.name}")
        self._tools[spec.name] = spec

    def get(self, name: str) -> ToolSpec:
        return self._tools[name]

    def names(self) -> tuple[str, ...]:
        return tuple(sorted(self._tools))
