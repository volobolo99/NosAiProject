from __future__ import annotations

from dataclasses import dataclass

from .contracts import ActivationRequest, McpMode, ToolRisk


class PolicyViolation(RuntimeError):
    """Raised when a request would cross an immutable MCP boundary."""


@dataclass
class McpPolicy:
    mode: McpMode = McpMode.OFFLINE
    network_enabled: bool = False

    def activate_network(self, confirmation: str) -> None:
        if confirmation != "operator":
            raise PolicyViolation("network activation requires explicit operator confirmation")
        self.mode = McpMode.NETWORK
        self.network_enabled = True

    def apply(self, request: ActivationRequest) -> None:
        if not request.enabled or request.mode is McpMode.OFFLINE:
            self.deactivate_network()
            return
        self.activate_network(request.confirmation)

    def deactivate_network(self) -> None:
        self.mode = McpMode.OFFLINE
        self.network_enabled = False

    def authorize_tool(self, tool_name: str, risk: ToolRisk) -> bool:
        del tool_name
        if risk in (ToolRisk.PRIVILEGED, ToolRisk.SECRET_EXPORT):
            return False
        if risk is ToolRisk.NETWORK and not self.network_enabled:
            return False
        return True


