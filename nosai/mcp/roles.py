from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class RoleDefinition:
    role_id: str
    purpose: str
    capabilities: tuple[str, ...]
    forbidden: tuple[str, ...] = ()


@dataclass(frozen=True)
class EmployeeRole:
    """Stable role identity; model bindings are replaceable configuration."""

    employee_id: str
    display_name: str
    purpose: str
    responsibilities: tuple[str, ...]
    primary_model: str
    fallback_models: tuple[str, ...]
    capabilities: tuple[str, ...]
    forbidden: tuple[str, ...] = ()
    approval_required: bool = True


DEFAULT_ROLES = (
    RoleDefinition("director", "propose MCP improvements", ("architecture", "optimization"), ("policy_override", "secret_export")),
    RoleDefinition("auditor", "independently verify changes", ("contract_review", "rollback"), ("self_approval",)),
    RoleDefinition("simulator", "evaluate tactics and models", ("simulation", "replay"), ("live_execution",)),
    RoleDefinition("learning", "promote observed evidence to offline skills", ("provenance", "validation"), ("unverified_promotion",)),
)


# Delivery roles are stable identities. Change a model binding here or in the
# operator configuration without changing the employee's memory, permissions or
# responsibilities.
DEFAULT_EMPLOYEE_ROLES = (
    EmployeeRole(
        "employee.orchestrator_cto", "Orchestrator/CTO",
        "governance and technical coordination",
        ("architecture", "task decomposition", "routing", "final approval"),
        "claude", ("qwen3-coder-30b",),
        ("architecture", "coordination", "routing"),
        ("policy_override", "secret_export", "direct_game_execution"),
    ),
    EmployeeRole(
        "employee.product_manager", "Product Manager",
        "product requirements and release scope",
        ("requirements", "roadmap", "acceptance criteria"),
        "qwen2.5-coder:7b", ("claude",),
        ("documentation", "requirements", "planning"),
        ("code_execution", "policy_override"),
    ),
    EmployeeRole(
        "employee.game_ai_architect", "Game AI Architect",
        "cognitive-layer boundaries and invariants",
        ("architecture", "ADR", "invariant review"),
        "claude", ("qwen3-coder-30b",),
        ("architecture", "contracts", "review"),
        ("direct_game_execution", "secret_export"),
    ),
    EmployeeRole(
        "employee.perception", "Perception Agent",
        "screen, network and local sensor interpretation",
        ("capture", "OCR/CV", "observation classification"),
        "gemini-2.5-flash-lite", ("qwen3-coder-30b", "qwen2.5-coder:7b"),
        ("vision", "observation", "provenance"),
        ("action_execution", "privileged_state"),
    ),
    EmployeeRole(
        "employee.world_model", "World Model Agent",
        "sensor fusion and semantic world state",
        ("fusion", "temporal belief", "entity identity"),
        "qwen3-coder-30b", ("deepseek-v4-flash",),
        ("world_model", "fusion", "determinism"),
        ("direct_game_execution", "privileged_state"),
    ),
    EmployeeRole(
        "employee.planning", "Planning Agent",
        "navigation and hierarchical plan generation",
        ("HTN/GOAP", "navigation", "replanning"),
        "qwen3-coder-30b", ("deepseek-v4-flash",),
        ("planning", "navigation", "simulation"),
        ("direct_game_execution", "safety_bypass"),
    ),
    EmployeeRole(
        "employee.decision", "Decision Agent",
        "candidate action ranking under uncertainty",
        ("utility", "risk ranking", "confidence handling"),
        "qwen3-coder-30b", ("deepseek-v4-flash",),
        ("ranking", "risk_analysis", "determinism"),
        ("direct_game_execution", "safety_bypass"),
    ),
    EmployeeRole(
        "employee.action", "Action Agent",
        "authorized actuation and post-condition verification",
        ("action dispatch", "receipt", "post-condition"),
        "deepseek-v4-flash", ("qwen3-coder-30b",),
        ("actuation", "verification", "recovery"),
        ("policy_override", "safety_bypass", "privileged_state"),
    ),
    EmployeeRole(
        "employee.memory", "Memory Agent",
        "episodic, semantic and procedural memory",
        ("persistence", "retrieval", "outcome ledger"),
        "deepseek-v4-flash", ("qwen3-coder-30b",),
        ("memory", "provenance", "replay"),
        ("secret_export", "unverified_promotion"),
    ),
    EmployeeRole(
        "employee.coding", "Coding Agent",
        "implementation inside an assigned ownership set",
        ("implementation", "refactoring", "unit tests"),
        "deepseek-v4-flash", ("qwen3-coder-30b",),
        ("coding", "testing", "debugging"),
        ("architecture_override", "protected_path_write"),
    ),
    EmployeeRole(
        "employee.testing", "Testing Agent",
        "repeatable tests, benchmarks and failure classification",
        ("unit tests", "integration tests", "benchmarking"),
        "deepseek-v4-flash", ("qwen3-coder-30b",),
        ("testing", "diagnostics", "reproducibility"),
        ("production_write", "safety_bypass"),
    ),
    EmployeeRole(
        "employee.security", "Security Agent",
        "security boundaries and sensitive-change review",
        ("threat modeling", "secret handling", "permission review"),
        "claude", ("deepseek-v4-flash",),
        ("security", "audit", "policy"),
        ("secret_export", "self_approval"),
    ),
    EmployeeRole(
        "employee.reviewer", "Reviewer Agent",
        "independent review before integration",
        ("diff review", "regression review", "contract compliance"),
        "claude", ("deepseek-v4-flash",),
        ("review", "contracts", "quality"),
        ("self_approval", "direct_game_execution"),
    ),
    EmployeeRole(
        "employee.documentation", "Documentation Agent",
        "canonical documentation and routing indexes",
        ("docs", "changelog", "contract map", "summaries"),
        "qwen2.5-coder:7b", ("deepseek-v4-flash",),
        ("documentation", "indexing", "provenance"),
        ("code_execution", "secret_export"),
    ),
)


