from nosai.mcp.chief import McpChief


def test_chief_health_report_is_structured_and_fail_closed(tmp_path):
    chief = McpChief(tmp_path)
    report = chief.health_check()
    assert report["schema_version"] == "mcp.chief.health.v1"
    assert report["authority"]["direct_execution"] is False
    assert "role_catalog" in report["checks"]
    assert report["status"] in {"healthy", "degraded"}


def test_chief_can_only_stage_unprotected_changes(tmp_path):
    chief = McpChief(tmp_path)
    proposal = chief.propose_improvement("router", "improve fallback ordering", ["nosai/mcp/router.py"])
    assert proposal["status"] == "proposed"
    try:
        chief.propose_improvement("policy", "disable guard", ["nosai/mcp/policy.py"])
    except PermissionError:
        pass
    else:
        raise AssertionError("chief must reject protected paths")


def test_chief_requires_audited_checks_before_promoting_binding(tmp_path):
    chief = McpChief(tmp_path)
    proposal = chief.bindings.propose("employee.coding", "qwen/qwen3-32b", [])
    try:
        chief.promote_binding(proposal["proposal_id"], {"tests_passed": True}, "operator")
    except ValueError:
        pass
    else:
        raise AssertionError("promotion must require all independent checks")
    result = chief.promote_binding(
        proposal["proposal_id"],
        {"tests_passed": True, "shadow_passed": True, "audit_approved": True},
        "operator",
    )
    assert result["state"] == "active"
