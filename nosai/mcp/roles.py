from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class RoleDefinition:
    role_id: str
    purpose: str
    capabilities: tuple[str, ...]
    forbidden: tuple[str, ...] = ()


DEFAULT_ROLES = (
    RoleDefinition("director", "propose MCP improvements", ("architecture", "optimization"), ("policy_override", "secret_export")),
    RoleDefinition("auditor", "independently verify changes", ("contract_review", "rollback"), ("self_approval",)),
    RoleDefinition("simulator", "evaluate tactics and models", ("simulation", "replay"), ("live_execution",)),
    RoleDefinition("learning", "promote observed evidence to offline skills", ("provenance", "validation"), ("unverified_promotion",)),
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


