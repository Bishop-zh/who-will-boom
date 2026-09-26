"""启动 MCP for Blender 服务端。

用法：
    blender.exe --python Tools/Blender/start_mcp_server.py

做两件事：
  1) 确保 "blender_mcp" 插件已启用
  2) 调用 blendermcp.start_server，让 Blender 在 localhost:9876 上监听

之后 MCP 客户端（mcp.json 里已配好的 blender 条目）就能连上。

为什么必须开着窗口：
    服务端把收到的命令塞进队列，由 Blender **主线程的 timer** 派发执行 ——
    这是为了避开 bpy 的线程安全问题。所以进程一退，服务就没了；
    也不能用 --background，那种模式下 timer 不会被驱动。
"""

import addon_utils
import bpy

ADDON = "blender_mcp"


def ensure_addon() -> bool:
    """按模块名启用插件。找得到才算成功。"""
    for module in addon_utils.modules():
        if module.__name__ == ADDON:
            try:
                addon_utils.enable(ADDON, default_set=True, persistent=True)
            except Exception as exc:                      # noqa: BLE001
                print(f"[MCP] 启用插件时出错：{exc}")
            return True
    print(f"[MCP] 没找到插件模块 {ADDON}，请确认它装在 addons 目录里")
    return False


def start_server() -> None:
    if not ensure_addon():
        return

    try:
        # 已在跑就不要再起一次，否则会 bind 失败
        if getattr(bpy.types, "blendermcp_server", None) and bpy.types.blendermcp_server.running:
            print("[MCP] 服务端已在运行")
            return

        bpy.ops.blendermcp.start_server()
        print("[MCP] 服务端已启动，监听 localhost:9876")
    except Exception as exc:                              # noqa: BLE001
        print(f"[MCP] 启动服务端失败：{exc}")


start_server()
