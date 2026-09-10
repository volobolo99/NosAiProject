from __future__ import annotations

import json
from dataclasses import asdict, dataclass, field
from enum import Enum
from typing import Any, Mapping


class McpMode(str, Enum):
    OFFLINE = "offline"
    NETWORK = "network"


class ToolRisk(str, Enum):
    SAFE = "safe"
    OBSERVATION = "observation"
    NETWORK = "network"
    PRIVILEGED = "privileged"
    SECRET_EXPORT = "secret_export"


@dataclass(frozen=True)
class ActivationRequest:
    enabled: bool
    mode: McpMode = McpMode.NETWORK
    confirmation: str = "operator"

    @classmethod
    def from_mapping(cls, value: Mapping[str, Any]) -> "ActivationRequest":
        enabled = bool(value.get("enabled", False))
        raw_mode = value.get("mode")
        mode = McpMode(raw_mode) if raw_mode else (McpMode.NETWORK if enabled else McpMode.OFFLINE)
        confirmation = str(value.get("confirmation", "operator"))
        return cls(enabled=enabled, mode=mode, confirmation=confirmation)


@dataclass(frozen=True)
class SimulationRequest:
    scenario_id: str
    seed: int = 0
    horizon_ticks: int = 20
    actions: list[dict[str, Any]] = field(default_factory=list)

    @classmethod
    def from_mapping(cls, value: Mapping[str, Any]) -> "SimulationRequest":
        scenario_id = str(value.get("scenario_id", "")).strip()
        if not scenario_id:
            raise ValueError("scenario_id is required")
        seed = int(value.get("seed", 0))
        horizon = int(value.get("horizon_ticks", 20))
        if horizon < 1 or horizon > 10_000:
            raise ValueError("horizon_ticks must be between 1 and 10000")
        raw_actions = value.get("actions", [])
        if not isinstance(raw_actions, list) or not all(isinstance(item, dict) for item in raw_actions):
            raise ValueError("actions must be a list of objects")
        return cls(scenario_id=scenario_id, seed=seed, horizon_ticks=horizon, actions=list(raw_actions))

    def to_json(self) -> str:
        return json.dumps(asdict(self), ensure_ascii=False, sort_keys=True)


@dataclass(frozen=True)
class ProviderConfig:
    provider_id: str
    model_id: str
    tier: int
    enabled: bool = True
    network_required: bool = False
    capabilities: tuple[str, ...] = ()


@dataclass(frozen=True)
class RouteDecision:
    provider_id: str
    model_id: str
    reason: str
    network_used: bool


@dataclass(frozen=True)
class LearningCandidate:
    candidate_id: str
    topic: str
    payload: dict[str, Any]
    source: str
    evidence_count: int
    status: str = "candidate"


@dataclass(frozen=True)
class SecretMetadata:
    provider_id: str
    present: bool
    updated_at: str | None
    fingerprint: str | None


