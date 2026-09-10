from nosai.mcp.auditor import McpAuditor
from nosai.mcp.director import McpDirector
from nosai.mcp.roles import RoleArchitect


def test_director_cannot_target_protected_paths(tmp_path):
    director = McpDirector(tmp_path)
    try:
        director.propose("x", "change", ["nosai/mcp/policy.py"])
    except PermissionError:
        pass
    else:
        raise AssertionError("protected path proposal must be rejected")


def test_auditor_vetoes_missing_safety_check(tmp_path):
    proposal = McpDirector(tmp_path).propose("x", "change", ["nosai/mcp/router.py"])
    verdict = McpAuditor().review(proposal, {"tests_passed": True, "contract_compatible": True})
    assert verdict.approved is False
    assert verdict.rollback_required is True


def test_role_architect_requires_director_and_auditor():
    assert RoleArchitect().verify() == []


