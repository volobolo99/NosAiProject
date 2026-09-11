import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "scripts"))

import generate_claude_agents as gen


def test_generate_writes_exactly_the_specced_agents(tmp_path):
    written = gen.generate(agents_dir=tmp_path)
    slugs = {p.stem for p in written}
    assert slugs == {"security-agent", "mcp-chief", "coding-agent", "documentation-agent"}
    for path in written:
        assert path.exists()


def test_type_b_agents_never_grant_write_or_edit(tmp_path):
    """Un guscio sottile con Write/Edit vanificherebbe l'isolamento in silenzio:
    il tool decide, il modello delegato esegue, non Claude in prima persona."""
    gen.generate(agents_dir=tmp_path)
    for employee_id, spec in gen.AGENT_SPECS.items():
        if spec["tipo"] != "B":
            continue
        content = (tmp_path / f"{spec['slug']}.md").read_text(encoding="utf-8")
        tools_line = next(line for line in content.splitlines() if line.startswith("tools:"))
        tools = {t.strip() for t in tools_line.removeprefix("tools:").split(",")}
        assert "Write" not in tools and "Edit" not in tools, f"{employee_id} type B grants Write/Edit"


def test_each_generated_agent_names_its_stable_employee_id(tmp_path):
    written = gen.generate(agents_dir=tmp_path)
    by_slug = {p.stem: p for p in written}
    for employee_id, spec in gen.AGENT_SPECS.items():
        content = by_slug[spec["slug"]].read_text(encoding="utf-8")
        assert f"`{employee_id}`" in content


def test_mcp_chief_cannot_promote_or_rollback_bindings(tmp_path):
    """Nessuna promozione senza conferma operatore esplicita, anche a livello
    di tooling: il frontmatter dell'agent-type non deve concedere questi tool."""
    gen.generate(agents_dir=tmp_path)
    content = (tmp_path / "mcp-chief.md").read_text(encoding="utf-8")
    assert "mcp_role_promote_binding" not in content
    assert "mcp_role_rollback_binding" not in content
