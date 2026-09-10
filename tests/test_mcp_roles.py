from nosai.mcp.roles import DEFAULT_EMPLOYEE_ROLES, RoleArchitect


def test_employee_catalog_has_unique_stable_roles_and_bindings():
    assert RoleArchitect.verify_employee_catalog(DEFAULT_EMPLOYEE_ROLES) == []
    assert len({employee.employee_id for employee in DEFAULT_EMPLOYEE_ROLES}) == len(DEFAULT_EMPLOYEE_ROLES)
    assert all(employee.primary_model for employee in DEFAULT_EMPLOYEE_ROLES)


def test_governance_roles_remain_valid():
    assert RoleArchitect().verify() == []
