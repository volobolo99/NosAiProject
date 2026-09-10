"""Entrypoint for the dedicated local MCP control panel."""

import sys
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parent.parent
if str(PROJECT_ROOT) not in sys.path:
    sys.path.insert(0, str(PROJECT_ROOT))

from nosai.mcp.dashboard import serve


if __name__ == "__main__":
    raise SystemExit(serve())

