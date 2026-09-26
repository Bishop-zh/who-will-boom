"""向 MCP for Blender 服务端发命令（纯 JSON over TCP，localhost:9876）。

用法：
    python Tools/Blender/mcp_client.py ping
    python Tools/Blender/mcp_client.py scene
    python Tools/Blender/mcp_client.py exec Tools/Blender/<脚本>.py

为什么需要它：
    CodeBuddy 侧的 blender MCP 工具需要重启客户端才会加载。
    这个脚本直接按协议跟 Blender 说话，所以在那之前也能驱动 Blender ——
    而且和 MCP 工具走的是同一条链路，可以做连通性验证。
"""

import argparse
import json
import socket
import sys

HOST = "127.0.0.1"
PORT = 9876


def send(command, host=HOST, port=PORT, timeout=600):
    """发一条命令并读回一个 JSON 响应。

    服务端是一个长连接处理器：回完响应不会关连接，所以不能读到 EOF 才返回 ——
    要「能解析出 JSON 就返回」。
    """
    sock = socket.create_connection((host, port), timeout=timeout)
    try:
        sock.sendall(json.dumps(command).encode("utf-8"))

        buffer = b""
        while True:
            chunk = sock.recv(65536)
            if not chunk:
                return None
            buffer += chunk
            try:
                return json.loads(buffer.decode("utf-8"))
            except (json.JSONDecodeError, UnicodeDecodeError):
                continue          # 还没收全，继续读
    finally:
        sock.close()


def main():
    parser = argparse.ArgumentParser(description="MCP for Blender 客户端")
    parser.add_argument("action", choices=["ping", "scene", "exec", "info"])
    parser.add_argument("path", nargs="?", help="exec 时要执行的 .py 路径")
    args = parser.parse_args()

    if args.action == "ping":
        command = {"type": "ping", "params": {}}
    elif args.action == "scene":
        command = {"type": "get_scene_info", "params": {}}
    elif args.action == "info":
        command = {"type": "get_addon_info", "params": {}}
    else:
        if not args.path:
            print("exec 需要给出脚本路径", file=sys.stderr)
            return 2
        with open(args.path, "r", encoding="utf-8") as handle:
            code = handle.read()
        command = {"type": "execute_code", "params": {"code": code}}

    try:
        response = send(command)
    except Exception as exc:                              # noqa: BLE001
        print(f"连接失败：{exc}")
        print("请确认 Blender 已打开，且 MCP 服务端处于 Connected 状态。")
        return 1

    print(json.dumps(response, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
