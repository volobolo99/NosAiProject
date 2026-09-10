from __future__ import annotations

import json
import uuid
from dataclasses import asdict, dataclass
from pathlib import Path


PROTECTED_PREFIXES = ("nosai/mcp/policy.py", "nosai/mcp/audit.py", "schemas/", "contracts/", "tests/")


@dataclass(frozen=True)
class ChangeProposal:
    proposal_id: str
    component: str
    summary: str
    files: tuple[str, ...]
    status: str = "proposed"


class McpDirector:
    def __init__(self, root: Path | str):
        self.root = Path(root)
        self.root.mkdir(parents=True, exist_ok=True)

    def propose(self, component: str, summary: str, files: list[str]) -> ChangeProposal:
        if not component.strip() or not summary.strip() or not files:
            raise ValueError("component, summary and files are required")
        normalized = tuple(str(path).replace("\\", "/") for path in files)
        protected = [path for path in normalized if path.startswith(PROTECTED_PREFIXES)]
        if protected:
            raise PermissionError("Director cannot propose changes to protected paths: " + ", ".join(protected))
        proposal = ChangeProposal(uuid.uuid4().hex, component, summary, normalized)
        target = self.root / (proposal.proposal_id + ".json")
        target.write_text(json.dumps(asdict(proposal), ensure_ascii=False, indent=2), encoding="utf-8")
        return proposal


