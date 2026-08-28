"""Perception-to-game-state evaluation boundary."""
from __future__ import annotations
from dataclasses import dataclass
from .contracts import PerceptionSnapshot

@dataclass(frozen=True)
class EvaluatedGameState:
    timestamp: float
    player_hp_pct: float | None
    player_mp_pct: float | None
    target_hp_pct: float | None
    enemy_count: int
    ocr_text: tuple[str, ...]

class GameStateEvaluator:
    """Converts perception output into a stable semantic snapshot."""
    def evaluate(self, snapshot: PerceptionSnapshot) -> EvaluatedGameState:
        enemies = sum(1 for e in snapshot.entities if e.label.lower() in {"enemy", "monster", "mob"})
        return EvaluatedGameState(
            timestamp=snapshot.timestamp,
            player_hp_pct=snapshot.player_hp_pct,
            player_mp_pct=snapshot.player_mp_pct,
            target_hp_pct=snapshot.target_hp_pct,
            enemy_count=enemies,
            ocr_text=tuple(r.text for r in snapshot.ocr if r.text),
        )