class RoleArchitect:
    def __init__(self, roles: tuple[RoleDefinition, ...] = DEFAULT_ROLES):
        self.roles = roles

    def verify(self) -> list[str]:
        errors: list[str] = []
        ids = [role.role_id for role in self.roles]
        if len(ids) != len(set(ids)):
            errors.append("duplicate role_id")
        for role in self.roles:
            if not role.purpose.strip() or not role.capabilities:
                errors.append(f"incomplete role: {role.role_id}")
        if "director" not in ids or "auditor" not in ids:
            errors.append("director and auditor roles are mandatory")
        return errors

    @staticmethod
    def verify_employee_catalog(
        employees: tuple[EmployeeRole, ...] = DEFAULT_EMPLOYEE_ROLES,
    ) -> list[str]:
        errors: list[str] = []
        ids = [employee.employee_id for employee in employees]
        if len(ids) != len(set(ids)):
            errors.append("duplicate employee_id")
        required = {"employee.orchestrator_cto", "employee.security", "employee.reviewer"}
        missing = sorted(required.difference(ids))
        if missing:
            errors.append("missing mandatory employees: " + ", ".join(missing))
        for employee in employees:
            if not employee.display_name.strip() or not employee.purpose.strip():
                errors.append(f"incomplete employee: {employee.employee_id}")
            if not employee.responsibilities or not employee.capabilities:
                errors.append(f"employee lacks responsibilities/capabilities: {employee.employee_id}")
            if not employee.primary_model.strip():
                errors.append(f"employee lacks primary_model: {employee.employee_id}")
            if employee.primary_model in employee.fallback_models:
                errors.append(f"primary model repeated as fallback: {employee.employee_id}")
        return errors
