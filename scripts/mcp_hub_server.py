"""Entrypoint for the unified NosAi MCP server (nosai-mcp-unified)."""

import sys
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parent.parent
if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))

from nosai.mcp.unified_server import run


if __name__ == "__main__":
    run()

