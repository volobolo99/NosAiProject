#!/usr/bin/env python3
from __future__ import annotations
import argparse
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from nosai.mcp.chief import McpChief
from nosai.mcp.chief_watchdog import run_health_tick, read_recent_ticks
from nosai.mcp.config import load_config
from nosai.mcp.policy import McpPolicy
from nosai.mcp.router import ModelRouter

def main(argv: "list[str] | None" = None) -> int:
    parser = argparse.ArgumentParser(
        description="MCP Chief watchdog utility"
    )
    parser.add_argument(
        "--tail",
        type=int,
        default=None,
        help="Show the last N health‑check ticks (default: run a new tick)",
    )
    args = parser.parse_args(argv)

    # Log file location (relative to project root)
    log_path = ROOT / "data" / "mcp" / "chief_watchdog.jsonl"

    if args.tail is None:
        # Build the usual MCP stack (policy, router, chief) exactly as the server does
        config = load_config()
        policy = McpPolicy()
        # ModelRouter.from_config requires the policy as a second argument
        router = ModelRouter.from_config(config, policy)
        chief = McpChief(
            Path(config["role_bindings_path"]).parent,
            bindings=router.role_bindings,
            router=router,
        )

        # Run a single health‑check tick and output the record
        record = run_health_tick(chief, log_path)
        print(json.dumps(record, ensure_ascii=False))
        return 0 if record.get("status") == "healthy" else 1
    else:
        # Read the most recent N ticks and output them as a JSON list
        ticks = read_recent_ticks(log_path, limit=args.tail)
        print(json.dumps(ticks, ensure_ascii=False))
        return 0

if __name__ == '__main__':
    raise SystemExit(main())
