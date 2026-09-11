import pytest

from nosai.mcp.enforcement import RoleEnforcementError, require_capability
from nosai.mcp.roles import DEFAULT_EMPLOYEE_ROLES


def test_forbidden_capability_is_blocked():
    with pytest.raises(RoleEnforcementError):
        require_capability("employee.security", "secret_export")


def test_undeclared_capability_is_blocked():
    with pytest.raises(RoleEnforcementError):
        require_capability("employee.security", "coding")


def test_declared_capability_is_allowed():
    require_capability("employee.security", "audit")


def test_unknown_employee_is_blocked():
    with pytest.raises(RoleEnforcementError):
        require_capability("employee.does_not_exist", "audit")


@pytest.mark.parametrize("employee_id,capability", [(None, "audit"), ("", "audit"), ("employee.security", None), ("employee.security", "")])
def test_empty_role_or_capability_is_a_noop(employee_id, capability):
    require_capability(employee_id, capability)


def test_forbidden_wins_even_if_never_declared_as_capability():
    """forbidden e' controllato per primo: non basta che una capability manchi
    da entrambi gli elenchi per considerarla autorizzata implicitamente."""
    employee = next(e for e in DEFAULT_EMPLOYEE_ROLES if e.employee_id == "employee.reviewer")
    assert "self_approval" in employee.forbidden
    with pytest.raises(RoleEnforcementError):
        require_capability("employee.reviewer", "self_approval")
