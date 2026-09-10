from nosai.mcp.audit import AuditLog, redact


def test_audit_redacts_token_like_values(tmp_path):
    path = tmp_path / "audit.jsonl"
    AuditLog(path).append("test", {"api_key": "secret-value", "message": "token=abc"})
    text = path.read_text(encoding="utf-8")
    assert "secret-value" not in text
    assert "token=abc" not in text
    assert redact({"password": "x"})["password"] == "[REDACTED]"


