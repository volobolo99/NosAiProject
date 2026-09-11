"""Genera i file .claude/agents/*.md per i ruoli employee.* che hanno un
agent-type Claude Code dedicato. nosai.mcp.roles.DEFAULT_EMPLOYEE_ROLES resta
l'unica fonte dei fatti (purpose, capabilities, forbidden): questo script non
inventa nulla oltre alla mappa AGENT_SPECS, che e' una decisione di governance
(slug, model, tools, tipo A/B), non deducibile automaticamente dal ruolo.
"""
from __future__ import annotations
import sys
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parent.parent
if PROJECT_ROOT not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))

from nosai.mcp.roles import DEFAULT_EMPLOYEE_ROLES

# tipo "A": primary_model=claude, subagente pieno. tipo "B": primary_model
# non-Claude, guscio sottile che delega al tool MCP — mai Write/Edit, o
# l'isolamento del Punto 3/4 e' vanificato in silenzio.
AGENT_SPECS = {
    "employee.security": {"slug": "security-agent", "model": "sonnet", "tools": ["Read", "Grep", "Glob", "Bash"], "tipo": "A"},
    "employee.mcp_chief": {"slug": "mcp-chief", "model": "sonnet", "tools": ["mcp__nosai-mcp-hub__mcp_chief_health", "mcp__nosai-mcp-hub__mcp_chief_observe_health", "mcp__nosai-mcp-hub__mcp_chief_recommendations", "mcp__nosai-mcp-hub__mcp_role_catalog", "mcp__nosai-mcp-hub__mcp_verify_roles"], "tipo": "A"},
    "employee.coding": {"slug": "coding-agent", "model": "haiku", "tools": ["mcp__orchestrator__cloud_infill_implementation", "mcp__orchestrator__local_generate_skeleton", "Read"], "tipo": "B"},
    "employee.documentation": {"slug": "documentation-agent", "model": "haiku", "tools": ["Bash", "Read"], "tipo": "B"},
}


def _frontmatter(employee_id: str, spec: dict, purpose: str) -> str:
    description = purpose[:1].upper() + purpose[1:] + "."
    if spec["tipo"] == "B":
        description += " Non scrive codice, delega al tool MCP."

    tools_str = ", ".join(spec["tools"])

    return f"""---
name: {spec['slug']}
description: {description}
model: {spec['model']}
tools: {tools_str}
---
"""


def _corpo(employee_id: str, spec: dict, capabilities: tuple, forbidden: tuple) -> str:
    corpo = f"Ruolo stabile: `{employee_id}`.\n\n"
    corpo += "**Capabilities:**\n"
    for cap in capabilities:
        corpo += f"- {cap}\n"
    corpo += "\n"
    corpo += "**Vietato:**\n"
    for forbid in forbidden:
        corpo += f"- {forbid}\n"
    if spec["tipo"] == "B":
        corpo += "\n**Non usare Write o Edit**: il lavoro lo fa il modello delegato, non tu."
    return corpo


def generate(employees=DEFAULT_EMPLOYEE_ROLES, agents_dir: Path | None = None) -> list[Path]:
    if agents_dir is None:
        agents_dir = PROJECT_ROOT / ".claude" / "agents"

    agents_dir.mkdir(parents=True, exist_ok=True)
    scritti: list[Path] = []

    for employee in employees:
        if employee.employee_id in AGENT_SPECS:
            spec = AGENT_SPECS[employee.employee_id]
            contenuto = (
                _frontmatter(employee.employee_id, spec, employee.purpose)
                + "\n"
                + _corpo(employee.employee_id, spec, employee.capabilities, employee.forbidden)
                + "\n"
            )
            path = agents_dir / f"{spec['slug']}.md"
            path.write_text(contenuto, encoding="utf-8")
            scritti.append(path)

    return scritti


if __name__ == "__main__":
    for path in generate():
        print(path)
