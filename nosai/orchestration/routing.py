from __future__ import annotations
from dataclasses import dataclass
from enum import Enum

__all__ = [
    "TaskKind",
    "ModelId",
    "CostClass",
    "ModelRoute",
    "ROUTING_TABLE",
    "route_task",
    "escalate",
]

class TaskKind(str, Enum):
    DOCUMENTATION = "documentation"
    SKELETON = "skeleton"
    SIMPLE_CODE = "simple_code"
    REPETITIVE_TEST = "repetitive_test"
    INITIAL_DEBUG = "initial_debug"
    LOG_ANALYSIS = "log_analysis"
    COMPLEX_CODE = "complex_code"
    MODULE_INTEGRATION = "module_integration"
    HARD_DEBUG = "hard_debug"
    STRUCTURAL_REFACTOR = "structural_refactor"
    VISION = "vision"
    FAST_CLASSIFICATION = "fast_classification"
    ARCHITECTURE = "architecture"
    CRITICAL_REVIEW = "critical_review"

class ModelId(str, Enum):
    LOCAL_QWEN25_CODER_7B = "qwen2.5-coder:7b"
    DEEPSEEK_V4_FLASH = "deepseek-v4-flash"
    QWEN3_CODER_30B = "qwen/qwen3-coder-30b-a3b-instruct"
    GEMINI_25_FLASH_LITE = "google/gemini-2.5-flash-lite"
    CLAUDE = "claude"

class CostClass(str, Enum):
    ZERO = "zero"
    LOW = "low"
    MEDIUM = "medium"
    MAX = "max"

@dataclass(frozen=True)
class ModelRoute:
    model: ModelId
    access: str
    cost_class: CostClass
    rationale: str

ROUTING_TABLE: dict[TaskKind, ModelRoute] = {}  # Popolare secondo COST_POLICY.md

def route_task(kind: TaskKind) -> ModelRoute:
    """Assegna un modello al task in base alla sua natura.

    Precondizione: kind è un TaskKind valido.
    Postcondizione: Restituisce sempre lo stesso ModelRoute per lo stesso kind.

    :param kind: Tipo di task da eseguire.
    :return: ModelRoute per eseguire il task.
    """
    raise NotImplementedError

def escalate(kind: TaskKind, reason: str) -> ModelRoute:
    """Escalona il task a un modello di costo superiore.

    Precondizione: reason non è vuota.
    Postcondizione: Non scende mai di classe di costo rispetto alla rotta base dello stesso kind.
    Postcondizione: Se la rotta base è già CLAUDE, solleva ValueError.

    :param kind: Tipo di task da escalare.
    :param reason: Motivo per l'escalazione.
    :return: ModelRoute per eseguire il task con classe di costo superiore.
    """
    raise NotImplementedError
