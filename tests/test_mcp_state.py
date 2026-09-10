import json

import pytest

from nosai.mcp.state import ConcurrentUpdateError, McpStateStore


def _proposal():
    return {
        "proposal_id": "proposal-1",
        "employee_id": "employee.coding",
        "primary_model": "model-a",
        "fallback_models": ["model-b"],
        "candidate_digest": "sha256:" + "a" * 64,
        "author_id": "director",
        "state": "shadow",
        "created_at": "2026-09-10T00:00:00+00:00",
    }


def test_two_store_instances_observe_one_monotonic_revision(tmp_path):
    path = tmp_path / "state.sqlite3"
    first = McpStateStore(path)
    second = McpStateStore(path)
    staged = first.stage_binding(_proposal(), expected_revision=0)

    assert staged["revision"] == 1
    assert second.snapshot()["revision"] == 1
    assert second.snapshot()["proposals"][0]["proposal_id"] == "proposal-1"


def test_stale_stage_is_rejected_without_partial_write(tmp_path):
    store = McpStateStore(tmp_path / "state.sqlite3")
    store.stage_binding(_proposal(), expected_revision=0)

    newer = dict(_proposal(), proposal_id="proposal-2")
    with pytest.raises(ConcurrentUpdateError):
        store.stage_binding(newer, expected_revision=0)
    snapshot = store.snapshot()
    assert snapshot["revision"] == 1
    assert len(snapshot["proposals"]) == 1


def test_promotion_is_atomic_and_idempotent(tmp_path):
    store = McpStateStore(tmp_path / "state.sqlite3")
    staged = store.stage_binding(_proposal(), expected_revision=0)
    promoted = store.promote_binding(
        "proposal-1",
        {"tests": "e1", "shadow": "e2", "audit": "e3"},
        expected_revision=staged["revision"],
        idempotency_key="promote-1",
    )
    repeated = store.promote_binding(
        "proposal-1",
        {"tests": "e1", "shadow": "e2", "audit": "e3"},
        expected_revision=0,
        idempotency_key="promote-1",
    )

    assert promoted == repeated
    assert store.snapshot()["bindings"][0]["state"] == "active"


def test_json_migration_creates_backup_and_survives_restart(tmp_path):
    legacy = tmp_path / "bindings.json"
    legacy.write_text(
        json.dumps(
            {
                "schema_version": "mcp.role_bindings.v1",
                "bindings": {
                    "employee.coding": {
                        "primary_model": "model-a",
                        "fallback_models": ["model-b"],
                        "version": 3,
                    }
                },
                "proposals": {},
            }
        ),
        encoding="utf-8",
    )
    store = McpStateStore(tmp_path / "state.sqlite3")
    migrated = store.migrate_json(legacy)
    reopened = McpStateStore(tmp_path / "state.sqlite3")

    assert migrated is True
    assert list(tmp_path.glob("bindings.json.bak.*"))
    assert reopened.snapshot()["bindings"][0]["primary_model"] == "model-a"
    assert reopened.snapshot()["bindings"][0]["version"] == 3


def test_expired_lease_is_fenced_and_new_worker_gets_token(tmp_path):
    store = McpStateStore(tmp_path / "state.sqlite3")
    first = store.acquire_lease("task-1", "worker-a", ttl_s=1)
    with pytest.raises(ConcurrentUpdateError):
        store.acquire_lease("task-1", "worker-b", ttl_s=60, now=first["expires_at"])
    second = store.acquire_lease("task-1", "worker-b", ttl_s=60, now="2030-01-01T00:00:00+00:00")
    assert second["worker_id"] == "worker-b"
    assert second["fencing_token"] > first["fencing_token"]
    with pytest.raises(PermissionError):
        store.release_lease("task-1", "worker-a", first["fencing_token"])
