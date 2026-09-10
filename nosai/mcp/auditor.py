from __future__ import annotations

from dataclasses import dataclass

from .director import ChangeProposal, PROTECTED_PREFIXES


@dataclass(frozen=True)
class AuditVerdict:
    proposal_id: str
    approved: bool
    reason: str
    rollback_required: bool


class McpAuditor:
    """Independent deterministic gate; it never trusts Director status."""

    def review(self, proposal: ChangeProposal, checks: dict[str, bool]) -> AuditVerdict:
        if any(path.startswith(PROTECTED_PREFIXES) for path in proposal.files):
            return AuditVerdict(proposal.proposal_id, False, "protected path", True)
        required = ("tests_passed", "contract_compatible", "safety_unchanged")
        missing = [name for name in required if checks.get(name) is not True]
        if missing:
            return AuditVerdict(proposal.proposal_id, False, "failed checks: " + ", ".join(missing), True)
        return AuditVerdict(proposal.proposal_id, True, "all independent checks passed", False)


