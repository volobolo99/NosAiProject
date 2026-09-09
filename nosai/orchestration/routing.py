"""Router dei modelli: assegna ogni tipo di lavoro al modello meno costoso capace
di completarlo.

Contratto: docs/contracts/orchestration_v1.json (ORCH-001).
Politica applicata: COST_POLICY.md. I prezzi che giustificano le classi di costo
stanno in scripts/model_prices.json, che resta l'unica fonte: la coerenza fra
questa tabella e quel listino e' verificata da tests/test_orchestration.py.
"""
from __future__ import annotations

from dataclasses import dataclass
from enum import Enum


class TaskKind(str, Enum):
    """Tipo di lavoro da instradare."""

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
    """Modelli disponibili al progetto."""

    LOCAL_QWEN25_CODER_7B = "qwen2.5-coder:7b"
    DEEPSEEK_V4_FLASH = "deepseek-v4-flash"
    QWEN3_CODER_30B = "qwen/qwen3-coder-30b-a3b-instruct"
    GEMINI_25_FLASH_LITE = "google/gemini-2.5-flash-lite"
    CLAUDE = "claude"


class CostClass(str, Enum):
    """Classe di costo relativa, derivata dai prezzi verificati."""

    ZERO = "zero"
    LOW = "low"
    MEDIUM = "medium"
    MAX = "max"


@dataclass(frozen=True)
class ModelRoute:
    """Destinazione di un task: quale modello, su quale canale, a che costo."""

    model: ModelId
    access: str
    cost_class: CostClass
    rationale: str


_ACCESS = {
    ModelId.LOCAL_QWEN25_CODER_7B: "Ollama locale",
    ModelId.DEEPSEEK_V4_FLASH: "API nativa DeepSeek",
    ModelId.QWEN3_CODER_30B: "OpenRouter",
    ModelId.GEMINI_25_FLASH_LITE: "OpenRouter",
    ModelId.CLAUDE: "Sessione Claude",
}

_COST = {
    ModelId.LOCAL_QWEN25_CODER_7B: CostClass.ZERO,
    ModelId.DEEPSEEK_V4_FLASH: CostClass.LOW,
    ModelId.QWEN3_CODER_30B: CostClass.LOW,
    ModelId.GEMINI_25_FLASH_LITE: CostClass.LOW,
    ModelId.CLAUDE: CostClass.MAX,
}

# Ordine delle classi: CostClass e' un'enumerazione di stringhe e il confronto
# lessicografico sarebbe sbagliato, "medium" precede "max" in ordine alfabetico.
_RANK = {CostClass.ZERO: 0, CostClass.LOW: 1, CostClass.MEDIUM: 2, CostClass.MAX: 3}

# Quando il modello assegnato non risolve il task, si sale a uno piu' capace.
# La catena non scende mai di classe di costo e si ferma a Claude.
_ESCALATION = {
    ModelId.LOCAL_QWEN25_CODER_7B: ModelId.QWEN3_CODER_30B,
    ModelId.DEEPSEEK_V4_FLASH: ModelId.QWEN3_CODER_30B,
    ModelId.GEMINI_25_FLASH_LITE: ModelId.CLAUDE,
    ModelId.QWEN3_CODER_30B: ModelId.CLAUDE,
    ModelId.CLAUDE: None,
}


def _route(model: ModelId, rationale: str) -> ModelRoute:
    return ModelRoute(
        model=model, access=_ACCESS[model], cost_class=_COST[model], rationale=rationale
    )


ROUTING_TABLE: dict[TaskKind, ModelRoute] = {
    TaskKind.DOCUMENTATION: _route(
        ModelId.LOCAL_QWEN25_CODER_7B, "Documentazione e file testuali: nessun ragionamento avanzato"
    ),
    TaskKind.SKELETON: _route(
        ModelId.LOCAL_QWEN25_CODER_7B, "Scheletri e firme: lavoro lungo e schematico, costo zero"
    ),
    TaskKind.SIMPLE_CODE: _route(
        ModelId.DEEPSEEK_V4_FLASH, "Codice semplice: competenza sufficiente senza spesa media"
    ),
    TaskKind.REPETITIVE_TEST: _route(
        ModelId.DEEPSEEK_V4_FLASH, "Test ripetitivi: lavoro meccanico su schema noto"
    ),
    TaskKind.INITIAL_DEBUG: _route(
        ModelId.DEEPSEEK_V4_FLASH, "Debug iniziale: prima analisi prima di salire di modello"
    ),
    TaskKind.LOG_ANALYSIS: _route(
        ModelId.DEEPSEEK_V4_FLASH, "Analisi ordinaria dei log: lettura di tracce, non progettazione"
    ),
    TaskKind.COMPLEX_CODE: _route(
        ModelId.QWEN3_CODER_30B, "Codice complesso: serve ragionamento da programmatore esperto"
    ),
    TaskKind.MODULE_INTEGRATION: _route(
        ModelId.QWEN3_CODER_30B, "Integrazione fra moduli: dipendenze e contratti incrociati"
    ),
    TaskKind.HARD_DEBUG: _route(
        ModelId.QWEN3_CODER_30B, "Debug difficile: causa non evidente dallo stack trace"
    ),
    TaskKind.STRUCTURAL_REFACTOR: _route(
        ModelId.QWEN3_CODER_30B, "Refactoring strutturale: tocca piu' moduli insieme"
    ),
    TaskKind.VISION: _route(
        ModelId.GEMINI_25_FLASH_LITE, "Analisi visiva dello schermo: modello multimodale economico"
    ),
    TaskKind.FAST_CLASSIFICATION: _route(
        ModelId.GEMINI_25_FLASH_LITE, "Classificazione rapida: latenza bassa e costo minimo"
    ),
    TaskKind.ARCHITECTURE: _route(
        ModelId.CLAUDE, "Architettura: decisione che nessun altro modello puo' assumere"
    ),
    TaskKind.CRITICAL_REVIEW: _route(
        ModelId.CLAUDE, "Revisione del codice critico: responsabilita' del ruolo di CTO"
    ),
}


def route_task(kind: TaskKind) -> ModelRoute:
    """Restituisce la rotta prevista per il tipo di lavoro indicato.

    Precondizione: kind e' un TaskKind valido, altrimenti solleva KeyError.
    Postcondizione: funzione pura e deterministica, stesso kind restituisce
    sempre la stessa rotta.
    """
    return ROUTING_TABLE[kind]


def escalate(kind: TaskKind, reason: str) -> ModelRoute:
    """Restituisce la rotta di risalita quando il modello assegnato non basta.

    Precondizione: reason non e' vuota, altrimenti solleva ValueError.
    Postcondizione: la classe di costo restituita non e' mai inferiore a quella
    della rotta base dello stesso kind.
    Postcondizione: se la rotta base e' gia' Claude solleva ValueError, perche'
    sopra Claude non esiste escalation.
    """
    if not reason or not reason.strip():
        raise ValueError("l'escalation richiede una motivazione non vuota")

    base = route_task(kind)
    successivo = _ESCALATION[base.model]
    if successivo is None:
        raise ValueError(
            "nessuna escalation possibile per {}: la rotta base e' gia' Claude".format(kind.value)
        )

    salita = _route(successivo, "escalation da {}: {}".format(base.model.value, reason.strip()))
    if _RANK[salita.cost_class] < _RANK[base.cost_class]:
        raise ValueError(
            "escalation incoerente: {} costa meno di {}".format(
                salita.model.value, base.model.value
            )
        )
    return salita
