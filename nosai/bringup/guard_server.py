"""Minimal Play Guard TCP endpoint for Wi-Fi bring-up.

NON-CANONICAL. ADR-0006 makes GuardAiNetworkChannel (NOSA binary framing,
RSA-2048 challenge/response, TCP/17471) the only canonical PC <-> phone channel.
This endpoint has no authentication and the Gate 1 runtime does not speak its
protocol, so a phone client built against it cannot reach the runtime. It is kept
for local transport experiments only and proves nothing about Gate 1.

Binds to 127.0.0.1 by default for development. For a real phone connection,
pass the PC's LAN address explicitly and keep the firewall scoped to the
trusted home/LAN network. The server only exchanges protocol messages.
"""
from __future__ import annotations

import argparse
import socket
import uuid

from .protocol import Message, PROTOCOL_VERSION, capabilities, hello, heartbeat, status


def serve(host: str, port: int) -> None:
    with socket.create_server((host, port), reuse_port=False) as server:
        print(f"Play Guard bring-up listening on {host}:{port} (protocol {PROTOCOL_VERSION})")
        while True:
            conn, address = server.accept()
            with conn:
                session_id = uuid.uuid4().hex
                seq = 1
                conn.sendall(hello(session_id, seq, "play_guard").encode())
                seq += 1
                conn.sendall(capabilities(session_id, seq, ["heartbeat", "status"]).encode())
                seq += 1
                conn_file = conn.makefile("rb")
                try:
                    for raw in conn_file:
                        msg = Message.decode(raw)
                        if msg.session_id != session_id:
                            conn.sendall(status(session_id, seq, "REJECTED", "session_id mismatch").encode())
                            seq += 1
                            break
                        if msg.type == "HELLO":
                            conn.sendall(status(session_id, seq, "CONNECTED", f"peer={address[0]}").encode())
                            seq += 1
                        elif msg.type == "HEARTBEAT":
                            conn.sendall(heartbeat(session_id, seq).encode())
                            seq += 1
                        elif msg.type == "STATUS":
                            conn.sendall(status(session_id, seq, "ACK", "status received").encode())
                            seq += 1
                        else:
                            conn.sendall(status(session_id, seq, "REJECTED", f"unsupported type={msg.type}").encode())
                            seq += 1
                except (ConnectionError, ValueError, OSError):
                    pass


def main() -> None:
    parser = argparse.ArgumentParser(description="NosAi minimal Play Guard Wi-Fi bring-up server")
    parser.add_argument("--host", default="127.0.0.1")
    # Not 8765: that port belongs to the Python operator UI, and sharing it meant
    # whichever process started second could not bind.
    parser.add_argument("--port", type=int, default=8769)
    args = parser.parse_args()
    serve(args.host, args.port)


if __name__ == "__main__":
    main()
