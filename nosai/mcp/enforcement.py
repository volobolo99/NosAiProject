from __future__ import annotations

from .roles import DEFAULT_EMPLOYEE_ROLES, EmployeeRole


class RoleEnforcementError(PermissionError):
    """A role requested a capability that is forbidden or not declared."""


def require_capability(
    employee_id: str | None,
    capability: str | None,
    *,
    employees: tuple[EmployeeRole, ...] = DEFAULT_EMPLOYEE_ROLES,
) -> None:
    """Block on forbidden/undeclared capabilities. No-op if either arg is empty
    (calls that today do not declare a role stay permissive, by design)."""
    if not employee_id or not capability:
        return

    employee = next((e for e in employees if e.employee_id == employee_id), None)
    if employee is None:
        raise RoleEnforcementError(f"unknown employee_id: {employee_id}")

    if capability in employee.forbidden:
        raise RoleEnforcementError(f"{employee_id} is forbidden to use capability: {capability}")

    if capability not in employee.capabilities:
        raise RoleEnforcementError(f"{employee_id} has no declared capability: {capability}")
