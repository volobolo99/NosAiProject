from nosai.mcp.chief import McpChief


def _evidence_ids(chief, candidate_digest):
    ids = {}
    for kind in ("tests", "shadow", "audit"):
        record = chief.record_evidence(
            candidate_digest,
            kind,
            "worker." + kind,
            {"status": "pass"},
        )
        ids[kind] = record["evidence_id"]
    return ids


def test_chief_health_report_is_structured_and_fail_closed(tmp_path):
    chief = McpChief(tmp_path)
    report = chief.health_check()
    assert report["schema_version"] == "mcp.chief.health.v1"
    assert report["authority"]["direct_execution"] is False
    assert "role_catalog" in report["checks"]
    assert report["status"] in {"healthy", "degraded"}


def test_chief_does_not_claim_provider_operational_from_catalog_only(tmp_path):
    class Router:
        def catalog(self):
            return [{"provider_id": "groq", "enabled": True}]

    chief = McpChief(tmp_path, router=Router())
    report = chief.health_check()
    assert report["checks"]["provider_catalog"]["ok"] is True
    assert report["checks"]["provider_health"]["ok"] is False
    assert report["checks"]["provider_health"]["states"]["groq"] == "UNKNOWN"


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


def test_chief_requires_independent_evidence_before_promoting_binding(tmp_path):
    chief = McpChief(tmp_path)
    proposal = chief.bindings.propose("employee.coding", "qwen/qwen3-32b", [], author_id="director")
    try:
        chief.promote_binding(proposal["proposal_id"], {"tests_passed": True}, "operator")
    except (TypeError, ValueError):
        pass
    else:
        raise AssertionError("promotion must require evidence ids")
    result = chief.promote_binding(
        proposal["proposal_id"],
        _evidence_ids(chief, proposal["candidate_digest"]),
        "operator",
    )
    assert result["state"] == "active"

