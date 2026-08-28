from __future__ import annotations

from pathlib import Path

import pytest

from nosai.runtime.replay import ReplayBuffer, ReplayTransition


def transition(index: int) -> ReplayTransition:
    return ReplayTransition(
        state={"tick": index},
        action="observe",
        reward=float(index),
        next_state={"tick": index + 1},
        info={"source": "test"},
    )


def test_capacity_keeps_newest_items() -> None:
    buffer = ReplayBuffer(capacity=2)
    for index in range(3):
        buffer.add(transition(index))
    assert len(buffer) == 2
    assert [item.state["tick"] for item in buffer.recent()] == [1, 2]


def test_sampling_is_deterministic_for_same_seed() -> None:
    first = ReplayBuffer(capacity=10, seed=7)
    second = ReplayBuffer(capacity=10, seed=7)
    for index in range(5):
        item = transition(index)
        first.add(item)
        second.add(item)
    assert first.sample(3) == second.sample(3)


def test_jsonl_round_trip(tmp_path: Path) -> None:
    path = tmp_path / "replay.jsonl"
    original = ReplayBuffer(capacity=10)
    original.add(transition(1))
    original.add(transition(2))

    original.save_jsonl(path)
    restored = ReplayBuffer(capacity=10)
    assert restored.load_jsonl(path) == 2
    assert restored.recent() == original.recent()


def test_invalid_arguments_fail_closed() -> None:
    with pytest.raises(ValueError):
        ReplayBuffer(capacity=0)
    buffer = ReplayBuffer()
    with pytest.raises(ValueError):
        buffer.sample(0)
