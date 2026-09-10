#!/usr/bin/env python3
"""Verify the machine-readable MCP contract/documentation surface.

This check is intentionally structural: it catches stale links, malformed JSON
and unresolved signatures that are accidentally marked as resolved.  It does
not claim that a build, provider, client or hardware environment has executed.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any


_PLACEHOLDER = re.compile(r"\b(da definire|da confermare|vedi il file|unknown|tbd)\b", re.IGNORECASE)
_PATH_LIKE = re.compile(r"^(?:[A-Za-z0-9_.-]+/)+[A-Za-z0-9_.-]+(?:\.[A-Za-z0-9_.-]+)?/?$")


def _load_json(path: Path, errors: list[str]) -> Any | None:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except FileNotFoundError:
        errors.append(f"missing JSON file: {path.as_posix()}")
    except (OSError, json.JSONDecodeError) as exc:
        errors.append(f"invalid JSON {path.as_posix()}: {exc}")
    return None


def _paths(value: Any) -> list[str]:
    if not isinstance(value, str):
        return []
    return [item.strip().strip("`") for item in value.split(";") if item.strip()]


def _is_concrete_path(value: str) -> bool:
    if _PLACEHOLDER.search(value):
        return False
    return bool(_PATH_LIKE.fullmatch(value))


def _check_contract_file(root: Path, value: str, field: str, cid: str, errors: list[str]) -> None:
    for item in _paths(value):
        if not _is_concrete_path(item):
            continue
        candidate = root / item
        if not candidate.exists():
            errors.append(f"{cid}: {field} does not exist: {item}")


def verify(root: Path | str) -> dict[str, Any]:
    """Return a deterministic structural verification report for ``root``."""

    root = Path(root)
    errors: list[str] = []
    warnings: list[str] = []
    ledger_path = root / "contracts" / "ledger.json"
    ledger = _load_json(ledger_path, errors)
    contract_ids: list[str] = []

    if isinstance(ledger, dict):
        gates = ledger.get("gates")
        if not isinstance(gates, list):
            errors.append("contracts/ledger.json: gates must be an array")
            gates = []
        for gate in gates:
            if not isinstance(gate, dict) or not isinstance(gate.get("contracts"), list):
                errors.append("contracts/ledger.json: each gate must contain a contracts array")
                continue
            for contract in gate["contracts"]:
                if not isinstance(contract, dict):
                    errors.append("contracts/ledger.json: contract entry must be an object")
                    continue
                cid = str(contract.get("cid", "")).strip()
                if not cid:
                    errors.append("contracts/ledger.json: contract is missing cid")
                    continue
                if cid in contract_ids:
                    errors.append(f"duplicate contract id: {cid}")
                contract_ids.append(cid)
                for field in ("target_file", "test_file"):
                    _check_contract_file(root, str(contract.get(field, "")), field, cid, errors)
                signature = str(contract.get("signature", ""))
                signature_status = str(contract.get("signature_status", "")).upper()
                if signature_status == "RESOLVED":
                    if not signature or _PLACEHOLDER.search(signature):
                        errors.append(f"{cid}: RESOLVED signature still contains a placeholder")
                    if not str(contract.get("signature_resolved_at", "")).strip():
                        errors.append(f"{cid}: RESOLVED signature has no signature_resolved_at")
                elif signature_status == "UNRESOLVED":
                    if not (contract.get("blocker") or contract.get("implementation_gate")):
                        errors.append(f"{cid}: UNRESOLVED signature has no blocker or implementation_gate")
                elif signature_status:
                    errors.append(f"{cid}: unsupported signature_status {signature_status!r}")
                elif _PLACEHOLDER.search(signature):
                    warnings.append(f"{cid}: placeholder signature has no explicit signature_status")

    # Every ledger CID must be discoverable from the compact human map.  The
    # map is navigation, while ledger.json remains authoritative for status.
    map_path = root / "docs" / "CONTRACT_MAP.md"
    try:
        contract_map = map_path.read_text(encoding="utf-8")
    except OSError:
        errors.append(f"missing contract map: {map_path.as_posix()}")
    else:
        for cid in contract_ids:
            if f"{cid} |" not in contract_map and f"`{cid}`" not in contract_map:
                errors.append(f"{cid}: missing from docs/CONTRACT_MAP.md")

    # Parse all JSON contracts and schemas so a malformed sidecar cannot be
    # silently ignored by an agent that only reads the ledger.
    for directory, pattern in ((root / "contracts", "*.json"), (root / "schemas", "*.schema.json")):
        if not directory.is_dir():
            warnings.append(f"optional directory not present: {directory.as_posix()}")
            continue
        for path in sorted(directory.glob(pattern)):
            _load_json(path, errors)

    return {
        "schema_version": "nosai.mcp.contract_verification.v1",
        "root": str(root),
        "contract_count": len(contract_ids),
        "errors": sorted(set(errors)),
        "warnings": sorted(set(warnings)),
        "ok": not errors,
    }


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path("."), help="repository root")
    parser.add_argument("--strict", action="store_true", help="return non-zero when errors are found")
    parser.add_argument("--output", type=Path, help="optional JSON report path")
    args = parser.parse_args(argv)
    report = verify(args.root)
    encoded = json.dumps(report, ensure_ascii=False, indent=2, sort_keys=True) + "\n"
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(encoded, encoding="utf-8")
    print(encoded, end="")
    return 1 if args.strict and not report["ok"] else 0


if __name__ == "__main__":
    raise SystemExit(main())

