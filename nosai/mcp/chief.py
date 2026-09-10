from __future__ import annotations

from dataclasses import asdict
from pathlib import Path
from typing import Any, Mapping

from .auditor import McpAuditor
from .bindings import RoleBindingRegistry
from .director import McpDirector
from .evidence import EvidenceAuthority
from .health import HealthSupervisor
from .roles import DEFAULT_EMPLOYEE_ROLES, RoleArchitect


class McpChief:
    """Governance agent for MCP services; never an execution authority."""

    def __init__(
        self,
        root: Path | str,
        bindings: RoleBindingRegistry | None = None,
        router: Any | None = None,
    ) -> None:
        self.root = Path(root)
        self.root.mkdir(parents=True, exist_ok=True)
        self.bindings = bindings or RoleBindingRegistry(self.root / "role_bindings.json", DEFAULT_EMPLOYEE_ROLES)
        self.evidence: EvidenceAuthority = self.bindings.evidence
        self.health = HealthSupervisor(self.bindings.state)
        self.router = router
        self.director = McpDirector(self.root / "proposals")
        self.auditor = McpAuditor()

    @property
    def authority(self) -> dict[str, bool]:
        return {
            "health_monitoring": True,
            "configuration_proposals": True,
            "binding_promotion": True,
            "rollback": True,
            "evidence_recording": True,
            "direct_execution": False,
            "policy_override": False,
            "audit_override": False,
            "secret_export": False,
            "privileged_game_state": False,
        }

    def health_check(self) -> dict[str, Any]:
        checks: dict[str, dict[str, Any]] = {}
        role_errors = RoleArchitect.verify_employee_catalog(DEFAULT_EMPLOYEE_ROLES)
        checks["role_catalog"] = {"ok": not role_errors, "errors": role_errors, "count": len(DEFAULT_EMPLOYEE_ROLES)}
        try:
            bindings = self.bindings.list()
            checks["binding_store"] = {"ok": True, "count": len(bindings)}
        except (OSError, ValueError, KeyError) as exc:
            checks["binding_store"] = {"ok": False, "errors": [str(exc)]}
        try:
            evidence = self.evidence.list()
            checks["evidence_store"] = {"ok": True, "count": len(evidence)}
        except (OSError, ValueError, KeyError) as exc:
            checks["evidence_store"] = {"ok": False, "errors": [str(exc)]}
        checks["proposal_store"] = {"ok": self.director.root.is_dir(), "path": str(self.director.root)}
        health = self.health.snapshot()
        checks["health_supervisor"] = {"ok": True, "observations": len(health["observations"]), "state_revision": health["state_revision"]}
        if self.router is not None:
            try:
                catalog = self.router.catalog()
                enabled = [item for item in catalog if item.get("enabled")]
                checks["provider_catalog"] = {"ok": bool(enabled), "enabled": len(enabled), "total": len(catalog)}
            except (AttributeError, OSError, ValueError) as exc:
                checks["provider_catalog"] = {"ok": False, "errors": [str(exc)]}
        checks["policy_boundaries"] = {
            "ok": all(
                not self.authority[key]
                for key in ("direct_execution", "policy_override", "audit_override", "secret_export", "privileged_game_state")
            )
        }
        failed = [name for name, result in checks.items() if not result.get("ok")]
        return {
            "schema_version": "mcp.chief.health.v1",
            "status": "degraded" if failed else "healthy",
            "checks": checks,
            "failed_checks": failed,
            "authority": self.authority,
            "recommendations": self.recommendations(checks),
            "observations": health["observations"],
            "state_revision": health["state_revision"],
            "leases": self.bindings.state.snapshot()["leases"],
        }

    def observe_health(
        self,
        name: str,
        status: str,
        details: Mapping[str, Any] | None = None,
        *,
        observed_at: str | None = None,
        ttl_s: int | None = None,
    ) -> dict[str, Any]:
        return self.health.observe(name, status, details, observed_at=observed_at, ttl_s=ttl_s)

    def recommendations(self, checks: dict[str, dict[str, Any]]) -> list[str]:
        recommendations: list[str] = []
        if not checks.get("role_catalog", {}).get("ok", False):
            recommendations.append("repair employee role catalog before accepting binding changes")
        if not checks.get("binding_store", {}).get("ok", False):
            recommendations.append("restore or rollback the binding store before routing inference")
        if not checks.get("evidence_store", {}).get("ok", False):
            recommendations.append("restore the evidence store before promoting bindings")
        if not checks.get("provider_catalog", {}).get("ok", True):
            recommendations.append("restore or qualify at least one enabled provider before online inference")
        if not checks.get("policy_boundaries", {}).get("ok", False):
            recommendations.append("suspend MCP and restore immutable policy boundaries")
        if not recommendations:
            recommendations.append("continue periodic health checks and shadow qualification")
        return recommendations

    def propose_improvement(self, component: str, summary: str, files: list[str]) -> dict[str, Any]:
        proposal = self.director.propose(component, summary, files)
        return asdict(proposal)

    def audit_proposal(self, proposal: dict[str, Any], checks: dict[str, bool]) -> dict[str, Any]:
        from .director import ChangeProposal

        verdict = self.auditor.review(ChangeProposal(**proposal), checks)
        return asdict(verdict)

    def record_evidence(
        self,
        candidate_digest: str,
        evidence_kind: str,
        executor_id: str,
        result: Mapping[str, Any],
        *,
        signer_id: str = "mcp-evidence-authority",
        observed_at: str | None = None,
        ttl_s: int | None = None,
        test_version: str = "mcp-promotion-v1",
        dataset_digest: str | None = None,
        environment_digest: str | None = None,
        artifact_digest: str | None = None,
    ) -> dict[str, Any]:
        """Record digests and status for a worker without storing raw secrets."""
        zero = "sha256:" + "0" * 64
        return self.evidence.record(
            candidate_digest,
            test_version,
            dataset_digest or zero,
            executor_id,
            environment_digest or zero,
            artifact_digest or zero,
            result,
            signer_id,
            evidence_kind=evidence_kind,
            observed_at=observed_at,
            ttl_s=ttl_s,
        )

    def promote_binding(self, proposal_id: str, evidence_ids: dict[str, str], confirmation: str) -> dict[str, Any]:
        return self.bindings.promote(proposal_id, evidence_ids, confirmation)

    def rollback_binding(self, employee_id: str, confirmation: str) -> dict[str, Any]:
        return self.bindings.rollback(employee_id, confirmation)
