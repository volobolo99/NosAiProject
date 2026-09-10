"""Entrypoint for the dedicated local MCP control panel."""

from nosai.mcp.dashboard import serve


if __name__ == "__main__":
    raise SystemExit(serve())


