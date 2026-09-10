import pytest

from nosai.mcp.bindings import RoleBindingRegistry
from nosai.mcp.roles import DEFAULT_EMPLOYEE_ROLES


def test_proposal_is_shadow_and_does_not_change_active_binding(tmp_path):
    registry = RoleBindingRegistry(tmp_path / "bindings.json", DEFAULT_EMPLOYEE_ROLES)
    employee_id = "employee.coding"
    before = registry.get(employee_id)
    proposal = registry.propose(employee_id, "qwen3-coder-30b", ["deepseek-v4-flash"])
    assert proposal["state"] == "shadow"
    assert registry.get(employee_id) == before


def test_promotion_requires_operator_and_all_independent_checks(tmp_path):
    registry = RoleBindingRegistry(tmp_path / "bindings.json", DEFAULT_EMPLOYEE_ROLES)
    proposal = registry.propose("employee.coding", "qwen3-coder-30b", [])
    with pytest.raises(PermissionError):
        registry.promote(proposal["proposal_id"], {"tests_passed": True, "shadow_passed": True, "audit_approved": True}, "not-operator")
    with pytest.raises(ValueError):
        registry.promote(proposal["proposal_id"], {"tests_passed": True, "shadow_passed": True}, "operator")
    active = registry.promote(
        proposal["proposal_id"],
        {"tests_passed": True, "shadow_passed": True, "audit_approved": True},
        "operator",
    )
    assert active["state"] == "active"
    assert active["primary_model"] == "qwen3-coder-30b"


def test_rollback_restores_previous_binding(tmp_path):
    registry = RoleBindingRegistry(tmp_path / "bindings.json", DEFAULT_EMPLOYEE_ROLES)
    original = registry.get("employee.coding")
    proposal = registry.propose("employee.coding", "qwen3-coder-30b", [])
    registry.promote(proposal["proposal_id"], {"tests_passed": True, "shadow_passed": True, "audit_approved": True}, "operator")
    restored = registry.rollback("employee.coding", "operator")
    assert restored["primary_model"] == original["primary_model"]
    assert restored["state"] == "active"


def test_role_router_prefers_promoted_binding(tmp_path):
    registry = RoleBindingRegistry(tmp_path / "bindings.json", DEFAULT_EMPLOYEE_ROLES)
    proposal = registry.propose("employee.coding", "qwen/qwen3-32b", [])
    registry.promote(proposal["proposal_id"], {"tests_passed": True, "shadow_passed": True, "audit_approved": True}, "operator")
    assert registry.get("employee.coding")["primary_model"] == "qwen/qwen3-32b"
